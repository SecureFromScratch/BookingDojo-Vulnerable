using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class PasswordResetController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly IOptions<WorkshopOptions> _workshop;

    public PasswordResetController(BookingDojoDbContext db, IOptions<WorkshopOptions> workshop)
    {
        _db = db;
        _workshop = workshop;
    }

    // In a real system this would send an email. For the workshop the token is returned directly
    // so students can observe and reuse it without a mail server.
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user == null)
            return NotFound(new { message = "Username not found" });

        // Invalidate any existing unused tokens for this user
        var existing = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync();
        foreach (var t in existing)
            t.UsedAt = DateTime.UtcNow;

        var token = new PasswordResetToken
        {
            UserId    = user.Id,
            Token     = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _db.PasswordResetTokens.Add(token);
        await _db.SaveChangesAsync();

        // Workshop: return the token directly (production would email it)
        return Ok(new
        {
            message    = "Reset token issued (workshop: token returned in response, not emailed)",
            resetToken = token.Token,
            expiresAt  = token.ExpiresAt
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return BadRequest(new { message = "New password must be at least 8 characters" });

        var now = DateTime.UtcNow;

        if (_workshop.Value.PasswordResetRaceCondition == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH (TOCTOU race condition)
            // Time of Check: read and validate the token
            var token = await _db.PasswordResetTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Token == request.Token
                                       && t.UsedAt == null
                                       && t.ExpiresAt > now);

            if (token == null)
                return BadRequest(new { message = "Invalid or expired reset token" });

            // Race window — artificial delay lets concurrent requests both pass the check above
            // before either writes. Both will mark the token used and set the password.
            await Task.Delay(500);

            // Time of Use: mark used and update password
            token.UsedAt = DateTime.UtcNow;
            token.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password reset successfully" });
        }
        else
        {
            // WORKSHOP: FIXED PATH
            // Atomic UPDATE: only the first concurrent request satisfies UsedAt IS NULL.
            if (_db.Database.IsRelational())
            {
                var markedAt = DateTime.UtcNow;
                var rows = await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE bookingdojo.\"PasswordResetTokens\" " +
                    "SET \"UsedAt\" = {0} " +
                    "WHERE \"Token\" = {1} AND \"UsedAt\" IS NULL AND \"ExpiresAt\" > {2}",
                    markedAt, request.Token, now);

                if (rows == 0)
                    return Conflict(new { message = "Invalid, expired, or already-used reset token" });

                var tokenRecord = await _db.PasswordResetTokens
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.Token == request.Token);

                tokenRecord!.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                await _db.SaveChangesAsync();
                return Ok(new { message = "Password reset successfully" });
            }
            else
            {
                // InMemory fallback for integration tests
                var token = await _db.PasswordResetTokens
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.Token == request.Token
                                           && t.UsedAt == null
                                           && t.ExpiresAt > now);
                if (token == null)
                    return Conflict(new { message = "Invalid, expired, or already-used reset token" });

                token.UsedAt = DateTime.UtcNow;
                token.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                await _db.SaveChangesAsync();
                return Ok(new { message = "Password reset successfully" });
            }
        }
    }
}
