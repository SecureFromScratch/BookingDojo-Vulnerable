using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
[Authorize]
public class WebhooksController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BookingDojoDbContext _db;

    public WebhooksController(IHttpClientFactory httpClientFactory, BookingDojoDbContext db)
    {
        _httpClientFactory = httpClientFactory;
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

        // VULNERABLE PATH — no URL validation, any URL is accepted (SSRF)
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

        // VULNERABLE PATH — no URL validation, any URL is fetched (SSRF)
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
}
