using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BookingDojo.Api.Services;

public class AuthService
{
    private readonly BookingDojoDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(BookingDojoDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<(bool Success, string? Jwt, string? RefreshToken, User? User)> LoginAsync(string username, string password)
    {
        User? user;

        // VULNERABLE PATH
        // The username is concatenated directly into raw SQL.
        // The endpoint returns only 200 or 401 — no query data is echoed back.
        //
        // Username enumeration (sleeps 3s only if 'admin' exists):
        //   x' OR 1=(SELECT 1 FROM pg_sleep(CASE WHEN
        //     (SELECT COUNT(*) FROM bookingdojo."Users" WHERE "Username"='admin')>0
        //     THEN 3 ELSE 0 END))--
        //
        // Conditional hash extraction (sleeps 3s when condition is true):
        //   admin' AND 1=(SELECT 1 FROM pg_sleep(
        //     CASE WHEN SUBSTRING("PasswordHash",1,1)='$' THEN 3 ELSE 0 END))--
        var sql = $"""
            SELECT "Id", "Username", "PasswordHash", "Role", "PartnerId"
            FROM bookingdojo."Users"
            WHERE "Username" = '{username}'
            """;

        user = null;
        var conn = _db.Database.GetDbConnection();
        var needsClose = conn.State != ConnectionState.Open;
        if (needsClose) await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                user = new User
                {
                    Id          = reader.GetGuid(reader.GetOrdinal("Id")),
                    Username    = reader.GetString(reader.GetOrdinal("Username")),
                    PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
                    Role        = reader.GetString(reader.GetOrdinal("Role")),
                    PartnerId   = reader.IsDBNull(reader.GetOrdinal("PartnerId"))
                                    ? null
                                    : reader.GetGuid(reader.GetOrdinal("PartnerId")),
                };
            }
        }
        finally
        {
            if (needsClose) await conn.CloseAsync();
        }

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return (false, null, null, null);

        var jwt = GenerateJwt(user);
        var refreshToken = await CreateRefreshTokenAsync(user.Id);
        return (true, jwt, refreshToken, user);
    }

    public async Task<(bool Success, string? Jwt, string? RefreshToken, User? User)> RefreshAsync(string refreshToken)
    {
        var stored = await _db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (stored == null)
            return (false, null, null, null);

        if (!stored.IsValid)
        {
            // Reuse of a revoked token — possible theft. Revoke all sessions for this user.
            await _db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
            return (false, null, null, null);
        }

        // Revoke old token and issue new ones (rotation)
        stored.RevokedAt = DateTime.UtcNow;

        var newRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = stored.UserId,
            Token     = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var jwt = GenerateJwt(stored.User);
        return (true, jwt, newRefreshToken, stored.User);
    }

    private async Task<string> CreateRefreshTokenAsync(Guid userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = userId,
            Token     = token,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return token;
    }

    public string GenerateJwtMfaStamped(User user) =>
        GenerateJwt(user, DateTime.UtcNow);

    private string GenerateJwt(User user, DateTime? mfaVerifiedAt = null)
    {
        var secret = _config["BookingDojo:Jwt:Secret"]!;
        var issuer = _config["BookingDojo:Jwt:Issuer"]!;
        var audience = _config["BookingDojo:Jwt:Audience"]!;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.Username),
            new("role", user.Role),
        };

        if (user.PartnerId.HasValue)
            claims.Add(new("partner_id", user.PartnerId.Value.ToString()));

        if (mfaVerifiedAt.HasValue)
            claims.Add(new("mfa_verified_at",
                new DateTimeOffset(mfaVerifiedAt.Value, TimeSpan.Zero).ToUnixTimeSeconds().ToString()));

        var expirationMinutes = _config.GetValue<int>("BookingDojo:Jwt:ExpirationMinutes", 15);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
