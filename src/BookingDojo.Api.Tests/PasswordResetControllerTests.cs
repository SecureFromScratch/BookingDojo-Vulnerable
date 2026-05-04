using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookingDojo.Api.Models;
using BookingDojo.Api.Tests.Infrastructure;
using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BookingDojo.Api.Tests;

// ─── Shared seed ─────────────────────────────────────────────────────────────

file static class PasswordResetSeed
{
    public static readonly Guid UserId = Guid.NewGuid();
    public const string Username = "resetuser";
    public const string InitialPassword = "Initial1234!";

    public static void Apply(CustomWebApplicationFactory factory)
    {
        factory.SeedDatabase(db =>
        {
            if (!db.Users.Any(u => u.Username == Username))
            {
                db.Users.Add(new User
                {
                    Id           = UserId,
                    Username     = Username,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(InitialPassword),
                    Role         = "PartnerUser"
                });
            }
        });
    }
}

// ─── Vulnerable factory ───────────────────────────────────────────────────────

public class VulnerablePasswordResetFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<WorkshopOptions>(o =>
                o.PasswordResetRaceCondition = "Vulnerable"));
    }
}

// ─── Vulnerable mode tests ────────────────────────────────────────────────────

public class PasswordResetVulnerableTests : IClassFixture<VulnerablePasswordResetFactory>
{
    private readonly VulnerablePasswordResetFactory _factory;

    public PasswordResetVulnerableTests(VulnerablePasswordResetFactory factory)
    {
        _factory = factory;
        PasswordResetSeed.Apply(factory);
    }

    private HttpClient AnonClient() => _factory.CreateClient();

    private StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task ForgotPassword_KnownUser_ReturnsToken()
    {
        var response = await AnonClient().PostAsync("/api/auth/forgot-password",
            Json(new { username = PasswordResetSeed.Username }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.True(body.TryGetProperty("resetToken", out var token));
        Assert.False(string.IsNullOrEmpty(token.GetString()));
    }

    [Fact]
    public async Task ForgotPassword_UnknownUser_Returns404()
    {
        var response = await AnonClient().PostAsync("/api/auth/forgot-password",
            Json(new { username = "nobody" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_Vulnerable_ValidToken_ChangesPassword()
    {
        var client = AnonClient();
        var forgotResp = await client.PostAsync("/api/auth/forgot-password",
            Json(new { username = PasswordResetSeed.Username }));
        var forgotBody = JsonSerializer.Deserialize<JsonElement>(await forgotResp.Content.ReadAsStringAsync());
        var token = forgotBody.GetProperty("resetToken").GetString()!;

        var resetResp = await client.PostAsync("/api/auth/reset-password",
            Json(new { token, newPassword = "NewPass5678!" }));
        Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_Vulnerable_WrongToken_Returns400()
    {
        var response = await AnonClient().PostAsync("/api/auth/reset-password",
            Json(new { token = "not-a-real-token", newPassword = "NewPass5678!" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_Vulnerable_ShortPassword_Returns400()
    {
        var client = AnonClient();
        var forgotResp = await client.PostAsync("/api/auth/forgot-password",
            Json(new { username = PasswordResetSeed.Username }));
        var forgotBody = JsonSerializer.Deserialize<JsonElement>(await forgotResp.Content.ReadAsStringAsync());
        var token = forgotBody.GetProperty("resetToken").GetString()!;

        var response = await client.PostAsync("/api/auth/reset-password",
            Json(new { token, newPassword = "short" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Demonstrates the TOCTOU race: two concurrent requests with the same token both succeed
    // because the 500 ms delay lets them both pass the UsedAt IS NULL check before either writes.
    [Fact]
    public async Task ResetPassword_Vulnerable_ConcurrentRequests_BothSucceed()
    {
        var client = AnonClient();
        var forgotResp = await client.PostAsync("/api/auth/forgot-password",
            Json(new { username = PasswordResetSeed.Username }));
        var forgotBody = JsonSerializer.Deserialize<JsonElement>(await forgotResp.Content.ReadAsStringAsync());
        var token = forgotBody.GetProperty("resetToken").GetString()!;

        var body = Json(new { token, newPassword = "Concurrent1!" });

        // Fire both requests simultaneously — both land during the artificial delay window
        var t1 = _factory.CreateClient().PostAsync("/api/auth/reset-password",
            new StringContent(JsonSerializer.Serialize(new { token, newPassword = "Concurrent1!" }), Encoding.UTF8, "application/json"));
        var t2 = _factory.CreateClient().PostAsync("/api/auth/reset-password",
            new StringContent(JsonSerializer.Serialize(new { token, newPassword = "Concurrent2!" }), Encoding.UTF8, "application/json"));

        var results = await Task.WhenAll(t1, t2);

        // In vulnerable mode both succeed — the race was won by both
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}

// ─── Fixed mode tests ─────────────────────────────────────────────────────────

public class PasswordResetFixedTests : IClassFixture<FixedWorkshopFactory>
{
    private readonly FixedWorkshopFactory _factory;

    public PasswordResetFixedTests(FixedWorkshopFactory factory)
    {
        _factory = factory;
        PasswordResetSeed.Apply(factory);
    }

    private HttpClient AnonClient() => _factory.CreateClient();

    private StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task ResetPassword_Fixed_ValidToken_ChangesPassword()
    {
        var client = AnonClient();
        var forgotResp = await client.PostAsync("/api/auth/forgot-password",
            Json(new { username = PasswordResetSeed.Username }));
        var forgotBody = JsonSerializer.Deserialize<JsonElement>(await forgotResp.Content.ReadAsStringAsync());
        var token = forgotBody.GetProperty("resetToken").GetString()!;

        var response = await client.PostAsync("/api/auth/reset-password",
            Json(new { token, newPassword = "FixedPass9!" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_Fixed_SecondCallWithSameToken_Returns409()
    {
        var client = AnonClient();
        var forgotResp = await client.PostAsync("/api/auth/forgot-password",
            Json(new { username = PasswordResetSeed.Username }));
        var forgotBody = JsonSerializer.Deserialize<JsonElement>(await forgotResp.Content.ReadAsStringAsync());
        var token = forgotBody.GetProperty("resetToken").GetString()!;

        var first = await client.PostAsync("/api/auth/reset-password",
            Json(new { token, newPassword = "FixedPass9!" }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync("/api/auth/reset-password",
            Json(new { token, newPassword = "FixedPass9!" }));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_Fixed_InvalidToken_Returns409()
    {
        var response = await AnonClient().PostAsync("/api/auth/reset-password",
            Json(new { token = "badtoken", newPassword = "FixedPass9!" }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_Fixed_KnownUser_ReturnsToken()
    {
        var response = await AnonClient().PostAsync("/api/auth/forgot-password",
            Json(new { username = PasswordResetSeed.Username }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.True(body.TryGetProperty("resetToken", out _));
    }
}
