using System.Text;
using BookingDojo.Api.Data;
using BookingDojo.Api.Services;
using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);

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

// Workshop options
builder.Services.Configure<WorkshopOptions>(
    builder.Configuration.GetSection(WorkshopOptions.Section));

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

builder.Services.AddAuthorization();

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

// Normal startup: fix schema if stale, then ensure all tables exist.
// Data seeding is separate (--seed-and-exit) so tests can seed their own fixtures.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookingDojoDbContext>();

    // Check every table we own — any missing table triggers a full drop+recreate.
    // EnsureCreatedAsync does nothing when the database already exists, so new tables
    // added in later labs would never be created without this guard.
    var schemaStale = false;
    try
    {
        await db.Bookings.AnyAsync();
        await db.Coupons.AnyAsync();
        await db.PasswordResetTokens.AnyAsync();
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
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Lab 09 — Exception Detail Disclosure: reads WorkshopOptions per-request so test overrides work
app.Use(async (ctx, next) =>
{
    try
    {
        await next(ctx);
    }
    catch (Exception ex)
    {
        var opts = ctx.RequestServices
            .GetRequiredService<IOptions<WorkshopOptions>>().Value;

        ctx.Response.StatusCode  = 500;
        ctx.Response.ContentType = "application/json";

        if (opts.ExceptionDetailDisclosure == "Vulnerable")
        {
            await ctx.Response.WriteAsJsonAsync(new
            {
                error      = ex.Message,
                type       = ex.GetType().FullName,
                source     = ex.Source,
                stackTrace = ex.StackTrace
            });
        }
        else
        {
            await ctx.Response.WriteAsJsonAsync(new
            {
                message = "An internal error occurred."
            });
        }
    }
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
