namespace BookingDojo.Api.Models;

public class CartItem
{
    public int Id { get; set; }
    public int CartId { get; set; }
    public Cart Cart { get; set; } = null!;
    public Guid HotelId { get; set; }
    public Hotel Hotel { get; set; } = null!;
    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }
    public string CardLastFour { get; set; } = string.Empty;
    public string? CardNumber { get; set; }
    public string SpecialRequests { get; set; } = string.Empty;
}
