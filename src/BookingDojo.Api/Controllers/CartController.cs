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
[Route("api/cart")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly IOptions<WorkshopOptions> _workshop;

    public CartController(BookingDojoDbContext db, IOptions<WorkshopOptions> workshop)
    {
        _db = db;
        _workshop = workshop;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    private string Username => User.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "unknown";

    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var cart = await GetOrCreateCart();
        return Ok(ToDto(cart));
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
        var storedCardNumber = _workshop.Value.CardPiiStorage == "Vulnerable" ? request.CardNumber : (string?)null;

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

        return Ok(new CartItemDto(item.Id, item.HotelId, hotel.Name, item.CheckIn, item.CheckOut, item.CardLastFour, item.CardNumber, item.SpecialRequests));
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
        var cart = await _db.Carts
            .Include(c => c.Items).ThenInclude(i => i.Hotel)
            .FirstOrDefaultAsync(c => c.UserId == UserId);

        if (cart == null || cart.Items.Count == 0)
            return BadRequest(new { message = "Cart is empty" });

        int? discountPercent = null;
        string? couponMessage = null;

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var couponResult = await ApplyCoupon(request.CouponCode);
            if (couponResult is BadRequestObjectResult or NotFoundObjectResult or ConflictObjectResult)
                return couponResult;

            var coupon = await _db.Coupons.AsNoTracking().FirstAsync(c => c.Code == request.CouponCode);
            discountPercent = coupon.DiscountPercent;
            couponMessage = $"Coupon {request.CouponCode} applied — {discountPercent}% off";
        }

        var snapshot = cart.Items.Select(i => (i.HotelId, i.Hotel.Name, i.CheckIn, i.CheckOut, i.CardLastFour, i.CardNumber, i.SpecialRequests)).ToList();

        var bookings = snapshot.Select(i => new Booking
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
        }).ToList();

        _db.Bookings.AddRange(bookings);
        _db.CartItems.RemoveRange(cart.Items);
        await _db.SaveChangesAsync();

        var bookingDtos = bookings.Zip(snapshot, (b, s) =>
            new BookingDto(b.Id, b.UserId, Username, b.HotelId, s.Name,
                b.CheckIn, b.CheckOut, b.CardLastFour, b.CardNumber, b.CardToken,
                b.SpecialRequests, b.CreatedAt)
        ).ToList();

        return Ok(new CheckoutResult(bookingDtos, discountPercent, couponMessage));
    }

    private async Task<IActionResult> ApplyCoupon(string code)
    {
        if (_workshop.Value.CouponRedemptionRaceCondition == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH (TOCTOU race condition)
            // Time of Check: read the coupon and validate remaining uses
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == code);
            if (coupon == null)
                return NotFound(new { message = $"Coupon '{code}' not found" });

            if (coupon.UsesCount >= coupon.MaxUses)
                return Conflict(new { message = "Coupon has already been fully redeemed" });

            // Artificial delay widens the race window:
            // two concurrent checkouts both pass the check above before either writes.
            await Task.Delay(500);

            // Time of Use: a concurrent request may have already incremented this
            coupon.UsesCount++;
            await _db.SaveChangesAsync();
            return Ok();
        }
        else
        {
            // WORKSHOP: FIXED PATH
            // Atomic UPDATE — the WHERE clause enforces the limit inside the database engine.
            // Only one concurrent transaction can increment; all others see 0 rows affected.
            if (_db.Database.IsRelational())
            {
                var rows = await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE bookingdojo.\"Coupons\" " +
                    "SET \"UsesCount\" = \"UsesCount\" + 1 " +
                    "WHERE \"Code\" = {0} AND \"UsesCount\" < \"MaxUses\"",
                    code);

                if (rows == 0)
                {
                    var exists = await _db.Coupons.AnyAsync(c => c.Code == code);
                    return exists
                        ? Conflict(new { message = "Coupon has already been fully redeemed" })
                        : NotFound(new { message = $"Coupon '{code}' not found" });
                }
                return Ok();
            }
            else
            {
                var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == code);
                if (coupon == null) return NotFound(new { message = $"Coupon '{code}' not found" });
                if (coupon.UsesCount >= coupon.MaxUses) return Conflict(new { message = "Coupon has already been fully redeemed" });
                coupon.UsesCount++;
                await _db.SaveChangesAsync();
                return Ok();
            }
        }
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

    private static CartDto ToDto(Cart cart) =>
        new(cart.Id, cart.Items.Select(i =>
            new CartItemDto(i.Id, i.HotelId, i.Hotel?.Name ?? "", i.CheckIn, i.CheckOut, i.CardLastFour, i.CardNumber, i.SpecialRequests)
        ).ToList());
}
