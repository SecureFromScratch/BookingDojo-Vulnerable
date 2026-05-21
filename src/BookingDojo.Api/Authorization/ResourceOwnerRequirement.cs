using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using Microsoft.AspNetCore.Authorization;

namespace BookingDojo.Api.Authorization;

public class ResourceOwnerRequirement : IAuthorizationRequirement { }

public class ResourceOwnerAuthorizationHandler : AuthorizationHandler<ResourceOwnerRequirement>
{
    private readonly IHttpContextAccessor _http;

    public ResourceOwnerAuthorizationHandler(IHttpContextAccessor http)
    {
        _http = http;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement)
    {
        // VULNERABLE PATH — ownership check is absent.
        // Any authenticated user can access any resource regardless of ownership.
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
