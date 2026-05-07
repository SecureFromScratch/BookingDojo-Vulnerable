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
[Route("api/coupons")]
[Authorize]
public class CouponsController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly IOptions<WorkshopOptions> _workshop;

    public CouponsController(BookingDojoDbContext db, IOptions<WorkshopOptions> workshop)
    {
        _db = db;
        _workshop = workshop;
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

        if (_workshop.Value.CouponRedemptionRaceCondition == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH (TOCTOU race condition)
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
                    request.Code);

                if (rows == 0)
                {
                    var exists = await _db.Coupons.AnyAsync(c => c.Code == request.Code);
                    return exists
                        ? Conflict(new { message = "Coupon has already been fully redeemed" })
                        : NotFound(new { message = $"Coupon '{request.Code}' not found" });
                }
            }
            else
            {
                var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == request.Code);
                if (coupon == null) return NotFound(new { message = $"Coupon '{request.Code}' not found" });
                if (coupon.UsesCount >= coupon.MaxUses) return Conflict(new { message = "Coupon has already been fully redeemed" });
                coupon.UsesCount++;
                await _db.SaveChangesAsync();
            }

            var redeemed = await _db.Coupons.AsNoTracking().FirstAsync(c => c.Code == request.Code);
            await SetCartCoupon(request.Code, redeemed.DiscountPercent);
            await _db.SaveChangesAsync();

            return Ok(new { discountPercent = redeemed.DiscountPercent, message = "Coupon applied" });
        }
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
