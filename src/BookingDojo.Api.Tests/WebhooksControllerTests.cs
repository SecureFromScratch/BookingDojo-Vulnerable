using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookingDojo.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BookingDojo.Api.Tests;

// ─── Fake outbound HTTP handler ───────────────────────────────────────────────

file sealed class FakeWebhookMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"status":"received","test":true}""",
                Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

// ─── Test factories ───────────────────────────────────────────────────────────
// In the vulnerable-clean branch SSRF validation is always absent — no configuration needed.

public class VulnerableWebhookFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient("webhook")
                .ConfigurePrimaryHttpMessageHandler(() => new FakeWebhookMessageHandler());
        });
    }
}

public class FixedWebhookFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient("webhook")
                .ConfigurePrimaryHttpMessageHandler(() => new FakeWebhookMessageHandler());
        });
    }
}

// ─── Vulnerable mode tests ────────────────────────────────────────────────────

public class WebhooksControllerVulnerableTests : IClassFixture<VulnerableWebhookFactory>
{
    private readonly VulnerableWebhookFactory _factory;

    public WebhooksControllerVulnerableTests(VulnerableWebhookFactory factory)
        => _factory = factory;

    private HttpClient Client()
    {
        var c = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken("AdminUser");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task TestWebhook_Vulnerable_HttpsPublicUrl_Returns200()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "https://webhook.example.com/notify" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Vulnerable_HttpScheme_NotBlocked()
    {
        // Vulnerable: non-HTTPS URLs are not rejected
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "http://internal.corp/api" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Vulnerable_PrivateIp_NotBlocked()
    {
        // Vulnerable: private IP ranges pass through to the fake handler
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "http://169.254.169.254/latest/meta-data/" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        // Server returns the fake response body — not a 400 block
        Assert.True(body.TryGetProperty("statusCode", out _) || body.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task TestWebhook_Vulnerable_Localhost_NotBlocked()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "http://localhost:5432/db" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Vulnerable_InvalidUrl_Returns400()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "not-a-url" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Vulnerable_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync("/api/webhooks/test",
            Json(new { url = "https://example.com" }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

// ─── Fixed mode tests ─────────────────────────────────────────────────────────

public class WebhooksControllerFixedTests : IClassFixture<FixedWebhookFactory>
{
    private readonly FixedWebhookFactory _factory;

    public WebhooksControllerFixedTests(FixedWebhookFactory factory)
        => _factory = factory;

    private HttpClient Client()
    {
        var c = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken("AdminUser");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task TestWebhook_Fixed_HttpsPublicUrl_Returns200()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "https://webhook.example.com/notify" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Fixed_HttpScheme_Returns400()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "http://webhook.example.com/notify" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Contains("HTTPS", body.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestWebhook_Fixed_Localhost_Returns400()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "https://localhost/internal" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Fixed_PrivateIp_10x_Returns400()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "https://10.0.0.1/admin" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Fixed_PrivateIp_192168_Returns400()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "https://192.168.1.1/router" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Fixed_LinkLocalMetadata_Returns400()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "https://169.254.169.254/latest/meta-data/" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Fixed_InternalHostname_Returns400()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "https://db.internal/status" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TestWebhook_Fixed_InvalidUrl_Returns400()
    {
        var response = await Client().PostAsync("/api/webhooks/test",
            Json(new { url = "not-a-url" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
