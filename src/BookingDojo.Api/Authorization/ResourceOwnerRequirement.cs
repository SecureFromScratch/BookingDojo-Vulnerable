using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BookingDojo.Api.Authorization;

public class ResourceOwnerRequirement : IAuthorizationRequirement { }

public class ResourceOwnerAuthorizationHandler : AuthorizationHandler<ResourceOwnerRequirement>
{
    private readonly IHttpContextAccessor _http;
    private readonly IOptions<WorkshopOptions> _workshop;

    public ResourceOwnerAuthorizationHandler(IHttpContextAccessor http, IOptions<WorkshopOptions> workshop)
    {
        _http = http;
        _workshop = workshop;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement)
    {
        if (_workshop.Value.BookingIdorAccess == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH — ownership check is absent.
            // NOTE: in a real vulnerable application this attribute would not exist at all.
            // We keep it here so the workshop toggle works, but the equivalent real-world
            // code would be a plain action with no [Authorize(Policy = "ResourceOwner")]:
            //
            //   [HttpGet("{id:int}")]
            //   public async Task<IActionResult> GetBookingById(int id)
            //   {
            //       var booking = await _db.Bookings.Include(b => b.Hotel)
            //                                       .FirstOrDefaultAsync(b => b.Id == id);
            //       if (booking == null) return NotFound();
            //       return Ok(ToDto(booking, booking.Hotel.Name));
            //   }
            context.Succeed(requirement);
            return;
        }

        // WORKSHOP: FIXED PATH
        var httpContext = _http.HttpContext!;

        var meta = httpContext.GetEndpoint()?.Metadata.GetMetadata<OwnedResourceAttribute>();
        if (meta == null) return;

        if (!httpContext.Request.RouteValues.TryGetValue("id", out var idValue)
            || !int.TryParse(idValue?.ToString(), out var id))
            return;

        var db = httpContext.RequestServices.GetRequiredService<BookingDojoDbContext>();
        var resource = await db.FindAsync(meta.ResourceType, id) as IOwnedResource;
        if (resource == null) return;

        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (sub != null && Guid.Parse(sub) == resource.UserId)
            context.Succeed(requirement);
    }
}
