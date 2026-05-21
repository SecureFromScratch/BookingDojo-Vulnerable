using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookingDojo.Api.Models;
using BookingDojo.Api.Tests.Infrastructure;

namespace BookingDojo.Api.Tests;

// ─── Shared setup ────────────────────────────────────────────────────────────

file static class BookingsSeed
{
    public static readonly Guid UserAId = Guid.NewGuid();
    public static readonly Guid UserBId = Guid.NewGuid();
    public static readonly Guid HotelId = Guid.NewGuid();

    public static void Apply(CustomWebApplicationFactory factory)
    {
        factory.SeedDatabase(db =>
        {
            if (!db.Hotels.Any())
            {
                db.Partners.Add(new Partner { Id = Guid.NewGuid(), Name = "Test Partner", IsActive = true });
                db.Hotels.Add(new Hotel
                {
                    Id = HotelId, PartnerId = db.Partners.Local.First().Id,
                    Name = "Test Hotel", Location = "Testville",
                    Description = "For tests", IsActive = true, CreatedAt = DateTime.UtcNow
                });
            }
        });
    }
}

// ─── Vulnerable mode tests ────────────────────────────────────────────────────

public class BookingsControllerVulnerableTests : IClassFixture<VulnerableWorkshopFactory>
{
    private readonly VulnerableWorkshopFactory _factory;

    public BookingsControllerVulnerableTests(VulnerableWorkshopFactory factory)
    {
        _factory = factory;
        BookingsSeed.Apply(factory);
    }

