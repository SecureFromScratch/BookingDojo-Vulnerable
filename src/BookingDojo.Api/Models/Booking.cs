using BookingDojo.Api.Authorization;

namespace BookingDojo.Api.Models;

public class Booking : IOwnedResource
{
    // Sequential integer — intentionally guessable for the IDOR lab.
    public int Id { get; set; }

    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;

    public Guid HotelId { get; set; }
    public Hotel Hotel { get; set; } = null!;

    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }

    public string CardLastFour { get; set; } = string.Empty;

    // PII lab: full card number stored in vulnerable mode, null in fixed mode.
    public string? CardNumber { get; set; }

    // PII lab: opaque payment token stored in fixed mode, null in vulnerable mode.
    public string? CardToken { get; set; }

    public string SpecialRequests { get; set; } = string.Empty;

    public decimal TotalPrice { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
