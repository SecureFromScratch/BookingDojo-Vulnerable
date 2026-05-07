namespace BookingDojo.Api.Models;

public class Hotel
{
    public Guid Id { get; set; }
    public Guid PartnerId { get; set; }
    public Partner Partner { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PricePerNight { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
