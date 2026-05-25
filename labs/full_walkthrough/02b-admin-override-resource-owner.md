# Bonus Lab 02b — Admin Override: Resource-Based Authorization

**Difficulty:** Intermediate  
**Category:** Broken Access Control / Authorization Design  
**OWASP Top 10:** A01:2021 — Broken Access Control

---

## Scenario

In Lab 02 you fixed IDOR by adding a `ResourceOwner` policy to `GET /api/bookings/{id}`. The ownership check works — but now the support team reports a problem: when an admin tries to look up a customer's booking to resolve a dispute, they receive **403 Forbidden**.

The task: extend the authorization so that **the resource owner OR an admin** can access any booking, while keeping the deny-by-default for everyone else.

---

## Background

### Where Lab 02 left off

```
src/BookingDojo.Api/
├── Authorization/
│   ├── ResourceOwnerRequirement.cs    ← requirement + handler
│   └── AuthorizationExtensions.cs     ← AddAuthorizationPolicies() extension method
└── Controllers/
    └── BookingsController.cs          ← GetBookingById uses [Authorize(Policy = "ResourceOwner")]
```

`ResourceOwnerAuthorizationHandler` only has one path to success — it checks `booking.UserId == JWT sub`. Admin is just another role; it conveys no special privilege here.

---

## Step 1 — Reproduce the problem

With the Lab 02 fix in place, log in as `admin / Admin1234!` and try to fetch a booking owned by `partner`:

```bash
# Log in as admin
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin1234!"}' | jq .

# Try to fetch booking #2 — owned by partner
curl -s -b cookies.txt http://localhost:5001/bff/bookings/2 | jq .
```

Expected result: `403 Forbidden`.

---

## Step 2 — Understand why

`ResourceOwnerAuthorizationHandler` only has one path to success:

```csharp
var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
if (sub != null && Guid.Parse(sub) == booking.UserId)
    context.Succeed(requirement);
```

There is no admin path. Because no handler calls `context.Succeed()` for an admin user, the framework denies the request.

---

## Step 3 — Apply the fix

### 3.1 — Add `BookingOwnerOrAdminHandler.cs` to the `Authorization/` folder

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BookingDojo.Api.Authorization;

public class OwnerOrAdminRequirement : IAuthorizationRequirement { }

public class BookingOwnerOrAdminHandler : AuthorizationHandler<OwnerOrAdminRequirement>
{
    private readonly BookingDojoDbContext _db;

    public BookingOwnerOrAdminHandler(BookingDojoDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerOrAdminRequirement requirement)
    {
        if (context.User.FindFirstValue("role") == "AdminUser")
        {
            context.Succeed(requirement);
            return;
        }

        var routeValues = context.Resource switch
        {
            AuthorizationFilterContext fc => fc.RouteData.Values,
            HttpContext hc => hc.GetRouteData()?.Values,
            _ => null
        };

        if (routeValues == null || !int.TryParse(routeValues["id"]?.ToString(), out var id))
            return;

        var booking = await _db.Bookings.FindAsync(id);
        if (booking == null)
            return;

        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (sub != null && Guid.Parse(sub) == booking.UserId)
            context.Succeed(requirement);
    }
}
```

The admin check runs first — admins are allowed through without loading any data. For non-admins, the handler loads the booking from the route ID and checks ownership.

### 3.2 — Register the new policy in `AuthorizationExtensions.cs`

```csharp
using Microsoft.AspNetCore.Authorization;

namespace BookingDojo.Api.Authorization;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, BookingOwnerOrAdminHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy("ResourceOwner", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ResourceOwnerRequirement());
            });

            options.AddPolicy("BookingOwnerOrAdmin", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new OwnerOrAdminRequirement());
            });
        });

        return services;
    }
}
```

> **Critical:** if you forget `AddScoped<IAuthorizationHandler, BookingOwnerOrAdminHandler>()`, ASP.NET Core will never call the handler and authorization will always fail silently.

### 3.3 — Update the controller

Change the policy name on `GetBookingById` from `"ResourceOwner"` to `"BookingOwnerOrAdmin"`:

```csharp
[HttpGet("{id:int}")]
[Authorize(Policy = "BookingOwnerOrAdmin")]
public async Task<IActionResult> GetBookingById(int id)
{
    var booking = await _db.Bookings
        .Include(b => b.Hotel)
        .FirstOrDefaultAsync(b => b.Id == id);

    if (booking == null)
        return NotFound();

    return Ok(ToDto(booking, booking.Hotel.Name));
}
```

That is the only change to the controller.

---

## Step 4 — Verify

Repeat the curl from Step 1. The admin should now receive the booking:

```bash
curl -s -b cookies.txt http://localhost:5001/bff/bookings/2 | jq .
```

Expected:

```json
{
  "id": 2,
  "username": "partner",
  "cardLastFour": "4242",
  ...
}
```

Now verify the ownership check still blocks a regular user from accessing someone else's booking:

```bash
curl -s -c cookies_p.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

# Try to fetch booking #1 — owned by admin, not partner
curl -s -o /dev/null -w "%{http_code}" \
  -b cookies_p.txt http://localhost:5001/bff/bookings/1
```

Expected: `403`

---

## How the authorization flow works now

```
GET /api/bookings/2
        │
        ▼
[Authorize(Policy = "BookingOwnerOrAdmin")]
        │
        ├─ unauthenticated → 401
        │
        ▼
BookingOwnerOrAdminHandler
        │
        ├─ role == "AdminUser"?       → Succeed → method body runs → 200
        │
        ├─ reads id = 2 from route
        ├─ loads Booking #2 from DB
        ├─ JWT sub == booking.UserId? → Succeed → method body runs → 200
        └─ neither                   → (nothing called) → 403
```

---

## Step 5 — Discussion

### Why admin access must be explicit

The denial-by-default is the secure state. Allowing admin access is an intentional privilege grant — expressed as a concrete `if` statement that any reviewer can see and audit. An absent check is not a safe alternative; it is an invisible hole.

### Why the admin check comes first

The handler checks the admin role before touching the database. An admin request succeeds immediately without a DB round-trip. Non-admin requests pay the cost of loading the booking only when necessary.

### Where the check lives

The admin check is in the handler, not in the controller. The controller stays free of access-control logic. When the same policy applies to multiple endpoints, you change the rule in one place.

### Audit logging

In production, admin access to another user's data should be logged — recording who accessed what and when. That logging belongs in the handler, not scattered across controller actions.

---

## Key Takeaways

- Admin override must be an **explicit grant**, not an absent check.
- The handler loads the resource itself — the controller needs no special base class and no extra injected service.
- All policy and handler registration lives in `AuthorizationExtensions.cs` — one file to update when rules change.
- Keep access-control logic in handlers; keep business logic in controllers.

---

## Further Reading

- [Microsoft Docs: Policy-based authorization in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [OWASP A01:2021 — Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
