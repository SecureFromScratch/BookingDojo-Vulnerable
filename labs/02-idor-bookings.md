# Exercise 02 — IDOR: Booking ID Enumeration

**Difficulty:** Beginner  
**Category:** Broken Access Control / IDOR  
**OWASP Top 10:** A01:2021 — Broken Access Control

---

## Scenario

You have just made a hotel booking on BookingDojo. Your confirmation shows **Booking #3**.

You notice the ID is a small sequential integer. You wonder: what happens if you request **Booking #1** or **Booking #2**?

---

## Background

**IDOR (Insecure Direct Object Reference)** occurs when an application exposes a direct reference to an internal object — such as a database row — and does not verify that the requesting user is authorised to access it.

Authentication answers *"who are you?"*. Authorisation answers *"are you allowed to do this?"*.  
Being logged in does not automatically mean you can access every resource. Each object access needs its own ownership check.

Sequential integer IDs make IDOR trivially exploitable: an attacker who sees their own ID can enumerate adjacent IDs to find other users' data.

---

## Step 1 — Observe the attack surface

The database is pre-seeded with two bookings owned by different users:

| Booking ID | Owner   | Card last 4 |
|-----------|---------|-------------|
| 1         | admin   | 1234        |
| 2         | partner | 4242        |

Open **two browser windows** (use a private/incognito window for the second).

**Window 1 — log in as `partner / Partner1234!`** at `http://localhost:5173`:
1. Navigate to **Bookings**.
2. Your booking is **#2**, card ending in `4242`.

**Window 2 — log in as `admin / Admin1234!`:**
1. Navigate to **Bookings**.
2. Your booking is **#1**, card ending in `1234`.
3. Create a new booking — it gets **#3**, the next sequential ID.

The IDs are consecutive integers. Knowing your own ID immediately reveals what IDs belong to other users.

---

## Step 2 — Exploit the IDOR via the UI

While logged in as `partner`, type this directly into the browser address bar:

```
http://localhost:5173/bookings/1
```

The booking detail page loads — showing `admin`'s booking, including the card ending in `1234`. You are authenticated as `partner` but the server returned a resource that belongs to `admin`. No tools required.

---

## Step 3 — Exploit the IDOR via curl

```bash
# Log in as admin — session cookie saved to cookies.txt
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin1234!"}' | jq .

# Fetch booking #2 — belongs to partner, not admin
curl -s -b cookies.txt http://localhost:5001/bff/bookings/2 | jq .
```

Expected response:

```json
{
  "id": 2,
  "username": "partner",
  "cardLastFour": "4242",
  ...
}
```

You are authenticated as `admin` but are reading `partner`'s payment data.

---

## Step 4 — Understand why it works

The vulnerable endpoint has no ownership check — any authenticated caller gets any booking:

```csharp
// VULNERABLE — no ownership check
[HttpGet("{id:int}")]
[Authorize]
public async Task<IActionResult> GetBookingById(int id)
{
    var booking = await _db.Bookings.Include(b => b.Hotel)
                                    .FirstOrDefaultAsync(b => b.Id == id);
    if (booking == null) return NotFound();
    return Ok(ToDto(booking, booking.Hotel.Name));
}
```

The server looks up the booking by ID and returns it to **any authenticated user** without checking whether that booking belongs to them.

---

## Step 5 — Apply the fix

The fix has two parts: an `Authorization/` folder with the requirement, handler, and extension method; and a one-line wire-up in `Program.cs`.

### 5.1 — Create the `Authorization/` folder

Create `src/BookingDojo.Api/Authorization/` and add two files.

**`Authorization/ResourceOwnerRequirement.cs`**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BookingDojo.Api.Authorization;

public class ResourceOwnerRequirement : IAuthorizationRequirement { }

public class ResourceOwnerAuthorizationHandler : AuthorizationHandler<ResourceOwnerRequirement>
{
    private readonly BookingDojoDbContext _db;

