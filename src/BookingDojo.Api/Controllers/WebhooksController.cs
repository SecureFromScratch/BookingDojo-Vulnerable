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
[Route("api/webhooks")]
[Authorize]
public class WebhooksController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<WorkshopOptions> _workshop;
    private readonly BookingDojoDbContext _db;

    public WebhooksController(IHttpClientFactory httpClientFactory, IOptions<WorkshopOptions> workshop, BookingDojoDbContext db)
    {
        _httpClientFactory = httpClientFactory;
        _workshop = workshop;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetWebhooks()
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var webhooks = await _db.Webhooks
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WebhookDto(w.Id, w.Url, w.CreatedAt))
            .ToListAsync();
        return Ok(webhooks);
    }

    [HttpPost]
    public async Task<IActionResult> RegisterWebhook([FromBody] RegisterWebhookRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            return BadRequest(new { message = "Invalid URL format" });

        if (_workshop.Value.WebhookSsrf == "Fixed")
        {
            if (!IsAllowedUrl(request.Url, out var reason))
                return BadRequest(new { message = $"URL not allowed: {reason}" });
        }

        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var webhook = new Webhook { UserId = userId, Url = request.Url };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();

        var (pingStatusCode, pingBody, pingError) = await PingUrl(request.Url);
        return Ok(new RegisterWebhookResponse(
            new WebhookDto(webhook.Id, webhook.Url, webhook.CreatedAt),
            pingStatusCode,
            pingBody,
            pingError));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteWebhook(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (webhook == null) return NotFound();

        _db.Webhooks.Remove(webhook);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<IActionResult> TestWebhook([FromBody] WebhookTestRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            return BadRequest(new { message = "Invalid URL format" });

        if (_workshop.Value.WebhookSsrf == "Fixed")
        {
            if (!IsAllowedUrl(request.Url, out var reason))
                return BadRequest(new { message = $"URL not allowed: {reason}" });
        }

        var (statusCode, body, error) = await PingUrl(request.Url);
        return Ok(new { url = request.Url, statusCode, body, error });
    }

    private async Task<(string? StatusCode, string? Body, string? Error)> PingUrl(string url)
    {
        var client = _httpClientFactory.CreateClient("webhook");
        var payload = new StringContent(
            """{"event":"booking.created","test":true}""",
            System.Text.Encoding.UTF8, "application/json");
        try
        {
            var response = await client.PostAsync(url, payload);
            var body = await response.Content.ReadAsStringAsync();
            if (body.Length > 2000) body = body[..2000] + "…[truncated]";
            return (((int)response.StatusCode).ToString(), body, null);
        }
        catch (HttpRequestException ex)
        {
            return (null, null, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return (null, null, "Request timed out");
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
            else if (b.Length == 16 && b[0] == 0xfe && (b[1] & 0xc0) == 0x80)
            {
                reason = "IPv6 link-local addresses are not permitted";
                return false;
            }
        }

        return true;
    }
}
