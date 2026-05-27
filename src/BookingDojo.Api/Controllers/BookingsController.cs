using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly BookingDojoDbContext _db;

    public BookingsController(BookingDojoDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyBookings([FromQuery] int page = 1)
    {
        const int PageSize = 10;
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var isAdmin = User.FindFirstValue("role") == "AdminUser";

        var query = _db.Bookings
            .Include(b => b.Hotel)
            .Where(b => isAdmin || b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt);

        var total = await query.CountAsync();
        var bookings = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return Ok(new
        {
            results = bookings.Select(b => ToDto(b, b.Hotel.Name)),
            total,
            page,
            pageSize = PageSize,
            totalPages = (int)Math.Ceiling((double)total / PageSize),
        });
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchBookings(
        [FromQuery] string q = "",
        [FromQuery] int? pageSize = null)
    {
        int usedPageSize;
        if (!pageSize.HasValue || pageSize.Value > 20)
        {
            usedPageSize = 20;
        }
        else
        {
            usedPageSize = pageSize.Value;
        }

        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var isAdmin = User.FindFirstValue("role") == "AdminUser";

        // VULNERABLE PATH (SQL injection)
        // q is concatenated directly into raw SQL — injectable.
        // No LIMIT clause: every matching row is read into memory.
        var sql = "SELECT b.\"Id\", b.\"UserId\", b.\"Username\", b.\"HotelId\", b.\"CardLastFour\"," +
          " b.\"CheckIn\", b.\"CheckOut\", b.\"SpecialRequests\", b.\"TotalPrice\", b.\"CreatedAt\"," +
          " h.\"Name\" AS \"HotelName\"" +
          " FROM bookingdojo.\"Bookings\" b" +
          " JOIN bookingdojo.\"Hotels\" h ON b.\"HotelId\" = h.\"Id\"" +
          " WHERE (@isAdmin = TRUE OR b.\"UserId\" = @userId) AND h.\"Name\" ILIKE @hotelName" +
          " ORDER BY b.\"CreatedAt\" DESC" +
          " LIMIT @usedPageSize";

        List<BookingDto> results = new();
        var conn = _db.Database.GetDbConnection();
        var needsClose = conn.State != ConnectionState.Open;
        if (needsClose) await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            // Add parameters
            var isAdminParam = cmd.CreateParameter();
            isAdminParam.ParameterName = "@isAdmin";
            isAdminParam.Value = isAdmin;
            isAdminParam.DbType = DbType.Boolean;
            cmd.Parameters.Add(isAdminParam);

            var userIdParam = cmd.CreateParameter();
            userIdParam.ParameterName = "@userId";
            userIdParam.Value = userId;
            userIdParam.DbType = DbType.Guid;
            cmd.Parameters.Add(userIdParam);

            var hotelNameParam = cmd.CreateParameter();
            hotelNameParam.ParameterName = "@hotelName";
            hotelNameParam.Value = $"%{q}%";
            hotelNameParam.DbType = DbType.String;
            cmd.Parameters.Add(hotelNameParam);

            var pageSizeParam = cmd.CreateParameter();
            pageSizeParam.ParameterName = "@usedPageSize";
            pageSizeParam.Value = usedPageSize;
            pageSizeParam.DbType = DbType.Int32;
            cmd.Parameters.Add(pageSizeParam);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new BookingDto(
                    reader.GetInt32(reader.GetOrdinal("Id")),
                    reader.GetGuid(reader.GetOrdinal("UserId")),
                    reader.GetString(reader.GetOrdinal("Username")),
                    reader.GetGuid(reader.GetOrdinal("HotelId")),
                    reader.GetString(reader.GetOrdinal("HotelName")),
                    reader.GetDateTime(reader.GetOrdinal("CheckIn")),
                    reader.GetDateTime(reader.GetOrdinal("CheckOut")),
                    reader.GetString(reader.GetOrdinal("CardLastFour")),
                    null, // CardNumber — not in base SELECT; craft a UNION to expose it
                    null, // CardToken
                    reader.IsDBNull(reader.GetOrdinal("SpecialRequests"))
                        ? "" : reader.GetString(reader.GetOrdinal("SpecialRequests")),
                    reader.GetDecimal(reader.GetOrdinal("TotalPrice")),
                    reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                ));
            }
        }
        catch(Exception ex)
        {
            // In a real application, log the exception and return a generic error message.
            return StatusCode(500, new { message = "An error occurred while processing your request.", details = ex.Message });
        }
        finally
        {
            if (needsClose) await conn.CloseAsync();
        }

        // VULNERABLE PATH (resource consumption)
        // The client-supplied pageSize is honoured unconditionally — the server places
        // no upper bound. A caller can omit it (returns every row) or set it to
        // Integer.MaxValue to retrieve the entire table in a single request.
        var truncated = true;
        /*if (usedPageSize > 0 && results.Count > usedPageSize)
        {
            results = results.Take(usedPageSize).ToList();
            truncated = true;
        }*/

        return Ok(new { results, truncated, appliedPageSize = usedPageSize });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBookingById(int id)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var isAdmin = User.FindFirstValue("role") == "AdminUser";

        var booking = await _db.Bookings
            .Include(b => b.Hotel)
            .Where(b => isAdmin || b.UserId == userId)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking == null)
            return NotFound();

        return Ok(ToDto(booking, booking.Hotel.Name));
    }

    private static BookingDto ToDto(Booking b, string hotelName) => new(
        b.Id, b.UserId, b.Username, b.HotelId, hotelName,
        b.CheckIn, b.CheckOut, b.CardLastFour, b.CardNumber, b.CardToken,
        b.SpecialRequests, b.TotalPrice, b.CreatedAt);
}