    public ResourceOwnerAuthorizationHandler(BookingDojoDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement)
    {
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

The handler reads the booking ID from the route, loads the booking from the database, and checks that the caller's JWT `sub` matches `booking.UserId`. If it matches, `context.Succeed(requirement)` is called. If it doesn't — or if the booking doesn't exist — the handler returns without calling `Succeed`, and the framework denies the request.

> **Note on `context.Resource`:** ASP.NET Core has two authorization checkpoints. At the `AuthorizationMiddleware` level the resource is the `HttpContext`; at the `AuthorizeFilter` level (inside MVC) it is an `AuthorizationFilterContext`. The switch handles both so the same handler works at runtime and in integration tests.

**`Authorization/AuthorizationExtensions.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;

namespace BookingDojo.Api.Authorization;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy("ResourceOwner", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ResourceOwnerRequirement());
            });
        });

        return services;
    }
}
```

All policy registration and handler wiring lives here. Adding a new policy in the future means editing one file.

> **Critical:** if you forget `AddScoped<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>()`, ASP.NET Core will never call the handler and authorization will always fail silently.

### 5.2 — Update `BookingsController`

Add `[Authorize(Policy = "ResourceOwner")]` to `GetBookingById`. No other changes to the controller are needed:

```csharp
[HttpGet("{id:int}")]
[Authorize(Policy = "ResourceOwner")]
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

The `[Authorize(Policy = "ResourceOwner")]` attribute causes ASP.NET Core to invoke `ResourceOwnerAuthorizationHandler` **before** the method body runs. The handler loads the booking, checks ownership, and either allows or denies the request. The controller body only runs if the handler succeeded.

### 5.3 — Wire up in `Program.cs`

Replace the existing `AddAuthorization()` call with:

```csharp
using BookingDojo.Api.Authorization;

builder.Services.AddAuthorizationPolicies();
```

### How the authorization flow works

```
GET /api/bookings/2
        │
        ▼
[Authorize(Policy = "ResourceOwner")]
        │
        ├─ unauthenticated → 401
        │
        ▼
ResourceOwnerAuthorizationHandler
        │
        ├─ reads id = 2 from route
        ├─ loads Booking #2 from DB
        ├─ JWT sub == booking.UserId?  → Succeed → method body runs → 200
        └─ no match (or not found)    → (nothing called) → 403
```

Re-run the curl command from Step 3. **Expected result:** `403 Forbidden`.

---

## Step 6 — Verify

```bash
curl -s -b cookies.txt http://localhost:5001/bff/bookings/2 | jq .
```

Expected: `403 Forbidden`.

Now verify the owner can still access their own booking:

```bash
curl -s -c cookies_p.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

curl -s -b cookies_p.txt http://localhost:5001/bff/bookings/2 | jq .
```

Expected: `200 OK` with partner's booking data.

---

## Step 7 — Discussion: what makes a good fix?

| Approach | Secure? | Notes |
|----------|---------|-------|
| Switch to GUIDs | ✗ | Obscurity is not access control. GUIDs appear in URLs, logs, and API responses — they leak. |
| Inline ownership check in controller body | ✓ | Correct, but mixes access control logic with business logic. Easy to forget when adding new endpoints. |
| Policy-based ownership check (what we did) | ✓ | Correct. Ownership logic lives in one handler; any endpoint that uses the same policy gets the same protection automatically. |
| Role-based admin override | ✓ (optional) | Admins may need to view any booking for support reasons — see Lab 02b. |

---

## Key Takeaways

- **Authentication ≠ Authorisation.** Being logged in does not grant access to every object.
- **The list endpoint is not enough.** Even if `GET /bookings` returns only your own bookings, a missing check on `GET /bookings/{id}` still exposes all data.
- **Sequential integer IDs are a red flag.** Prefer UUIDs as references — and still add ownership checks.
- The handler reads the booking ID from the route, loads it from the database, and checks ownership — all before the controller body runs.
- IDOR is OWASP A01 for a reason: it is trivial to introduce and easy to miss in code review.

---

## Further Reading

- [OWASP IDOR Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Insecure_Direct_Object_Reference_Prevention_Cheat_Sheet.html)
- [OWASP A01:2021 — Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
