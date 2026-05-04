using System.Net;
using BookingDojo.Api.Models;
using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
[Authorize]
public class WebhooksController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<WorkshopOptions> _workshop;

    public WebhooksController(IHttpClientFactory httpClientFactory, IOptions<WorkshopOptions> workshop)
    {
        _httpClientFactory = httpClientFactory;
        _workshop = workshop;
    }

    [HttpPost("test")]
    public async Task<IActionResult> TestWebhook([FromBody] WebhookTestRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            return BadRequest(new { message = "Invalid URL format" });

        if (_workshop.Value.WebhookSsrf == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH
            // The server fetches any URL the caller provides — no host, scheme, or IP validation.
            // Targets: internal APIs, cloud metadata (169.254.169.254), loopback services, etc.
            return await FetchUrl(request.Url);
        }
        else
        {
            // WORKSHOP: FIXED PATH
            // Validate the URL before making any outbound request.
            if (!IsAllowedUrl(request.Url, out var reason))
                return BadRequest(new { message = $"URL not allowed: {reason}" });

            return await FetchUrl(request.Url);
        }
    }

    private async Task<IActionResult> FetchUrl(string url)
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
            return Ok(new { url, statusCode = (int)response.StatusCode, body });
        }
        catch (HttpRequestException ex)
        {
            return Ok(new { url, error = ex.Message });
        }
        catch (TaskCanceledException)
        {
            return Ok(new { url, error = "Request timed out" });
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
                    || (b[0] == 169 && b[1] == 254)   // link-local / AWS metadata
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
