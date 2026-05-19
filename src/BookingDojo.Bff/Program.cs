using Amazon.SimpleSystemsManagement;
using Amazon.Runtime;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

var apiUrl = builder.Configuration["BookingDojo:ApiUrl"] ?? "http://localhost:5000";

builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(apiUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// CORS — allow the Vite dev server
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

// Data Protection — keys stored in LocalStack SSM so they survive restarts and are shared
// across instances. Keys are stored under /bookingdojo/bff/dp-keys in Parameter Store.
var localStackUrl = builder.Configuration["AWS:ServiceURL"] ?? "http://localhost:4566";
builder.Services.AddAWSService<IAmazonSimpleSystemsManagement>(new AWSOptions
{
    Credentials = new BasicAWSCredentials("test", "test"),
    Region = Amazon.RegionEndpoint.USEast1,
    DefaultClientConfig = { ServiceURL = localStackUrl }
});

builder.Services.AddDataProtection()
    .PersistKeysToAWSSystemsManager("/bookingdojo/bff/dp-keys")
    .SetApplicationName("BookingDojo.Bff");

// Cookie authentication — JWT is encrypted inside the cookie, never in plaintext in the browser
builder.Services.AddAuthentication("bff")
    .AddCookie("bff", options =>
    {
        options.Cookie.Name = "bd_token";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