    private HttpClient Client(string role, Guid userId, string username = "user")
    {
        var client = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken(role, userId: userId, username: username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task CreateBooking_Returns201WithIntegerId()
    {
        var client = Client("AdminUser", BookingsSeed.UserAId);

        var response = await client.PostAsync("/api/bookings", Json(new
        {
            hotelId        = BookingsSeed.HotelId,
            checkIn        = DateTime.UtcNow.AddDays(5),
            checkOut       = DateTime.UtcNow.AddDays(8),
            cardNumber     = "4111111111111234",
            specialRequests = "Window seat"
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.True(body.GetProperty("id").GetInt32() > 0);
        Assert.Equal("1234", body.GetProperty("cardLastFour").GetString());
    }

    [Fact]
    public async Task CreateBooking_InvalidCard_Returns400()
    {
        var client = Client("AdminUser", BookingsSeed.UserAId);

        var response = await client.PostAsync("/api/bookings", Json(new
        {
            hotelId        = BookingsSeed.HotelId,
            checkIn        = DateTime.UtcNow.AddDays(5),
            checkOut       = DateTime.UtcNow.AddDays(8),
            cardNumber     = "abc",
            specialRequests = ""
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/bookings", Json(new
        {
            hotelId        = BookingsSeed.HotelId,
            checkIn        = DateTime.UtcNow.AddDays(5),
            checkOut       = DateTime.UtcNow.AddDays(8),
            cardNumber     = "9999",
            specialRequests = ""
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyBookings_ReturnsOnlyCallerBookings()
    {
        var clientA = Client("AdminUser", BookingsSeed.UserAId, "userA");
        await clientA.PostAsync("/api/bookings", Json(new
        {
            hotelId = BookingsSeed.HotelId, checkIn = DateTime.UtcNow.AddDays(1),
            checkOut = DateTime.UtcNow.AddDays(2), cardNumber = "4111111111111111", specialRequests = ""
        }));

        var clientB = Client("SupportUser", BookingsSeed.UserBId, "userB");
        await clientB.PostAsync("/api/bookings", Json(new
        {
            hotelId = BookingsSeed.HotelId, checkIn = DateTime.UtcNow.AddDays(3),
            checkOut = DateTime.UtcNow.AddDays(4), cardNumber = "4111111111112222", specialRequests = ""
        }));

        var response = await clientA.GetAsync("/api/bookings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var bookings = body.GetProperty("results").EnumerateArray().ToList();
        Assert.All(bookings, b => Assert.Equal(BookingsSeed.UserAId.ToString(), b.GetProperty("userId").GetString()));
        Assert.DoesNotContain(bookings, b => b.GetProperty("cardLastFour").GetString() == "2222");
    }

    [Fact]
    public async Task GetBookingById_Vulnerable_OtherUsersBooking_Returns200WithCardData()
    {
        var clientA = Client("AdminUser", BookingsSeed.UserAId, "userA");
        var createResp = await clientA.PostAsync("/api/bookings", Json(new
        {
            hotelId = BookingsSeed.HotelId, checkIn = DateTime.UtcNow.AddDays(10),
            checkOut = DateTime.UtcNow.AddDays(12), cardNumber = "4111111111115678", specialRequests = "Quiet room"
        }));
        var created = JsonSerializer.Deserialize<JsonElement>(await createResp.Content.ReadAsStringAsync());
        var bookingId = created.GetProperty("id").GetInt32();

        // UserB fetches UserA's booking by ID — succeeds in Vulnerable mode (IDOR)
        var clientB = Client("SupportUser", BookingsSeed.UserBId, "userB");
        var response = await clientB.GetAsync($"/api/bookings/{bookingId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Equal("5678", body.GetProperty("cardLastFour").GetString());
        Assert.Equal(BookingsSeed.UserAId.ToString(), body.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task GetBookingById_NonExistent_Returns404()
    {
        var client = Client("AdminUser", BookingsSeed.UserAId);
        var response = await client.GetAsync("/api/bookings/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBookingById_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/bookings/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

// ─── Resource consumption pageSize tests ─────────────────────────────────────

public class ResourceConsumptionVulnerableTests : IClassFixture<ResourceConsumptionVulnerableFactory>
{
    private readonly ResourceConsumptionVulnerableFactory _factory;
    private static readonly Guid HotelId = Guid.NewGuid();

    public ResourceConsumptionVulnerableTests(ResourceConsumptionVulnerableFactory factory)
    {
        _factory = factory;
        _factory.SeedDatabase(db =>
        {
            if (!db.Hotels.Any())
            {
                var partnerId = Guid.NewGuid();
                db.Partners.Add(new Partner { Id = partnerId, Name = "RC Partner", IsActive = true });
                db.Hotels.Add(new Hotel
                {
                    Id = HotelId, PartnerId = partnerId, Name = "RC Hotel",
                    Location = "Testville", Description = "RC", IsActive = true, CreatedAt = DateTime.UtcNow
                });
            }
        });
    }

    private HttpClient Client(Guid userId)
    {
        var c = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken("PartnerUser", userId: userId, username: "rcuser");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task SearchBookings_Vulnerable_ClientPageSizeIsHonoured()
    {
        var userId = Guid.NewGuid();
        var client = Client(userId);

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsync("/api/bookings", Json(new
            {
                hotelId = HotelId,
                checkIn = DateTime.UtcNow.AddDays(i + 1),
                checkOut = DateTime.UtcNow.AddDays(i + 2),
                cardNumber = $"411111111111{i:D2}66",
                specialRequests = ""
            }));
        }

        // Ask for 1 result — Vulnerable mode must honour this
        var response = await client.GetAsync("/api/bookings/search?q=&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var results = body.GetProperty("results").EnumerateArray().ToList();
        Assert.Single(results);                                   // server honoured client's pageSize=1
        Assert.True(body.GetProperty("truncated").GetBoolean());  // more exist but were truncated by client request
    }

    [Fact]
    public async Task SearchBookings_Vulnerable_NullPageSize_ReturnsAll()
    {
        var userId = Guid.NewGuid();
        var client = Client(userId);

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsync("/api/bookings", Json(new
            {
                hotelId = HotelId,
                checkIn = DateTime.UtcNow.AddDays(i + 10),
                checkOut = DateTime.UtcNow.AddDays(i + 11),
                cardNumber = $"411111111111{i:D2}77",
                specialRequests = ""
            }));
        }

        // No pageSize → no server-side cap → all 3 returned
        var response = await client.GetAsync("/api/bookings/search?q=");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var results = body.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(3, results.Count);
        Assert.False(body.GetProperty("truncated").GetBoolean());
    }
}
