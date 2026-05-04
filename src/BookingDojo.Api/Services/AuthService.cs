using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using BookingDojo.Api.Workshop;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BookingDojo.Api.Services;

public class AuthService
{
    private readonly BookingDojoDbContext _db;
    private readonly IConfiguration _config;
    private readonly IOptions<WorkshopOptions> _workshop;

    public AuthService(BookingDojoDbContext db, IConfiguration config, IOptions<WorkshopOptions> workshop)
    {
        _db = db;
        _config = config;
        _workshop = workshop;
    }

    public async Task<(bool Success, string? Token, User? User)> LoginAsync(string username, string password)
    {
        User? user;

        if (_workshop.Value.LoginSqlInjection == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH
            // The username is concatenated directly into raw SQL.
            // The endpoint returns only 200 or 401 — no query data is echoed back.
            //
            // Timing probe (confirms injection, always sleeps 3s if 'admin' exists):
            //   username: admin' AND 1=(SELECT 1 FROM pg_sleep(3))--
            //
            // Conditional extraction (sleeps 3s when condition is true):
            //   username: admin' AND 1=(SELECT 1 FROM pg_sleep(
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
        }
        else
        {
            // WORKSHOP: FIXED PATH
            // username is bound as a SQL parameter — injection not possible.
            user = await _db.Users
                .FirstOrDefaultAsync(u => u.Username == username);
        }

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return (false, null, null);

        var token = GenerateJwt(user);
        return (true, token, user);
    }

    private string GenerateJwt(User user)
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

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
