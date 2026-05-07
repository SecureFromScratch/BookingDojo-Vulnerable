namespace BookingDojo.Api.Models;

public class Cart
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public List<CartItem> Items { get; set; } = [];
    public string? AppliedCouponCode { get; set; }
    public int AppliedCouponCount { get; set; }
}
