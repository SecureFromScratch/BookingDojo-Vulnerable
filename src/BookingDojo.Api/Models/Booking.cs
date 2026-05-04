namespace BookingDojo.Api.Models;

public class Booking
{
    // Sequential integer — intentionally guessable for the IDOR lab.
    public int Id { get; set; }

    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;

    public Guid HotelId { get; set; }
    public Hotel Hotel { get; set; } = null!;

    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }

    // The sensitive field — makes the IDOR meaningful.
    public string CardLastFour { get; set; } = string.Empty;

    public string SpecialRequests { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
