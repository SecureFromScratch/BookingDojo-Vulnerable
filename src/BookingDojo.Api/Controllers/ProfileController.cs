using System.IdentityModel.Tokens.Jwt;
using System.Net;
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
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<WorkshopOptions> _workshop;

    public ProfileController(BookingDojoDbContext db, IHttpClientFactory httpClientFactory, IOptions<WorkshopOptions> workshop)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _workshop = workshop;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();
        return Ok(new ProfileDto(user.Username, user.Role, user.DisplayName, user.Bio, user.AvatarUrl));
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        user.DisplayName = request.DisplayName;
        user.Bio = request.Bio;
        await _db.SaveChangesAsync();

        return Ok(new ProfileDto(user.Username, user.Role, user.DisplayName, user.Bio, user.AvatarUrl));
    }

    [HttpPost("avatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        if (file.Length > 512 * 1024)
            return BadRequest(new { message = "File too large (max 512 KB)" });

        if (!file.ContentType.StartsWith("image/"))
            return BadRequest(new { message = "Only image files are accepted" });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var base64 = Convert.ToBase64String(ms.ToArray());
        var dataUrl = $"data:{file.ContentType};base64,{base64}";

        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        user.AvatarUrl = dataUrl;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Avatar updated", avatarUrl = dataUrl });
    }

    [HttpPost("avatar-url")]
    public async Task<IActionResult> SetAvatarFromUrl([FromBody] SetAvatarUrlRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            return BadRequest(new { message = "Invalid URL format" });

        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        if (_workshop.Value.ProfileAvatarSsrf == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH
            // The server fetches any URL the caller provides — no host or IP validation.
            // This allows the caller to read internal-only endpoints that the browser
            // cannot reach directly (internal APIs, cloud metadata, etc.).
            var client = _httpClientFactory.CreateClient("profile");
            try
            {
                var response = await client.GetAsync(request.Url);
                var body = await response.Content.ReadAsStringAsync();
                if (body.Length > 4000) body = body[..4000] + "\n…[truncated]";

                user.AvatarUrl = request.Url;
                await _db.SaveChangesAsync();

                return Ok(new AvatarFetchResponse(request.Url, (int)response.StatusCode, body, null));
            }
            catch (HttpRequestException ex)
            {
                return Ok(new AvatarFetchResponse(request.Url, null, null, ex.Message));
            }
            catch (TaskCanceledException)
            {
                return Ok(new AvatarFetchResponse(request.Url, null, null, "Request timed out"));
            }
        }
        else
        {
            // WORKSHOP: FIXED PATH
            if (!IsAllowedUrl(request.Url, out var reason))
                return BadRequest(new { message = $"URL not allowed: {reason}" });

            user.AvatarUrl = request.Url;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Avatar URL updated", avatarUrl = request.Url });
        }
    }

    private static bool IsAllowedUrl(string url, out string reason)
    {
        reason = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "not a valid absolute URL";
            return false;
        }

        if (uri.Scheme != "https")
        {
            reason = "only HTTPS is permitted";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();

        if (host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0")
        {
            reason = "loopback addresses are not permitted";
            return false;
        }

        if (host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".localhost"))
        {
            reason = "internal hostnames are not permitted";
            return false;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            var b = ip.GetAddressBytes();
            if (b.Length == 4)
            {
                if (b[0] == 10
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 169 && b[1] == 254)
                    || b[0] == 127)
                {
                    reason = "private and link-local IP ranges are not permitted";
                    return false;
                }
            }
        }

        return true;
    }
}
