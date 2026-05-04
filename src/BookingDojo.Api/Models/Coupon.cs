namespace BookingDojo.Api.Models;

public class Coupon
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int DiscountPercent { get; set; }
    public int MaxUses { get; set; }
    public int UsesCount { get; set; }
}
