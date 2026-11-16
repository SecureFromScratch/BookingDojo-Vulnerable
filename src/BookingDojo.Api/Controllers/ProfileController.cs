using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public ProfileController(BookingDojoDbContext db, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
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

        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return NotFound();

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var base64 = Convert.ToBase64String(ms.ToArray());
        var avatarUrl = $"data:{file.ContentType};base64,{base64}";

        /* SPECIAL SVG HANDLING DISABLED - LOOK AT FULL WALKTHOUGH FOR DETAILS
        // SVG is the textual image format supported by this route.
        var isSvg = file.ContentType.Split(';')[0].Trim()
            .Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase);

        var avatarUrl = isSvg
            ? "/bff/profile/unembedImage?base64=" + Uri.EscapeDataString(base64)
            : $"data:{file.ContentType};base64,{base64}";
        */
        
        user.AvatarUrl = avatarUrl;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Avatar updated", avatarUrl = avatarUrl });
    }

    // example external image url: https://img-s-msn-com.akamaized.net/tenant/amp/entityid/AA2h4TQ5.img?w=1012&h=675&m=6
    [HttpPost("avatar-url")]
    public async Task<IActionResult> SetAvatarFromUrl([FromBody] SetAvatarUrlRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            return BadRequest(new { message = "Invalid URL format" });

        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        // VULNERABLE PATH
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


    [HttpGet("unembedImage")]
    public IActionResult UnembedImage([FromQuery] string base64)
    {
        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return BadRequest("Invalid Base64");
        }

        // Limit payload size.
        if (bytes.Length > 512 * 1024)
            return BadRequest("Image too large");

        // This endpoint only serves SVG content.
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        if (!content.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Not an SVG image");

        Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; script-src 'none'; sandbox";

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "no-store";

        return File(bytes, "image/svg+xml");
    }
}
