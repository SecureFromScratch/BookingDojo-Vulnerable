using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookingDojo.Api.Models;
using BookingDojo.Api.Tests.Infrastructure;

namespace BookingDojo.Api.Tests;

// ─── Factories ────────────────────────────────────────────────────────────────
// In the vulnerable-clean branch all modes are always vulnerable — no configuration needed.

public class AuditVulnerableFactory : CustomWebApplicationFactory { }

// ─── Shared helpers ───────────────────────────────────────────────────────────

public abstract class AuditManipulationBase
{
    protected readonly CustomWebApplicationFactory _factory;

    protected AuditManipulationBase(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    protected HttpClient Client(string role, string username = "auditor")
    {
        var client = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken(role, userId: Guid.NewGuid(), username: username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected HttpClient AnonClient() => _factory.CreateClient();

    protected StringContent Json(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    protected async Task<JsonElement> Body(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync());

    // Seeds one audit log entry directly and returns its id.
    protected async Task<string> SeedLogEntry(string action = "TEST_ACTION")
    {
        _factory.SeedDatabase(db =>
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id        = Guid.NewGuid(),
                UserId    = Guid.Empty,
                Username  = "seeded_user",
                Action    = action,
                Details   = $"Seeded entry for {action}",
                Timestamp = DateTime.UtcNow,
                IpAddress = "127.0.0.1",
            });
            db.SaveChanges();
        });

        var r = await Client("AdminUser").GetAsync("/api/audit-logs");
        var logs = (await Body(r)).EnumerateArray().ToList();
        return logs.First(l => l.GetProperty("action").GetString() == action)
                   .GetProperty("id").GetString()!;
    }
}

// ─── Log injection tests (vulnerable) ────────────────────────────────────────

public class LogInjectionVulnerableTests : AuditManipulationBase, IClassFixture<AuditVulnerableFactory>
{
    public LogInjectionVulnerableTests(AuditVulnerableFactory factory) : base(factory) { }

    [Fact]
    public async Task Login_WithNewlineInUsername_AuditEntryIsCreated()
    {
        // A failed login attempt with a crafted username creates an audit entry.
        // In Vulnerable mode the raw \n is logged to ILogger (visible in the server
        // console as a fake log line). The DB entry stores the username as-is.
        await AnonClient().PostAsync("/api/auth/login", Json(new
        {
            username = "alice\n[CRITICAL] fake escalation",
            password = "wrong"
        }));

        var r = await Client("AdminUser").GetAsync("/api/audit-logs");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var logs = (await Body(r)).EnumerateArray().ToList();
        // At least one LOGIN_FAILED entry with the injected username must exist.
        var entry = logs.FirstOrDefault(l =>
            l.GetProperty("action").GetString() == "LOGIN_FAILED" &&
            (l.GetProperty("username").GetString() ?? "").StartsWith("alice"));
        Assert.True(entry.ValueKind != JsonValueKind.Undefined,
            "Expected a LOGIN_FAILED audit entry for the crafted username");
    }

    [Fact]
    public async Task Login_Normal_AuditEntryIsCreated()
    {
        // Baseline: a plain failed login still creates an audit entry.
        await AnonClient().PostAsync("/api/auth/login", Json(new
        {
            username = "nobody",
            password = "wrong"
        }));

        var r = await Client("AdminUser").GetAsync("/api/audit-logs");
        var logs = (await Body(r)).EnumerateArray().ToList();
        Assert.Contains(logs, l => l.GetProperty("action").GetString() == "LOGIN_FAILED");
    }
}

// ─── Audit log deletion (vulnerable) ─────────────────────────────────────────

public class AuditDeletionVulnerableTests : AuditManipulationBase, IClassFixture<AuditVulnerableFactory>
{
    public AuditDeletionVulnerableTests(AuditVulnerableFactory factory) : base(factory) { }

    [Fact]
    public async Task Vulnerable_AdminUser_CanDeleteEntry()
    {
        var id = await SeedLogEntry("ADMIN_DELETE");
        var r = await Client("AdminUser").DeleteAsync($"/api/audit-logs/{id}");
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
    }

    [Fact]
    public async Task Vulnerable_SupportUser_CanDeleteEntry()
    {
        // In vulnerable mode SupportUser faces no extra restriction — they can erase evidence.
        var id = await SeedLogEntry("SUPPORT_DELETE");
        var r = await Client("SupportUser").DeleteAsync($"/api/audit-logs/{id}");
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
    }

    [Fact]
    public async Task Vulnerable_DeletionLeavesNoSecondaryTrace()
    {
        var id = await SeedLogEntry("COVER_TRACKS");
        await Client("SupportUser").DeleteAsync($"/api/audit-logs/{id}");

        var logs = (await Body(await Client("AdminUser").GetAsync("/api/audit-logs")))
            .EnumerateArray().ToList();
        Assert.DoesNotContain(logs,
            l => l.GetProperty("action").GetString() == "LOG_ENTRY_DELETED");
    }

    [Fact]
    public async Task Vulnerable_DeleteNonExistent_Returns404()
    {
        var r = await Client("AdminUser").DeleteAsync($"/api/audit-logs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}

