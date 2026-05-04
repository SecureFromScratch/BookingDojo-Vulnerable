using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BookingDojo.Api.Models;
using BookingDojo.Api.Tests.Infrastructure;

namespace BookingDojo.Api.Tests;

public class PartnersControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    private static readonly Guid _activePartnerAId = Guid.NewGuid();
    private static readonly Guid _activePartnerBId = Guid.NewGuid();
    private static readonly Guid _inactivePartnerId = Guid.NewGuid();

    public PartnersControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        factory.SeedDatabase(db =>
        {
            if (!db.Partners.Any())
            {
                db.Partners.AddRange(
                    new Partner { Id = _activePartnerAId, Name = "Sunshine Hotels", IsActive = true },
                    new Partner { Id = _activePartnerBId, Name = "Mountain Retreats", IsActive = true },
                    new Partner { Id = _inactivePartnerId, Name = "Closed Partner", IsActive = false }
                );
            }
        });
    }

    [Fact]
    public async Task GetPartners_AsAdmin_ReturnsOnlyActivePartners()
    {
        var token = TestTokenHelper.GenerateToken("AdminUser");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/partners");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var partners = JsonSerializer.Deserialize<List<JsonElement>>(json)!;

        Assert.Equal(2, partners.Count);
        Assert.All(partners, p => Assert.False(string.IsNullOrEmpty(p.GetProperty("name").GetString())));
        Assert.DoesNotContain(partners, p => p.GetProperty("name").GetString() == "Closed Partner");
    }

    [Fact]
    public async Task GetPartners_AsAdmin_ReturnsCorrectShape()
    {
        var token = TestTokenHelper.GenerateToken("AdminUser");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/partners");
        var json = await response.Content.ReadAsStringAsync();
        var partners = JsonSerializer.Deserialize<List<JsonElement>>(json)!;

        var first = partners[0];
        Assert.True(first.TryGetProperty("id", out _), "Response must have 'id' property (camelCase)");
        Assert.True(first.TryGetProperty("name", out _), "Response must have 'name' property (camelCase)");
    }

    [Fact]
    public async Task GetPartners_WithNoAuth_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/partners");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPartners_AsPartnerUser_Returns403()
    {
        var token = TestTokenHelper.GenerateToken("PartnerUser", _activePartnerAId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/partners");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPartners_AsSupportUser_Returns403()
    {
        var token = TestTokenHelper.GenerateToken("SupportUser");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/partners");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
