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
            cardLastFour   = "1234",
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
            cardLastFour   = "abc",
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
            cardLastFour   = "9999",
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
            checkOut = DateTime.UtcNow.AddDays(2), cardLastFour = "1111", specialRequests = ""
        }));

        var clientB = Client("SupportUser", BookingsSeed.UserBId, "userB");
        await clientB.PostAsync("/api/bookings", Json(new
        {
            hotelId = BookingsSeed.HotelId, checkIn = DateTime.UtcNow.AddDays(3),
            checkOut = DateTime.UtcNow.AddDays(4), cardLastFour = "2222", specialRequests = ""
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
            checkOut = DateTime.UtcNow.AddDays(12), cardLastFour = "5678", specialRequests = "Quiet room"
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

// ─── Fixed mode tests ─────────────────────────────────────────────────────────

public class BookingsControllerFixedTests : IClassFixture<FixedWorkshopFactory>
{
    private readonly FixedWorkshopFactory _factory;

    public BookingsControllerFixedTests(FixedWorkshopFactory factory)
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
    public async Task GetBookingById_Fixed_OwnBooking_Returns200()
    {
        var clientA = Client("AdminUser", BookingsSeed.UserAId, "userA");
        var createResp = await clientA.PostAsync("/api/bookings", Json(new
        {
            hotelId = BookingsSeed.HotelId, checkIn = DateTime.UtcNow.AddDays(5),
            checkOut = DateTime.UtcNow.AddDays(7), cardLastFour = "3333", specialRequests = ""
        }));
        var created = JsonSerializer.Deserialize<JsonElement>(await createResp.Content.ReadAsStringAsync());
        var bookingId = created.GetProperty("id").GetInt32();

        var response = await clientA.GetAsync($"/api/bookings/{bookingId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Equal("3333", body.GetProperty("cardLastFour").GetString());
    }

    [Fact]
    public async Task GetBookingById_Fixed_OtherUsersBooking_Returns403()
    {
        // UserA creates a booking
        var clientA = Client("AdminUser", BookingsSeed.UserAId, "userA");
        var createResp = await clientA.PostAsync("/api/bookings", Json(new
        {
            hotelId = BookingsSeed.HotelId, checkIn = DateTime.UtcNow.AddDays(5),
            checkOut = DateTime.UtcNow.AddDays(7), cardLastFour = "4444", specialRequests = ""
        }));
        var created = JsonSerializer.Deserialize<JsonElement>(await createResp.Content.ReadAsStringAsync());
        var bookingId = created.GetProperty("id").GetInt32();

        // UserB tries to fetch it — should be denied in Fixed mode
        var clientB = Client("SupportUser", BookingsSeed.UserBId, "userB");
        var response = await clientB.GetAsync($"/api/bookings/{bookingId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetBookingById_Fixed_NonExistent_Returns404()
    {
        var client = Client("AdminUser", BookingsSeed.UserAId);
        var response = await client.GetAsync("/api/bookings/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SearchBookings_Fixed_ReturnsOnlyCallerMatchingBookings()
    {
        var clientA = Client("AdminUser", BookingsSeed.UserAId, "userA");
        await clientA.PostAsync("/api/bookings", Json(new
        {
            hotelId = BookingsSeed.HotelId, checkIn = DateTime.UtcNow.AddDays(1),
            checkOut = DateTime.UtcNow.AddDays(2), cardLastFour = "7777", specialRequests = ""
        }));

        var clientB = Client("SupportUser", BookingsSeed.UserBId, "userB");
        await clientB.PostAsync("/api/bookings", Json(new
        {
            hotelId = BookingsSeed.HotelId, checkIn = DateTime.UtcNow.AddDays(3),
            checkOut = DateTime.UtcNow.AddDays(4), cardLastFour = "8888", specialRequests = ""
        }));

        var response = await clientA.GetAsync("/api/bookings/search?q=test");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var results = body.GetProperty("results").EnumerateArray().ToList();
        Assert.All(results, r => Assert.Equal(BookingsSeed.UserAId.ToString(), r.GetProperty("userId").GetString()));
        Assert.DoesNotContain(results, r => r.GetProperty("cardLastFour").GetString() == "8888");
    }

    [Fact]
    public async Task SearchBookings_Fixed_NoMatch_ReturnsEmpty()
    {
        var client = Client("AdminUser", BookingsSeed.UserAId);
        var response = await client.GetAsync("/api/bookings/search?q=NonExistentHotelXYZ");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Empty(body.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task SearchBookings_Fixed_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/bookings/search?q=test");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SearchBookings_Fixed_ResponseIncludesTruncatedFalseWhenUnderCap()
    {
        var client = Client("AdminUser", BookingsSeed.UserAId, "userA");
        var response = await client.GetAsync("/api/bookings/search?q=");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.True(body.TryGetProperty("results", out _), "response must have 'results' array");
        Assert.True(body.TryGetProperty("truncated", out var truncated), "response must have 'truncated' flag");
        Assert.False(truncated.GetBoolean());
    }

    [Fact]
    public async Task SearchBookings_Fixed_ClientPageSizeIsIgnored()
    {
        // Create 3 bookings for a dedicated userId so the count is known
        var userId = Guid.NewGuid();
        var client = Client("AdminUser", userId, "pageSizeUser");
        for (var i = 0; i < 3; i++)
        {
            await client.PostAsync("/api/bookings", Json(new
            {
                hotelId = BookingsSeed.HotelId,
                checkIn = DateTime.UtcNow.AddDays(i + 1),
                checkOut = DateTime.UtcNow.AddDays(i + 2),
                cardLastFour = $"55{i:D2}",
                specialRequests = ""
            }));
        }

        // Ask for only 1 result — Fixed mode must ignore this and return all 3 (< server cap of 50)
        var response = await client.GetAsync("/api/bookings/search?q=&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var results = body.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(3, results.Count);                          // client asked for 1, server returned all 3
        Assert.False(body.GetProperty("truncated").GetBoolean()); // 3 < 50-cap → not truncated
    }
}

// ─── Resource consumption pageSize tests ─────────────────────────────────────
// Uses a factory where SQLi is Fixed (LINQ, works with InMemory) and
// resource consumption is Vulnerable so the client-controlled pageSize is honoured.

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
                cardLastFour = $"66{i:D2}",
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
                cardLastFour = $"77{i:D2}",
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
