using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly IOptions<WorkshopOptions> _workshop;

    public BookingsController(BookingDojoDbContext db, IOptions<WorkshopOptions> workshop)
    {
        _db = db;
        _workshop = workshop;
    }

    [HttpPost]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest request)
    {
        var userId   = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var username = User.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "unknown";

        var hotel = await _db.Hotels.FindAsync(request.HotelId);
        if (hotel == null || !hotel.IsActive)
            return BadRequest(new { message = "Hotel not found or inactive" });

        if (request.CardLastFour.Length != 4 || !request.CardLastFour.All(char.IsDigit))
            return BadRequest(new { message = "cardLastFour must be exactly 4 digits" });

        var booking = new Booking
        {
            UserId          = userId,
            Username        = username,
            HotelId         = request.HotelId,
            CheckIn         = DateTime.SpecifyKind(request.CheckIn, DateTimeKind.Utc),
            CheckOut        = DateTime.SpecifyKind(request.CheckOut, DateTimeKind.Utc),
            CardLastFour    = request.CardLastFour,
            SpecialRequests = request.SpecialRequests,
            CreatedAt       = DateTime.UtcNow
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBookingById), new { id = booking.Id },
            ToDto(booking, hotel.Name));
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

        // ── SQL injection control ─────────────────────────────────────────────
        List<BookingDto> results;

        if (_workshop.Value.BookingSearchSqlInjection == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH (SQL injection)
            // q is concatenated directly into raw SQL — injectable.
            // No LIMIT clause: every matching row is read into memory.
            var sql = "SELECT b.\"Id\", b.\"UserId\", b.\"Username\", b.\"HotelId\", b.\"CardLastFour\"," +
                      " b.\"CheckIn\", b.\"CheckOut\", b.\"SpecialRequests\", b.\"CreatedAt\"," +
                      " h.\"Name\" AS \"HotelName\"" +
                      " FROM bookingdojo.\"Bookings\" b" +
                      " JOIN bookingdojo.\"Hotels\" h ON b.\"HotelId\" = h.\"Id\"" +
                      $" WHERE b.\"UserId\" = '{userId}' AND h.\"Name\" ILIKE '%{q}%'" +
                      " ORDER BY b.\"CreatedAt\" DESC";

            results = new List<BookingDto>();
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
                        reader.IsDBNull(reader.GetOrdinal("SpecialRequests"))
                            ? "" : reader.GetString(reader.GetOrdinal("SpecialRequests")),
                        reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                    ));
                }
            }
            finally
            {
                if (needsClose) await conn.CloseAsync();
            }
        }
        else
        {
            // WORKSHOP: FIXED PATH (SQL injection fixed)
            // userId is a typed SQL parameter — q never reaches SQL.
            var allUserBookings = await _db.Bookings
                .Include(b => b.Hotel)
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            var filtered = string.IsNullOrEmpty(q)
                ? allUserBookings
                : allUserBookings.Where(b => b.Hotel.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

            results = filtered.Select(b => ToDto(b, b.Hotel.Name)).ToList();
        }

        // ── Resource consumption control (independent of SQL injection fix) ──
        var truncated = false;
        if (_workshop.Value.BookingSearchResourceConsumption == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH (resource consumption)
            // The client-supplied pageSize is honoured unconditionally — the server places
            // no upper bound. A caller can omit it (returns every row) or set it to
            // Integer.MaxValue to retrieve the entire table in a single request.
            if (pageSize.HasValue && pageSize.Value > 0 && results.Count > pageSize.Value)
            {
                results = results.Take(pageSize.Value).ToList();
                truncated = true;
            }
        }
        else
        {
            // WORKSHOP: FIXED PATH (resource consumption)
            // Server-side hard cap — the client-supplied pageSize is ignored completely.
            // No matter what the caller requests, at most MaxResults rows are returned.
            const int MaxResults = 10;
            if (results.Count > MaxResults)
            {
                results = results.Take(MaxResults).ToList();
                truncated = true;
            }
        }

        return Ok(new { results, truncated, appliedPageSize = pageSize });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBookingById(int id)
    {
        var booking = await _db.Bookings
            .Include(b => b.Hotel)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking == null)
            return NotFound();

        if (_workshop.Value.BookingIdorAccess == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH
            // No ownership check — any authenticated user can fetch any booking by ID.
            // Sequential integer IDs make enumeration trivial.
            return Ok(ToDto(booking, booking.Hotel.Name));
        }

        // WORKSHOP: FIXED PATH
        var callerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        if (booking.UserId != callerId)
            return Forbid();

        return Ok(ToDto(booking, booking.Hotel.Name));
    }

    private static BookingDto ToDto(Booking b, string hotelName) => new(
        b.Id, b.UserId, b.Username, b.HotelId, hotelName,
        b.CheckIn, b.CheckOut, b.CardLastFour, b.SpecialRequests, b.CreatedAt);
}
