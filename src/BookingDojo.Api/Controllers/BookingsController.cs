using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Authorization;
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

    [HttpPost]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest request)
    {
        var userId   = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var username = User.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "unknown";

        var hotel = await _db.Hotels.FindAsync(request.HotelId);
        if (hotel == null || !hotel.IsActive)
            return BadRequest(new { message = "Hotel not found or inactive" });

        if (request.CardNumber.Length < 13 || request.CardNumber.Length > 19 || !request.CardNumber.All(char.IsDigit))
            return BadRequest(new { message = "cardNumber must be 13–19 digits" });

        var (lastFour, storedCardNumber, cardToken) = Tokenize(request.CardNumber);

        var nights = (int)(request.CheckOut - request.CheckIn).TotalDays;
        var totalPrice = Math.Round(hotel.PricePerNight * nights, 2);

        var booking = new Booking
        {
            UserId          = userId,
            Username        = username,
            HotelId         = request.HotelId,
            CheckIn         = DateTime.SpecifyKind(request.CheckIn, DateTimeKind.Utc),
            CheckOut        = DateTime.SpecifyKind(request.CheckOut, DateTimeKind.Utc),
            CardLastFour    = lastFour,
            CardNumber      = storedCardNumber,
            CardToken       = cardToken,
            SpecialRequests = request.SpecialRequests,
            TotalPrice      = totalPrice,
            CreatedAt       = DateTime.UtcNow
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBookingById), new { id = booking.Id },
            ToDto(booking, hotel.Name));
    }

    // VULNERABLE PATH: always returns full card number (no tokenization)
    private static (string lastFour, string? cardNumber, string? cardToken) Tokenize(string fullCardNumber)
    {
        var lastFour = fullCardNumber[^4..];
        return (lastFour, fullCardNumber, null);
    }

    [HttpGet]
    public async Task<IActionResult> GetMyBookings([FromQuery] int page = 1)
    {
        const int PageSize = 10;
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var query = _db.Bookings
            .Include(b => b.Hotel)
            .Where(b => b.UserId == userId)
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
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        // VULNERABLE PATH (SQL injection)
        // q is concatenated directly into raw SQL — injectable.
        // No LIMIT clause: every matching row is read into memory.
        var sql = "SELECT b.\"Id\", b.\"UserId\", b.\"Username\", b.\"HotelId\", b.\"CardLastFour\"," +
                  " b.\"CheckIn\", b.\"CheckOut\", b.\"SpecialRequests\", b.\"TotalPrice\", b.\"CreatedAt\"," +
                  " h.\"Name\" AS \"HotelName\"" +
                  " FROM bookingdojo.\"Bookings\" b" +
                  " JOIN bookingdojo.\"Hotels\" h ON b.\"HotelId\" = h.\"Id\"" +
                  $" WHERE b.\"UserId\" = '{userId}' AND h.\"Name\" ILIKE '%{q}%'" +
                  " ORDER BY b.\"CreatedAt\" DESC";

        List<BookingDto> results = new();
        var conn = _db.Database.GetDbConnection();
        var needsClose = conn.State != ConnectionState.Open;
        if (needsClose) await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
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
        var truncated = false;
        if (pageSize.HasValue && pageSize.Value > 0 && results.Count > pageSize.Value)
        {
            results = results.Take(pageSize.Value).ToList();
            truncated = true;
        }

        return Ok(new { results, truncated, appliedPageSize = pageSize });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "ResourceOwner")]
    [OwnedResource(typeof(Booking))]
    public async Task<IActionResult> GetBookingById(int id)
    {
        var booking = await _db.Bookings
            .Include(b => b.Hotel)
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
