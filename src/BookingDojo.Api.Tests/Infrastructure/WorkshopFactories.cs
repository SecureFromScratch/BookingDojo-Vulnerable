using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BookingDojo.Api.Tests.Infrastructure;

public class VulnerableWorkshopFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<WorkshopOptions>(o => o.BookingIdorAccess = "Vulnerable"));
    }
}

// SQLi fixed (so LINQ runs against InMemory), resource consumption vulnerable
// Used to test the client-controlled pageSize behaviour without SQL injection noise.
public class ResourceConsumptionVulnerableFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<WorkshopOptions>(o =>
            {
                o.BookingSearchSqlInjection = "Fixed";
                o.BookingSearchResourceConsumption = "Vulnerable";
            }));
    }
}

public class VulnerableCouponCartFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<WorkshopOptions>(o => o.CouponRedemptionRaceCondition = "Vulnerable"));
    }
}

public class FixedCouponCartFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<WorkshopOptions>(o => o.CouponRedemptionRaceCondition = "Fixed"));
    }
}

public class FixedWorkshopFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<WorkshopOptions>(o =>
            {
                o.BookingIdorAccess = "Fixed";
                o.BookingSearchSqlInjection = "Fixed";
                o.CardPiiStorage = "Fixed";
                o.MfaBruteForceProtection = "Fixed";
                o.LogInjection = "Fixed";
                o.AuditLogDeletion = "Fixed";
                o.LoginSqlInjection = "Fixed";
                o.BookingSearchResourceConsumption = "Fixed";
                o.CouponRedemptionRaceCondition = "Fixed";
                o.PasswordResetRaceCondition = "Fixed";
                o.WebhookSsrf = "Fixed";
                o.ExceptionDetailDisclosure = "Fixed";
            }));
    }
}
