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

    [HttpPost("redeem")]
    public async Task<IActionResult> RedeemCoupon([FromBody] RedeemCouponRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { message = "Code is required" });

        if (_workshop.Value.CouponRedemptionRaceCondition == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH (TOCTOU race condition)
            // Time of Check: read state and validate
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == request.Code);
            if (coupon == null)
                return NotFound(new { message = "Coupon not found" });

            if (coupon.UsesCount >= coupon.MaxUses)
                return Conflict(new { message = "Coupon already exhausted" });

            // Artificial delay widens the race window.
            // Two concurrent requests both pass the check above before either writes.
            await Task.Delay(500);

            // Time of Use: by now another concurrent request may have already incremented
            coupon.UsesCount++;
            await _db.SaveChangesAsync();
            return Ok(new { discountPercent = coupon.DiscountPercent, message = "Coupon applied" });
        }
        else
        {
            // WORKSHOP: FIXED PATH
            // Atomic UPDATE — the WHERE clause enforces the limit inside the database.
            // Only one concurrent transaction can win; all others see 0 rows affected.
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
                        ? Conflict(new { message = "Coupon already exhausted" })
                        : NotFound(new { message = "Coupon not found" });
                }

                var updated = await _db.Coupons.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Code == request.Code);
                return Ok(new { discountPercent = updated!.DiscountPercent, message = "Coupon applied" });
            }
            else
            {
                // InMemory provider (integration tests): simulate the atomic check
                var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == request.Code);
                if (coupon == null)
                    return NotFound(new { message = "Coupon not found" });
                if (coupon.UsesCount >= coupon.MaxUses)
                    return Conflict(new { message = "Coupon already exhausted" });
                coupon.UsesCount++;
                await _db.SaveChangesAsync();
                return Ok(new { discountPercent = coupon.DiscountPercent, message = "Coupon applied" });
            }
        }
    }
}
