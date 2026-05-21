using System.Text;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using BookingDojo.Api.Authorization;
using BookingDojo.Api.Data;
using BookingDojo.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);

var localStackUrl = builder.Configuration["AWS:ServiceURL"] ?? "http://localhost:4566";
builder.Configuration.AddSystemsManager(source =>
{
    source.Path = "/bookingdojo";
    source.AwsOptions = new AWSOptions
    {
        Credentials = new BasicAWSCredentials("test", "test"),
        Region = Amazon.RegionEndpoint.USEast1,
        DefaultClientConfig = { ServiceURL = localStackUrl }
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
builder.Services.AddDbContext<BookingDojoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BookingDojo")));

// Services
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<DataSeeder>();

// HttpClient for outbound webhook calls (Lab 08 — SSRF)
builder.Services.AddHttpClient("webhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// HttpClient for profile avatar URL fetch (Lab 13 — SSRF)
builder.Services.AddHttpClient("profile", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// JWT Authentication
var jwtSecret = builder.Configuration["BookingDojo:Jwt:Secret"]!;
var jwtIssuer = builder.Configuration["BookingDojo:Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["BookingDojo:Jwt:Audience"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            RoleClaimType = "role",
            NameClaimType = JwtRegisteredClaimNames.Name
        };
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorization(options =>
    options.AddPolicy("ResourceOwner", policy =>
        policy.Requirements.Add(new ResourceOwnerRequirement())));
builder.Services.AddSingleton<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>();

// CORS — allow BFF and Vite dev server
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// --seed-and-exit: create schema + seed data, then exit
if (args.Contains("--seed-and-exit"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
    Console.WriteLine("Seeding complete. Exiting.");
    return;
}

// Normal startup: fix schema if stale, then ensure all tables exist, then auto-seed if empty.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookingDojoDbContext>();

    // Check every table we own — any missing table triggers a full drop+recreate.
    // EnsureCreatedAsync does nothing when the database already exists, so new tables
    // added in later labs would never be created without this guard.
    // Use FirstOrDefaultAsync (not AnyAsync) so EF Core projects all mapped columns —
    // a missing column throws here, triggering a full schema rebuild.
    var schemaStale = false;
    try
    {
        await db.Bookings.FirstOrDefaultAsync();
        await db.Coupons.FirstOrDefaultAsync();
        await db.PasswordResetTokens.FirstOrDefaultAsync();
        await db.Carts.FirstOrDefaultAsync();
        await db.CartItems.FirstOrDefaultAsync();
        await db.MfaChallenges.FirstOrDefaultAsync();
        await db.Users.Select(u => u.AvatarUrl).FirstOrDefaultAsync();
        await db.RefreshTokens.FirstOrDefaultAsync();
        await db.Webhooks.FirstOrDefaultAsync();
    }
    catch { schemaStale = true; }

    if (schemaStale)
    {
        var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        log.LogWarning("[BookingDojo] Schema out of date — dropping schema and recreating.");
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS bookingdojo CASCADE");
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA bookingdojo");
    }

    await db.Database.EnsureCreatedAsync();

    // Auto-seed if the database is empty (e.g. after a schema recreate).
    // DataSeeder.SeedAsync() is idempotent — it skips if users already exist.
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// VULNERABLE PATH: Exception Detail Disclosure — returns full exception details
app.Use(async (ctx, next) =>
{
    try { await next(ctx); }
    catch (Exception ex)
    {
        ctx.Response.StatusCode  = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            error      = ex.Message,
            type       = ex.GetType().FullName,
            source     = ex.Source,
            stackTrace = ex.StackTrace
        });
    }
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
