using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookingDojo.Api.Tests.Infrastructure;
using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BookingDojo.Api.Tests;

// ─── Factories ────────────────────────────────────────────────────────────────

public class MfaVulnerableFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<WorkshopOptions>(o => o.MfaBruteForceProtection = "Vulnerable"));
    }
}

public class MfaFixedFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<WorkshopOptions>(o => o.MfaBruteForceProtection = "Fixed"));
    }
}

// ─── Base ─────────────────────────────────────────────────────────────────────

public abstract class MfaTestBase
{
    protected readonly CustomWebApplicationFactory _factory;

    protected MfaTestBase(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    protected HttpClient Client(Guid? userId = null)
    {
        var client = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken("PartnerUser", userId: userId ?? Guid.NewGuid(), username: "mfauser");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected StringContent Json(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    protected async Task<JsonElement> Body(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync());

    protected async Task<string> RequestAndGetCode(HttpClient client)
    {
        await client.PostAsync("/api/auth/mfa/challenge", null);
        var otp = await client.GetAsync("/api/auth/mfa/otp");
        var body = await Body(otp);
        return body.GetProperty("code").GetString()!;
    }
}

// ─── Shared behaviour (mode-independent) ─────────────────────────────────────

public class MfaSharedTests : MfaTestBase, IClassFixture<MfaVulnerableFactory>
{
    public MfaSharedTests(MfaVulnerableFactory factory) : base(factory) { }

    [Fact]
    public async Task Challenge_Returns200WithExpiry()
    {
        var r = await Client().PostAsync("/api/auth/mfa/challenge", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Body(r);
        Assert.True(body.TryGetProperty("expiresAt", out _));
        Assert.Equal(10, body.GetProperty("ttlMinutes").GetInt32());
    }

    [Fact]
    public async Task GetOtp_WithoutChallenge_Returns404()
    {
        var r = await Client().GetAsync("/api/auth/mfa/otp");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task GetOtp_AfterChallenge_ReturnsFourDigitCode()
    {
        var client = Client(Guid.NewGuid());
        await client.PostAsync("/api/auth/mfa/challenge", null);
        var r = await client.GetAsync("/api/auth/mfa/otp");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Body(r);
        var code = body.GetProperty("code").GetString()!;
        Assert.Equal(4, code.Length);
        Assert.True(code.All(char.IsDigit));
    }

    [Fact]
    public async Task Verify_CorrectCode_Returns200()
    {
        var client = Client(Guid.NewGuid());
        var code = await RequestAndGetCode(client);

        var r = await client.PostAsync("/api/auth/mfa/verify", Json(new { code }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await Body(r);
        Assert.True(body.GetProperty("verified").GetBoolean());
    }

    [Fact]
    public async Task Verify_WrongCode_Returns401()
    {
        var client = Client(Guid.NewGuid());
        await client.PostAsync("/api/auth/mfa/challenge", null);

        var r = await client.PostAsync("/api/auth/mfa/verify", Json(new { code = "XXXX" }));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Challenge_Invalidates_PreviousChallenge()
    {
        var client = Client(Guid.NewGuid());
        var oldCode = await RequestAndGetCode(client);

        // Request a new challenge — old code should be gone
        await client.PostAsync("/api/auth/mfa/challenge", null);
        var newCode = await RequestAndGetCode(client);

        // Old code must not work
        var r = await client.PostAsync("/api/auth/mfa/verify", Json(new { code = oldCode }));
        // new code is different from old (overwhelmingly likely), so old code returns 401
        if (oldCode != newCode)
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var r = await client.PostAsync("/api/auth/mfa/challenge", null);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}

// ─── Vulnerable mode ──────────────────────────────────────────────────────────

public class MfaVulnerableTests : MfaTestBase, IClassFixture<MfaVulnerableFactory>
{
    public MfaVulnerableTests(MfaVulnerableFactory factory) : base(factory) { }

    [Fact]
    public async Task Vulnerable_UnlimitedRetries_CanVerifyAfterManyFailures()
    {
        var client = Client(Guid.NewGuid());
        var code = await RequestAndGetCode(client);

        // Send 20 wrong attempts — should all return 401, never 429
        for (var i = 0; i < 20; i++)
        {
            var wrong = code == "0000" ? "1111" : "0000";
            var r = await client.PostAsync("/api/auth/mfa/verify", Json(new { code = wrong }));
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        // Correct code must still work after unlimited wrong attempts
        var final = await client.PostAsync("/api/auth/mfa/verify", Json(new { code }));
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
    }

    [Fact]
    public async Task Vulnerable_GetOtp_AttemptsRemainingIsNull()
    {
        var client = Client(Guid.NewGuid());
        await client.PostAsync("/api/auth/mfa/challenge", null);
        var r = await client.GetAsync("/api/auth/mfa/otp");
        var body = await Body(r);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attemptsRemaining").ValueKind);
    }
}

// ─── Fixed mode ───────────────────────────────────────────────────────────────

public class MfaFixedTests : MfaTestBase, IClassFixture<MfaFixedFactory>
{
    public MfaFixedTests(MfaFixedFactory factory) : base(factory) { }

    [Fact]
    public async Task Fixed_LockedOutAfterFiveFailures_Returns429()
    {
        var client = Client(Guid.NewGuid());
        var code = await RequestAndGetCode(client);
        var wrong = code == "0000" ? "1111" : "0000";

        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++)
            last = await client.PostAsync("/api/auth/mfa/verify", Json(new { code = wrong }));

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
    }

    [Fact]
    public async Task Fixed_AfterLockout_ChallengeIsInvalidated_VerifyReturns404()
    {
        var client = Client(Guid.NewGuid());
        var code = await RequestAndGetCode(client);
        var wrong = code == "0000" ? "1111" : "0000";

        for (var i = 0; i < 5; i++)
            await client.PostAsync("/api/auth/mfa/verify", Json(new { code = wrong }));

        // Challenge is gone — verify should 404
        var r = await client.PostAsync("/api/auth/mfa/verify", Json(new { code }));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Fixed_AttemptsRemainingDecrementsOnEachFailure()
    {
        var client = Client(Guid.NewGuid());
        var code = await RequestAndGetCode(client);
        var wrong = code == "0000" ? "1111" : "0000";

        await client.PostAsync("/api/auth/mfa/verify", Json(new { code = wrong }));

        var otp = await client.GetAsync("/api/auth/mfa/otp");
        var body = await Body(otp);
        Assert.Equal(4, body.GetProperty("attemptsRemaining").GetInt32()); // 5 - 1
    }

    [Fact]
    public async Task Fixed_CanRequestNewChallengeAfterLockout()
    {
        var client = Client(Guid.NewGuid());
        var code = await RequestAndGetCode(client);
        var wrong = code == "0000" ? "1111" : "0000";

        for (var i = 0; i < 5; i++)
            await client.PostAsync("/api/auth/mfa/verify", Json(new { code = wrong }));

        // Should be able to start fresh
        var newChallenge = await client.PostAsync("/api/auth/mfa/challenge", null);
        Assert.Equal(HttpStatusCode.OK, newChallenge.StatusCode);

        var newCode = await RequestAndGetCode(client);
        var verify = await client.PostAsync("/api/auth/mfa/verify", Json(new { code = newCode }));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }
}
