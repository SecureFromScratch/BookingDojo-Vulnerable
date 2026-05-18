using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace BookingDojo.Bff.Controllers;

[ApiController]
[Route("bff/{**path}")]
public class BffProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public BffProxyController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private static readonly HashSet<string> _publicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth/forgot-password",
        "auth/reset-password",
    };

    private static readonly string[] _blockedPrefixes = ["internal/"];

    [HttpGet]
    [HttpPost]
    [HttpPut]
    [HttpPatch]
    [HttpDelete]
    public async Task<IActionResult> Proxy(string path)
    {
        if (_blockedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return NotFound();

        var isPublic = _publicPaths.Contains(path);

        if (!isPublic && User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        string? accessToken = null;
        if (!isPublic)
        {
            accessToken = await GetValidAccessTokenAsync();
            if (accessToken == null)
                return Unauthorized();
        }

        var client = _httpClientFactory.CreateClient("api");
        var targetUrl = $"/api/{path}{(Request.QueryString.HasValue ? Request.QueryString.Value : "")}";

        var apiRequest = new HttpRequestMessage(new HttpMethod(Request.Method), targetUrl);

        if (!string.IsNullOrEmpty(accessToken))
            apiRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        if (Request.Method is "POST" or "PUT" or "PATCH")
        {
            var requestContent = new StreamContent(Request.Body);
            requestContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            apiRequest.Content = requestContent;
        }

        var response = await client.SendAsync(apiRequest);

        Response.StatusCode = (int)response.StatusCode;
        Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";

        var responseBody = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrEmpty(responseBody))
            await Response.WriteAsync(responseBody);

        return new EmptyResult();
    }

    private async Task<string?> GetValidAccessTokenAsync()
    {
        var accessToken = User.FindFirstValue("access_token");
        if (string.IsNullOrEmpty(accessToken)) return null;

        if (!IsExpiredOrExpiringSoon(accessToken)) return accessToken;

        // JWT is expired or about to expire — use the refresh token
        var refreshToken = User.FindFirstValue("refresh_token");
        if (string.IsNullOrEmpty(refreshToken)) return null;

        var client = _httpClientFactory.CreateClient("api");
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        if (!response.IsSuccessStatusCode) return null;

        var data = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var newAccessToken  = data.GetProperty("token").GetString()!;
        var newRefreshToken = data.GetProperty("refreshToken").GetString()!;

        // Re-issue the cookie with updated tokens (all other claims stay the same)
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, User.Identity!.Name!),
            new("access_token",  newAccessToken),
            new("refresh_token", newRefreshToken),
            new("role",          User.FindFirstValue("role") ?? ""),
        };
        var partnerId = User.FindFirstValue("partner_id");
        if (!string.IsNullOrEmpty(partnerId))
            claims.Add(new("partner_id", partnerId));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "bff"));
        await HttpContext.SignInAsync("bff", principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc   = DateTimeOffset.UtcNow.AddDays(7),
            AllowRefresh = true
        });

        return newAccessToken;
    }

    private static bool IsExpiredOrExpiringSoon(string jwt)
    {
        try
        {
            var payload = jwt.Split('.')[1];
            var padded = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };
            var json = JsonSerializer.Deserialize<JsonElement>(
                Encoding.UTF8.GetString(Convert.FromBase64String(padded)));

            if (!json.TryGetProperty("exp", out var expProp)) return true;
            return DateTimeOffset.FromUnixTimeSeconds(expProp.GetInt64())
                   <= DateTimeOffset.UtcNow.AddSeconds(30);
        }
        catch { return true; }
    }
}
