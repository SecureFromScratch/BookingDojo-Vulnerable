using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using BookingDojo.Api.Services;
using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/auth/mfa")]
[Authorize]
public class MfaController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly IOptions<WorkshopOptions> _workshop;
    private readonly AuthService _authService;

    private const int MaxAttempts = 5;
    private const int OtpTtlMinutes = 10;

    public MfaController(BookingDojoDbContext db, IOptions<WorkshopOptions> workshop, AuthService authService)
    {
        _db = db;
        _workshop = workshop;
        _authService = authService;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    private string Username => User.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "unknown";

    // POST /api/auth/mfa/challenge
    // Generates (or replaces) the user's active OTP.
    [HttpPost("challenge")]
    public async Task<IActionResult> Challenge()
    {
        // Invalidate any existing active challenge for this user.
        var existing = await _db.MfaChallenges
            .Where(m => m.UserId == UserId && m.VerifiedAt == null && m.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        _db.MfaChallenges.RemoveRange(existing);

        var code = Random.Shared.Next(0, 10_000).ToString("D4");
        var challenge = new MfaChallenge
        {
            UserId = UserId,
            Code = code,
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpTtlMinutes),
        };

        _db.MfaChallenges.Add(challenge);
        await _db.SaveChangesAsync();

        return Ok(new { expiresAt = challenge.ExpiresAt, ttlMinutes = OtpTtlMinutes });
    }

    // GET /api/auth/mfa/otp
    // Workshop-only: simulates out-of-band OTP delivery (SMS / email).
    // In a real system this endpoint would not exist — the code goes to the user's phone/inbox.
    [HttpGet("otp")]
    public async Task<IActionResult> GetOtp()
    {
        var challenge = await ActiveChallenge();
        if (challenge == null)
            return NotFound(new { message = "No active challenge. Call POST /api/auth/mfa/challenge first." });

        var attemptsLeft = _workshop.Value.MfaBruteForceProtection == "Fixed"
            ? MaxAttempts - challenge.AttemptCount
            : (int?)null;

        return Ok(new
        {
            code = challenge.Code,
            expiresAt = challenge.ExpiresAt,
            attemptsRemaining = attemptsLeft,
            workshopNote = "This endpoint exists only for the workshop — it simulates SMS/email delivery.",
        });
    }

    // POST /api/auth/mfa/verify
    // Validates the OTP. Vulnerable: unlimited retries. Fixed: locks out after MaxAttempts.
    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] MfaVerifyRequest request)
    {
        var challenge = await ActiveChallenge();
        if (challenge == null)
            return NotFound(new { message = "No active challenge. Call POST /api/auth/mfa/challenge first." });

        if (_workshop.Value.MfaBruteForceProtection == "Fixed")
        {
            if (challenge.AttemptCount >= MaxAttempts)
            {
                _db.MfaChallenges.Remove(challenge);
                await _db.SaveChangesAsync();
                return StatusCode(429, new { message = "Too many failed attempts. Challenge invalidated — request a new one." });
            }
        }

        if (challenge.Code != request.Code)
        {
            challenge.AttemptCount++;
            await _db.SaveChangesAsync();

            if (_workshop.Value.MfaBruteForceProtection == "Fixed" && challenge.AttemptCount >= MaxAttempts)
            {
                _db.MfaChallenges.Remove(challenge);
                await _db.SaveChangesAsync();
                return StatusCode(429, new { message = "Too many failed attempts. Challenge invalidated — request a new one." });
            }

            var attemptsLeft = _workshop.Value.MfaBruteForceProtection == "Fixed"
                ? (int?)(MaxAttempts - challenge.AttemptCount)
                : null;
            return Unauthorized(new { message = "Incorrect code.", attemptsRemaining = attemptsLeft });
        }

        challenge.VerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var user = await _db.Users.FindAsync(UserId);
        var newToken = _authService.GenerateJwtMfaStamped(user!);

        return Ok(new
        {
            verified = true,
            username = Username,
            userId = UserId,
            verifiedAt = challenge.VerifiedAt,
            token = newToken,
        });
    }

    private Task<MfaChallenge?> ActiveChallenge() =>
        _db.MfaChallenges.FirstOrDefaultAsync(
            m => m.UserId == UserId && m.VerifiedAt == null && m.ExpiresAt > DateTime.UtcNow);
}
