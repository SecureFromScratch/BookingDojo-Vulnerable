using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/coupons")]
[Authorize]
public class CouponsController : ControllerBase
{
    private readonly BookingDojoDbContext _db;

    public CouponsController(BookingDojoDbContext db)
    {
        _db = db;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    [HttpDelete("redeem")]
    public async Task<IActionResult> CancelRedeem([FromQuery] string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { message = "Coupon code is required" });

        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == code);
        if (coupon == null)
            return NotFound(new { message = $"Coupon '{code}' not found" });

        if (coupon.UsesCount > 0)
            coupon.UsesCount--;

        var cart = await _db.Carts.FirstOrDefaultAsync(c => c.UserId == UserId);
        if (cart != null)
        {
            cart.AppliedCouponCode = null;
            cart.AppliedCouponCount = 0;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("redeem")]
    public async Task<IActionResult> Redeem([FromBody] RedeemCouponRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { message = "Coupon code is required" });

        // VULNERABLE PATH (TOCTOU race condition)
        // Time of Check: read the coupon and validate remaining uses.
        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == request.Code);
        if (coupon == null)
            return NotFound(new { message = $"Coupon '{request.Code}' not found" });

        if (coupon.UsesCount >= coupon.MaxUses)
            return Conflict(new { message = "Coupon has already been fully redeemed" });

        // Artificial delay widens the race window:
        // two concurrent requests both pass the check above before either writes.
        await Task.Delay(500);

        // Time of Use: a concurrent request may have already incremented this.
        coupon.UsesCount++;
        await SetCartCoupon(request.Code, coupon.DiscountPercent);
        await _db.SaveChangesAsync();

        return Ok(new { discountPercent = coupon.DiscountPercent, message = "Coupon applied" });
    }

    private async Task SetCartCoupon(string code, int discountPercent)
    {
        // Atomic UPSERT — increments AppliedCouponCount so each concurrent redemption
        // is counted separately. Compound discount is computed at read time: price × (1-rate%)^count.
        _ = discountPercent; // rate is stored on the Coupon row, not duplicated here
        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO bookingdojo.\"Carts\" (\"UserId\", \"AppliedCouponCode\", \"AppliedCouponCount\") " +
            "VALUES ({0}, {1}, 1) " +
            "ON CONFLICT (\"UserId\") DO UPDATE " +
            "SET \"AppliedCouponCode\" = EXCLUDED.\"AppliedCouponCode\", " +
            "    \"AppliedCouponCount\" = bookingdojo.\"Carts\".\"AppliedCouponCount\" + 1",
            UserId, code);
    }
}
