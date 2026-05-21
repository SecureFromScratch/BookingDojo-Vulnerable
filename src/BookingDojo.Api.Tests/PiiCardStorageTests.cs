using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookingDojo.Api.Models;
using BookingDojo.Api.Tests.Infrastructure;

namespace BookingDojo.Api.Tests;

// ─── Workshop factories ───────────────────────────────────────────────────────
// In the vulnerable-clean branch PII storage is always vulnerable (full card number stored).

public class PiiVulnerableFactory : CustomWebApplicationFactory { }

// ─── Shared seed ─────────────────────────────────────────────────────────────

file static class PiiSeed
{
    public static readonly Guid HotelId   = Guid.NewGuid();
    public static readonly Guid PartnerId = Guid.NewGuid();

    public static void Apply(CustomWebApplicationFactory factory)
    {
        factory.SeedDatabase(db =>
        {
            if (!db.Hotels.Any())
            {
                db.Partners.Add(new Partner { Id = PartnerId, Name = "PII Partner", IsActive = true });
                db.Hotels.Add(new Hotel
                {
                    Id = HotelId, PartnerId = PartnerId,
                    Name = "PII Hotel", Location = "Testville",
                    Description = "desc", IsActive = true, CreatedAt = DateTime.UtcNow,
                });
                db.SaveChanges();
            }
        });
    }
}

// ─── Base ─────────────────────────────────────────────────────────────────────

public abstract class PiiTestBase
{
    protected readonly CustomWebApplicationFactory _factory;

    protected PiiTestBase(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        PiiSeed.Apply(factory);
    }

    protected HttpClient Client(Guid? userId = null, string role = "PartnerUser", string username = "user")
    {
        var client = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken(role, userId: userId ?? Guid.NewGuid(), username: username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected StringContent Json(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    protected async Task<JsonElement> Body(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync());

    protected Task<HttpResponseMessage> CreateBooking(HttpClient client, string cardNumber = "4111111111111234") =>
        client.PostAsync("/api/bookings", Json(new
        {
            hotelId = PiiSeed.HotelId,
            checkIn = DateTime.UtcNow.AddDays(5),
            checkOut = DateTime.UtcNow.AddDays(8),
            cardNumber,
            specialRequests = "",
        }));
}

// ─── Vulnerable mode ──────────────────────────────────────────────────────────

public class PiiVulnerableTests : PiiTestBase, IClassFixture<PiiVulnerableFactory>
{
    public PiiVulnerableTests(PiiVulnerableFactory factory) : base(factory) { }

    [Fact]
    public async Task CreateBooking_Vulnerable_ResponseContainsFullCardNumber()
    {
        var client = Client();
        var r = await CreateBooking(client, "5500005555554242");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);

        var body = await Body(r);
        Assert.Equal("4242", body.GetProperty("cardLastFour").GetString());
        Assert.Equal("5500005555554242", body.GetProperty("cardNumber").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cardToken").ValueKind);
    }

    [Fact]
    public async Task GetBookingById_Vulnerable_IgorExposeFullCardNumber()
    {
        // UserA creates a booking
        var userA = Client(Guid.NewGuid(), username: "userA");
        var created = await Body(await CreateBooking(userA, "4111111111111111"));
        var bookingId = created.GetProperty("id").GetInt32();

        // UserB fetches it via IDOR — gets the full card number
        var userB = Client(Guid.NewGuid(), username: "userB");
        var r = await userB.GetAsync($"/api/bookings/{bookingId}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Body(r);
        Assert.Equal("4111111111111111", body.GetProperty("cardNumber").GetString());
    }

    [Fact]
    public async Task CreateBooking_InvalidCardNumber_Returns400()
    {
        var client = Client();
        var r = await CreateBooking(client, "abc");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_ShortCardNumber_Returns400()
    {
        var client = Client();
        var r = await CreateBooking(client, "1234"); // 4 digits — too short
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}

