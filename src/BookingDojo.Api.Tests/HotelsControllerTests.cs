using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookingDojo.Api.Models;
using BookingDojo.Api.Tests.Infrastructure;

namespace BookingDojo.Api.Tests;

public class HotelsControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    private static readonly Guid _partnerAId = Guid.NewGuid();
    private static readonly Guid _partnerBId = Guid.NewGuid();

    public HotelsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        factory.SeedDatabase(db =>
        {
            if (!db.Partners.Any())
            {
                db.Partners.AddRange(
                    new Partner { Id = _partnerAId, Name = "Partner A", IsActive = true },
                    new Partner { Id = _partnerBId, Name = "Partner B", IsActive = true }
                );
                db.Hotels.AddRange(
                    new Hotel { Id = Guid.NewGuid(), PartnerId = _partnerAId, Name = "Hotel Alpha", Location = "Paris", Description = "Nice", IsActive = true, CreatedAt = DateTime.UtcNow },
                    new Hotel { Id = Guid.NewGuid(), PartnerId = _partnerBId, Name = "Hotel Beta", Location = "Rome", Description = "Great", IsActive = true, CreatedAt = DateTime.UtcNow }
                );
            }
        });
    }

    private StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task GetHotels_AsAdmin_ReturnsAllActiveHotels()
    {
        var token = TestTokenHelper.GenerateToken("AdminUser");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/hotels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var hotels = JsonSerializer.Deserialize<List<JsonElement>>(json)!;
        Assert.True(hotels.Count >= 2);
    }

    [Fact]
    public async Task GetHotels_AsPartnerUser_ReturnsOnlyOwnHotels()
    {
        var token = TestTokenHelper.GenerateToken("PartnerUser", _partnerAId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/hotels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var hotels = JsonSerializer.Deserialize<List<JsonElement>>(json)!;
        Assert.All(hotels, h => Assert.Equal(_partnerAId.ToString(), h.GetProperty("partnerId").GetString()));
    }

    [Fact]
    public async Task CreateHotel_AsAdmin_WithValidPartnerId_Returns201()
    {
        var token = TestTokenHelper.GenerateToken("AdminUser");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync("/api/hotels", Json(new
        {
            name = "Grand Hotel Test",
            location = "Berlin",
            description = "A test hotel",
            partnerId = _partnerAId
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var hotel = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("Grand Hotel Test", hotel.GetProperty("name").GetString());
        Assert.Equal("Partner A", hotel.GetProperty("partnerName").GetString());
    }

    [Fact]
    public async Task CreateHotel_AsAdmin_WithoutPartnerId_Returns400WithMessage()
    {
        var token = TestTokenHelper.GenerateToken("AdminUser");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync("/api/hotels", Json(new
        {
            name = "No Partner Hotel",
            location = "Nowhere",
            description = "Missing partner"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("AdminUser must specify a partnerId", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CreateHotel_AsAdmin_WithInactivePartner_Returns400()
    {
        var inactivePartnerId = Guid.NewGuid();
        _factory.SeedDatabase(db =>
        {
            if (!db.Partners.Any(p => p.Id == inactivePartnerId))
                db.Partners.Add(new Partner { Id = inactivePartnerId, Name = "Inactive", IsActive = false });
        });

        var token = TestTokenHelper.GenerateToken("AdminUser");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync("/api/hotels", Json(new
        {
            name = "Hotel for Inactive Partner",
            location = "Nowhere",
            description = "Should fail",
            partnerId = inactivePartnerId
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateHotel_AsPartnerUser_Returns201UsingOwnPartner()
    {
        var token = TestTokenHelper.GenerateToken("PartnerUser", _partnerAId, "partner");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync("/api/hotels", Json(new
        {
            name = "Partner Hotel Test",
            location = "Madrid",
            description = "Created by partner"
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var hotel = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(_partnerAId.ToString(), hotel.GetProperty("partnerId").GetString());
    }

    [Fact]
    public async Task CreateHotel_WithNoAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsync("/api/hotels", Json(new
        {
            name = "Unauthorized Hotel",
            location = "Nowhere",
            description = "Should fail"
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── /api/hotels/available ────────────────────────────────────────────────

    [Fact]
    public async Task GetAvailableHotels_AsPartnerUser_ReturnsAllActiveHotels_NotJustOwn()
    {
        // PartnerUser belongs to Partner A but should see hotels from BOTH partners
        var token = TestTokenHelper.GenerateToken("PartnerUser", _partnerAId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/hotels/available");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hotels = JsonSerializer.Deserialize<List<JsonElement>>(
            await response.Content.ReadAsStringAsync())!;

        Assert.True(hotels.Count >= 2, "PartnerUser should see hotels from all partners on /available");
        var partnerIds = hotels.Select(h => h.GetProperty("partnerId").GetString()).ToHashSet();
        Assert.Contains(_partnerAId.ToString(), partnerIds);
        Assert.Contains(_partnerBId.ToString(), partnerIds);
    }

    [Fact]
    public async Task GetAvailableHotels_AsAdmin_ReturnsAllActiveHotels()
    {
        var token = TestTokenHelper.GenerateToken("AdminUser");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/hotels/available");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hotels = JsonSerializer.Deserialize<List<JsonElement>>(
            await response.Content.ReadAsStringAsync())!;
        Assert.True(hotels.Count >= 2);
    }

    [Fact]
    public async Task GetAvailableHotels_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/hotels/available");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
