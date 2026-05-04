using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookingDojo.Api.Models;
using BookingDojo.Api.Tests.Infrastructure;

namespace BookingDojo.Api.Tests;

file static class CartSeed
{
    public static readonly Guid UserId  = Guid.NewGuid();
    public static readonly Guid HotelId = Guid.NewGuid();
    public static readonly Guid PartnerId = Guid.NewGuid();

    public static void Apply(CustomWebApplicationFactory factory)
    {
        factory.SeedDatabase(db =>
        {
            if (!db.Hotels.Any())
            {
                db.Partners.Add(new Partner { Id = PartnerId, Name = "Test Partner" });
                db.Hotels.Add(new Hotel
                {
                    Id = HotelId, PartnerId = PartnerId,
                    Name = "Test Hotel", Location = "Test City",
                    Description = "desc", IsActive = true,
                });
                db.Coupons.AddRange(
                    new Coupon { Code = "ONCE10",  DiscountPercent = 10, MaxUses = 1, UsesCount = 0 },
                    new Coupon { Code = "MULTI20", DiscountPercent = 20, MaxUses = 5, UsesCount = 0 },
                    new Coupon { Code = "SPENT",   DiscountPercent = 15, MaxUses = 1, UsesCount = 1 }
                );
                db.SaveChanges();
            }
        });
    }
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

public abstract class CartTestBase
{
    protected readonly CustomWebApplicationFactory _factory;

    protected CartTestBase(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        CartSeed.Apply(factory);
    }

    protected HttpClient Client(Guid? userId = null)
    {
        var client = _factory.CreateClient();
        var token = TestTokenHelper.GenerateToken("PartnerUser", userId: userId ?? CartSeed.UserId, username: "cartuser");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected StringContent Json(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    protected async Task<JsonElement> Body(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync());

    protected Task<HttpResponseMessage> AddItem(HttpClient client) =>
        client.PostAsync("/api/cart/items", Json(new
        {
            hotelId = CartSeed.HotelId,
            checkIn = DateTime.UtcNow.AddDays(1),
            checkOut = DateTime.UtcNow.AddDays(3),
            cardNumber = "4111111111111234",
            specialRequests = ""
        }));
}

// ─── Cart CRUD ────────────────────────────────────────────────────────────────

public class CartCrudTests : CartTestBase, IClassFixture<CustomWebApplicationFactory>
{
    public CartCrudTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetCart_CreatesEmptyCartOnFirstCall()
    {
        var r = await Client().GetAsync("/api/cart");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Body(r);
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task AddItem_AppearsInCart()
    {
        var client = Client(Guid.NewGuid());
        await AddItem(client);

        var r = await client.GetAsync("/api/cart");
        var body = await Body(r);
        Assert.Equal(1, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task RemoveItem_DisappearsFromCart()
    {
        var client = Client(Guid.NewGuid());
        var addResp = await AddItem(client);
        var itemId = (await Body(addResp)).GetProperty("id").GetInt32();

        await client.DeleteAsync($"/api/cart/items/{itemId}");

        var r = await client.GetAsync("/api/cart");
        Assert.Equal(0, (await Body(r)).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Checkout_EmptyCart_Returns400()
    {
        var r = await Client(Guid.NewGuid()).PostAsync("/api/cart/checkout", Json(new { couponCode = (string?)null }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}

// ─── Vulnerable coupon (race condition exploitable) ───────────────────────────

public class CartCheckoutVulnerableTests : CartTestBase, IClassFixture<VulnerableCouponCartFactory>
{
    public CartCheckoutVulnerableTests(VulnerableCouponCartFactory factory) : base(factory) { }

    [Fact]
    public async Task Checkout_WithoutCoupon_CreatesBookings()
    {
        var client = Client(Guid.NewGuid());
        await AddItem(client);

        var r = await client.PostAsync("/api/cart/checkout", Json(new { couponCode = (string?)null }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await Body(r);
        Assert.Equal(1, body.GetProperty("bookings").GetArrayLength());
        Assert.True(body.GetProperty("discountPercent").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Checkout_ValidCoupon_AppliesDiscount()
    {
        var client = Client(Guid.NewGuid());
        await AddItem(client);

        var r = await client.PostAsync("/api/cart/checkout", Json(new { couponCode = "MULTI20" }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await Body(r);
        Assert.Equal(20, body.GetProperty("discountPercent").GetInt32());
    }

    [Fact]
    public async Task Checkout_ExhaustedCoupon_Returns409()
    {
        var userA = Client(Guid.NewGuid());
        await AddItem(userA);
        var r = await userA.PostAsync("/api/cart/checkout", Json(new { couponCode = "SPENT" }));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Checkout_InvalidCoupon_Returns404()
    {
        var client = Client(Guid.NewGuid());
        await AddItem(client);
        var r = await client.PostAsync("/api/cart/checkout", Json(new { couponCode = "NOPE" }));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Checkout_Vulnerable_ConcurrentRequestsBothSucceed()
    {
        // Two users simultaneously checkout with a MaxUses=1 coupon.
        // The 500ms artificial delay means both pass the "UsesCount < MaxUses" check
        // before either writes — the race condition lets both succeed.
        var clientA = Client(Guid.NewGuid());
        var clientB = Client(Guid.NewGuid());
        await AddItem(clientA);
        await AddItem(clientB);

        var t1 = clientA.PostAsync("/api/cart/checkout", Json(new { couponCode = "ONCE10" }));
        var t2 = clientB.PostAsync("/api/cart/checkout", Json(new { couponCode = "ONCE10" }));
        var results = await Task.WhenAll(t1, t2);

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}

// ─── Fixed coupon (atomic UPDATE) ────────────────────────────────────────────

public class CartCheckoutFixedTests : CartTestBase, IClassFixture<FixedCouponCartFactory>
{
    public CartCheckoutFixedTests(FixedCouponCartFactory factory) : base(factory) { }

    [Fact]
    public async Task Checkout_Fixed_FirstRequestSucceeds_SecondReturns409()
    {
        // Sequential: first user redeems the MaxUses=1 coupon, second user is rejected.
        // (Concurrent atomicity is enforced by the SQL UPDATE — verifiable with a real DB,
        //  not InMemory which lacks SQL-level locking.)
        var clientA = Client(Guid.NewGuid());
        var clientB = Client(Guid.NewGuid());
        await AddItem(clientA);
        await AddItem(clientB);

        var r1 = await clientA.PostAsync("/api/cart/checkout", Json(new { couponCode = "ONCE10" }));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await clientB.PostAsync("/api/cart/checkout", Json(new { couponCode = "ONCE10" }));
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
    }
}
