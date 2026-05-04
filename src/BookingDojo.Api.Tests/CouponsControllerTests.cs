using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookingDojo.Api.Models;
using BookingDojo.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using BookingDojo.Api.Workshop;

namespace BookingDojo.Api.Tests;

// ─── Shared seed ─────────────────────────────────────────────────────────────

file static class CouponsSeed
{
    public static readonly Guid UserId = Guid.NewGuid();

    public static void Apply(CustomWebApplicationFactory factory)
    {
        factory.SeedDatabase(db =>
        {
            if (!db.Coupons.Any())
            {
                db.Coupons.AddRange(
                    new Coupon { Code = "ONCE10",   DiscountPercent = 10, MaxUses = 1, UsesCount = 0 },
                    new Coupon { Code = "MULTI20",  DiscountPercent = 20, MaxUses = 5, UsesCount = 0 },
                    new Coupon { Code = "SPENT",    DiscountPercent = 15, MaxUses = 1, UsesCount = 1 }
                );
            }
        });
    }
}

// ─── Vulnerable mode factory ──────────────────────────────────────────────────

public class VulnerableCouponFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<WorkshopOptions>(o =>
                o.CouponRedemptionRaceCondition = "Vulnerable"));
    }
}

// ─── Vulnerable mode tests ────────────────────────────────────────────────────

public class CouponsControllerVulnerableTests : IClassFixture<VulnerableCouponFactory>
{
    private readonly VulnerableCouponFactory _factory;

    public CouponsControllerVulnerableTests(VulnerableCouponFactory factory)
    {
        _factory = factory;
        CouponsSeed.Apply(factory);
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken("PartnerUser", userId: CouponsSeed.UserId, username: "tester");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task RedeemCoupon_Vulnerable_ValidCode_Returns200()
    {
        var response = await Client().PostAsync("/api/coupons/redeem", Json(new { code = "MULTI20" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Equal(20, body.GetProperty("discountPercent").GetInt32());
    }

    [Fact]
    public async Task RedeemCoupon_Vulnerable_AlreadyExhausted_Returns409()
    {
        var response = await Client().PostAsync("/api/coupons/redeem", Json(new { code = "SPENT" }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RedeemCoupon_Vulnerable_UnknownCode_Returns404()
    {
        var response = await Client().PostAsync("/api/coupons/redeem", Json(new { code = "DOESNOTEXIST" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RedeemCoupon_Vulnerable_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/coupons/redeem", Json(new { code = "MULTI20" }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RedeemCoupon_Vulnerable_EmptyCode_Returns400()
    {
        var response = await Client().PostAsync("/api/coupons/redeem", Json(new { code = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

// ─── Fixed mode tests ─────────────────────────────────────────────────────────

public class CouponsControllerFixedTests : IClassFixture<FixedWorkshopFactory>
{
    private readonly FixedWorkshopFactory _factory;

    public CouponsControllerFixedTests(FixedWorkshopFactory factory)
    {
        _factory = factory;
        CouponsSeed.Apply(factory);
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken("PartnerUser", userId: CouponsSeed.UserId, username: "tester");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task RedeemCoupon_Fixed_ValidCode_Returns200WithDiscount()
    {
        var response = await Client().PostAsync("/api/coupons/redeem", Json(new { code = "MULTI20" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Equal(20, body.GetProperty("discountPercent").GetInt32());
        Assert.Equal("Coupon applied", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RedeemCoupon_Fixed_SingleUseCoupon_SecondCallReturns409()
    {
        var client = Client();
        var first = await client.PostAsync("/api/coupons/redeem", Json(new { code = "ONCE10" }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync("/api/coupons/redeem", Json(new { code = "ONCE10" }));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task RedeemCoupon_Fixed_AlreadyExhausted_Returns409()
    {
        var response = await Client().PostAsync("/api/coupons/redeem", Json(new { code = "SPENT" }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RedeemCoupon_Fixed_UnknownCode_Returns404()
    {
        var response = await Client().PostAsync("/api/coupons/redeem", Json(new { code = "GHOST" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RedeemCoupon_Fixed_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/coupons/redeem", Json(new { code = "MULTI20" }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
