using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BookingDojo.Api.Tests.Infrastructure;

namespace BookingDojo.Api.Tests;

// ─── Vulnerable factory ───────────────────────────────────────────────────────
// In the vulnerable-clean branch exception details are always disclosed.

public class VulnerableExceptionFactory : CustomWebApplicationFactory { }

// ─── Vulnerable mode tests ────────────────────────────────────────────────────

public class ExceptionDisclosureVulnerableTests : IClassFixture<VulnerableExceptionFactory>
{
    private readonly VulnerableExceptionFactory _factory;

    public ExceptionDisclosureVulnerableTests(VulnerableExceptionFactory factory)
        => _factory = factory;

    private HttpClient Client()
    {
        var c = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken("AdminUser");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task TriggerError_Vulnerable_Returns500()
    {
        var response = await Client().GetAsync("/api/debug/throw");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task TriggerError_Vulnerable_ResponseContainsExceptionType()
    {
        var response = await Client().GetAsync("/api/debug/throw");
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.True(body.TryGetProperty("type", out var type), "response must expose exception type");
        Assert.Contains("InvalidOperationException", type.GetString()!);
    }

    [Fact]
    public async Task TriggerError_Vulnerable_ResponseContainsStackTrace()
    {
        var response = await Client().GetAsync("/api/debug/throw");
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.True(body.TryGetProperty("stackTrace", out var st), "response must expose stack trace");
        Assert.False(string.IsNullOrEmpty(st.GetString()));
    }

    [Fact]
    public async Task TriggerError_Vulnerable_ResponseContainsSensitiveMessage()
    {
        var response = await Client().GetAsync("/api/debug/throw");
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.True(body.TryGetProperty("error", out var error));
        // The fake connection string is in the exception message
        Assert.Contains("Password=", error.GetString()!);
    }
}

