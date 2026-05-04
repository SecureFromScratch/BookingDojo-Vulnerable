using System.IdentityModel.Tokens.Jwt;
using System.Text;
using BookingDojo.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BookingDojo.Api.Tests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string JwtSecret = "TestSecretForIntegrationTests1234567890!!";
    public const string JwtIssuer = "BookingDojo";
    public const string JwtAudience = "BookingDojo";

    private readonly string _dbName = $"TestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BookingDojoDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<BookingDojoDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
                options.TokenValidationParameters.IssuerSigningKey = key;
                options.TokenValidationParameters.ValidIssuer = JwtIssuer;
                options.TokenValidationParameters.ValidAudience = JwtAudience;
                options.TokenValidationParameters.RoleClaimType = "role";
                options.TokenValidationParameters.NameClaimType = JwtRegisteredClaimNames.Name;
            });
        });
    }

    public void SeedDatabase(Action<BookingDojoDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDojoDbContext>();
        seed(db);
        db.SaveChanges();
    }
}
