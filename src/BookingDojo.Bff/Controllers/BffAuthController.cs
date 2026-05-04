using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace BookingDojo.Bff.Controllers;

[ApiController]
[Route("bff/auth")]
public class BffAuthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private const string CookieName = "bd_token";

    public BffAuthController(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] JsonElement body)
    {
        var client = _httpClientFactory.CreateClient("api");
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/auth/login", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<object>(errorBody));
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var loginData = JsonSerializer.Deserialize<JsonElement>(responseBody);
        var token = loginData.GetProperty("token").GetString()!;

        Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = false, // set true in production
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(8)
        });

        // Return user info without the token
        var result = new
        {
            username = loginData.GetProperty("username").GetString(),
            role = loginData.GetProperty("role").GetString(),
            partnerId = loginData.TryGetProperty("partnerId", out var pid) ? pid.GetString() : null
        };

        return Ok(result);
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        var token = Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        // Decode JWT payload (no validation needed — the API validates on each proxied request)
        var parts = token.Split('.');
        if (parts.Length != 3)
            return Unauthorized();

        try
        {
            var payload = parts[1];
            // Add padding if needed
            var padded = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var claims = JsonSerializer.Deserialize<JsonElement>(json);

            return Ok(new
            {
                username = claims.TryGetProperty("name", out var name) ? name.GetString() : null,
                role = claims.TryGetProperty("role", out var role) ? role.GetString() : null,
                partnerId = claims.TryGetProperty("partner_id", out var pid) ? pid.GetString() : null
            });
        }
        catch
        {
            return Unauthorized();
        }
    }

    [HttpDelete("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(CookieName);
        return Ok();
    }
}
