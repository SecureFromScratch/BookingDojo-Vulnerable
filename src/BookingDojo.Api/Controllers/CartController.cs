using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly BookingDojoDbContext _db;

    public CartController(BookingDojoDbContext db)
    {
        _db = db;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    private string Username => User.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "unknown";

    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var cart = await GetOrCreateCart();
        int? couponRate = null;
        if (cart.AppliedCouponCode != null && cart.AppliedCouponCount > 0)
        {
            var coupon = await _db.Coupons.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Code == cart.AppliedCouponCode);
            couponRate = coupon?.DiscountPercent;
        }
        return Ok(ToDto(cart, couponRate));
    }

    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] AddToCartRequest request)
    {
        var hotel = await _db.Hotels.FindAsync(request.HotelId);
        if (hotel == null || !hotel.IsActive)
            return BadRequest(new { message = "Hotel not found or inactive" });

        if (request.CardNumber.Length < 13 || request.CardNumber.Length > 19 || !request.CardNumber.All(char.IsDigit))
            return BadRequest(new { message = "cardNumber must be 13–19 digits" });

        if (request.CheckOut <= request.CheckIn)
            return BadRequest(new { message = "Check-out must be after check-in" });

        var cart = await GetOrCreateCart();

        var lastFour = request.CardNumber[^4..];
        var storedCardNumber = request.CardNumber;

        var item = new CartItem
        {
            CartId = cart.Id,
            HotelId = request.HotelId,
            CheckIn = DateTime.SpecifyKind(request.CheckIn, DateTimeKind.Utc),
            CheckOut = DateTime.SpecifyKind(request.CheckOut, DateTimeKind.Utc),
            CardLastFour = lastFour,
            CardNumber = storedCardNumber,
            SpecialRequests = request.SpecialRequests,
        };

        _db.CartItems.Add(item);
        await _db.SaveChangesAsync();

        var nights = (int)(item.CheckOut - item.CheckIn).TotalDays;
        var totalPrice = hotel.PricePerNight * nights;
        return Ok(new CartItemDto(item.Id, item.HotelId, hotel.Name, item.CheckIn, item.CheckOut, item.CardLastFour, item.CardNumber, item.SpecialRequests, totalPrice));
    }

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> RemoveItem(int id)
    {
        var cart = await _db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == UserId);

        var item = cart?.Items.FirstOrDefault(i => i.Id == id);
        if (item == null) return NotFound();

        _db.CartItems.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CartCheckoutRequest request)
    {
        var mfaVerifiedAt = User.FindFirstValue("mfa_verified_at");
        var mfaValid = mfaVerifiedAt != null &&
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(mfaVerifiedAt)) >= DateTimeOffset.UtcNow.AddMinutes(-5);
        if (!mfaValid)
            return StatusCode(403, new { requiresMfa = true, message = "Payment requires MFA verification. Please verify your identity and try again." });

        var cart = await _db.Carts
            .Include(c => c.Items).ThenInclude(i => i.Hotel)
            .FirstOrDefaultAsync(c => c.UserId == UserId);

        if (cart == null || cart.Items.Count == 0)
            return BadRequest(new { message = "Cart is empty" });

        // Compound discount: each application multiplies the remaining price by (1 - rate/100).
        // In Vulnerable mode a race allows count > 1 on a single-use coupon.
        decimal? discountMultiplier = null;
        string? couponMessage = null;
        if (!string.IsNullOrWhiteSpace(cart.AppliedCouponCode) && cart.AppliedCouponCount > 0)
        {
            var coupon = await _db.Coupons.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Code == cart.AppliedCouponCode);
            if (coupon != null)
            {
                discountMultiplier = (decimal)Math.Pow(1 - coupon.DiscountPercent / 100.0, cart.AppliedCouponCount);
                couponMessage = cart.AppliedCouponCount > 1
                    ? $"Coupon {cart.AppliedCouponCode} applied {cart.AppliedCouponCount}× — {coupon.DiscountPercent}% each"
                    : $"Coupon {cart.AppliedCouponCode} applied — {coupon.DiscountPercent}% off";
            }
        }

        var snapshot = cart.Items.Select(i =>
        {
            var nights = (int)(i.CheckOut - i.CheckIn).TotalDays;
            var basePrice = i.Hotel.PricePerNight * nights;
            return (i.HotelId, i.Hotel.Name, i.CheckIn, i.CheckOut, i.CardLastFour, i.CardNumber, i.SpecialRequests, BasePrice: basePrice);
        }).ToList();

        var bookings = snapshot.Select(i =>
        {
            var totalPrice = discountMultiplier.HasValue
                ? i.BasePrice * discountMultiplier.Value
                : i.BasePrice;
            return new Booking
            {
                UserId = UserId,
                Username = Username,
                HotelId = i.HotelId,
                CheckIn = i.CheckIn,
                CheckOut = i.CheckOut,
                CardLastFour = i.CardLastFour,
                CardNumber = i.CardNumber,
                CreatedAt = DateTime.UtcNow,
                SpecialRequests = i.SpecialRequests,
                TotalPrice = Math.Round(totalPrice, 2),
            };
        }).ToList();

        _db.Bookings.AddRange(bookings);
        _db.CartItems.RemoveRange(cart.Items);
        cart.AppliedCouponCode = null;
        cart.AppliedCouponCount = 0;
        await _db.SaveChangesAsync();

        var bookingDtos = bookings.Zip(snapshot, (b, s) =>
            new BookingDto(b.Id, b.UserId, Username, b.HotelId, s.Name,
                b.CheckIn, b.CheckOut, b.CardLastFour, b.CardNumber, b.CardToken,
                b.SpecialRequests, b.TotalPrice, b.CreatedAt)
        ).ToList();

        var effectivePercent = discountMultiplier.HasValue
            ? (int)Math.Round((1 - (double)discountMultiplier.Value) * 100)
            : (int?)null;
        return Ok(new CheckoutResult(bookingDtos, effectivePercent, couponMessage));
    }


    private async Task<Cart> GetOrCreateCart()
    {
        var cart = await _db.Carts
            .Include(c => c.Items).ThenInclude(i => i.Hotel)
            .FirstOrDefaultAsync(c => c.UserId == UserId);

        if (cart != null) return cart;

        cart = new Cart { UserId = UserId };
        _db.Carts.Add(cart);
        await _db.SaveChangesAsync();
        return cart;
    }

    private static CartDto ToDto(Cart cart, int? couponRate = null) =>
        new(cart.Id, cart.Items.Select(i =>
        {
            var nights = (int)(i.CheckOut - i.CheckIn).TotalDays;
            var totalPrice = (i.Hotel?.PricePerNight ?? 0m) * nights;
            return new CartItemDto(i.Id, i.HotelId, i.Hotel?.Name ?? "", i.CheckIn, i.CheckOut, i.CardLastFour, i.CardNumber, i.SpecialRequests, totalPrice);
        }).ToList(), cart.AppliedCouponCode, couponRate, cart.AppliedCouponCount);
}
