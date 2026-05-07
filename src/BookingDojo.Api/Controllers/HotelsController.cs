using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using BookingDojo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/hotels")]
[Authorize]
public class HotelsController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly AuditLogService _auditLogService;

    public HotelsController(BookingDojoDbContext db, AuditLogService auditLogService)
    {
        _db = db;
        _auditLogService = auditLogService;
    }

    [HttpGet]
    public async Task<IActionResult> GetHotels()
    {
        var role = User.FindFirstValue("role");
        var partnerIdClaim = User.FindFirstValue("partner_id");

        IQueryable<Hotel> query = _db.Hotels.Include(h => h.Partner);

        if (role == "PartnerUser" && Guid.TryParse(partnerIdClaim, out var partnerId))
        {
            query = query.Where(h => h.PartnerId == partnerId);
        }

        var hotels = await query
            .Where(h => h.IsActive)
            .OrderBy(h => h.Name)
            .Select(h => new HotelDto(h.Id, h.PartnerId, h.Partner!.Name, h.Name, h.Location, h.Description, h.PricePerNight, h.IsActive, h.CreatedAt))
            .ToListAsync();

        return Ok(hotels);
    }

    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableHotels()
    {
        var hotels = await _db.Hotels
            .Include(h => h.Partner)
            .Where(h => h.IsActive)
            .OrderBy(h => h.Name)
            .Select(h => new HotelDto(h.Id, h.PartnerId, h.Partner!.Name, h.Name, h.Location, h.Description, h.PricePerNight, h.IsActive, h.CreatedAt))
            .ToListAsync();

        return Ok(hotels);
    }

    [HttpPost]
    [Authorize(Roles = "AdminUser,PartnerUser")]
    public async Task<IActionResult> CreateHotel([FromBody] CreateHotelRequest request)
    {
        var role = User.FindFirstValue("role");
        var username = User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Name) ?? "unknown";
        var partnerIdClaim = User.FindFirstValue("partner_id");

        Guid partnerId;

        if (role == "PartnerUser")
        {
            if (!Guid.TryParse(partnerIdClaim, out partnerId))
                return Forbid();
        }
        else
        {
            if (request.PartnerId == null)
                return BadRequest(new { message = "AdminUser must specify a partnerId" });
            partnerId = request.PartnerId.Value;
        }

        var partner = await _db.Partners.FindAsync(partnerId);
        if (partner == null || !partner.IsActive)
            return BadRequest(new { message = "Partner not found or inactive" });

        if (request.PricePerNight <= 0)
            return BadRequest(new { message = "PricePerNight must be greater than zero" });

        var hotel = new Hotel
        {
            PartnerId = partnerId,
            Name = request.Name,
            Location = request.Location,
            Description = request.Description,
            PricePerNight = request.PricePerNight,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Hotels.Add(hotel);
        await _db.SaveChangesAsync();

        await _auditLogService.LogAsync(
            username,
            "HOTEL_CREATED",
            $"Hotel '{request.Name}' created in {request.Location} for partner {partner.Name}. Description: {request.Description}",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return CreatedAtAction(nameof(GetHotels), new { id = hotel.Id },
            new HotelDto(hotel.Id, hotel.PartnerId, partner.Name, hotel.Name, hotel.Location, hotel.Description, hotel.PricePerNight, hotel.IsActive, hotel.CreatedAt));
    }
}
