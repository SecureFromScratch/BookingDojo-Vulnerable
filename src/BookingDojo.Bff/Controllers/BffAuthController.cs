using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace BookingDojo.Bff.Controllers;

[ApiController]
[Route("bff/auth")]
public class BffAuthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public BffAuthController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] JsonElement body)
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.PostAsync("/api/auth/login",
            new StringContent(body.GetRawText(), Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonSerializer.Deserialize<object>(errorBody));
        }

        var loginData = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name,  loginData.GetProperty("username").GetString()!),
            new("access_token",   loginData.GetProperty("token").GetString()!),
            new("refresh_token",  loginData.GetProperty("refreshToken").GetString()!),
            new("role",           loginData.GetProperty("role").GetString()!),
        };

        if (loginData.TryGetProperty("partnerId", out var pid) && pid.ValueKind != JsonValueKind.Null)
            claims.Add(new("partner_id", pid.GetString()!));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "bff"));
        await HttpContext.SignInAsync("bff", principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc   = DateTimeOffset.UtcNow.AddDays(7),
            AllowRefresh = true
        });

        return Ok(new
        {
            username  = loginData.GetProperty("username").GetString(),
            role      = loginData.GetProperty("role").GetString(),
            partnerId = loginData.TryGetProperty("partnerId", out var p) && p.ValueKind != JsonValueKind.Null
                            ? p.GetString() : null
        });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        return Ok(new
        {
            username  = User.Identity.Name,
            role      = User.FindFirstValue("role"),
            partnerId = User.FindFirstValue("partner_id")
        });
    }

    [HttpDelete("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("bff");
        return NoContent();
    }
}
