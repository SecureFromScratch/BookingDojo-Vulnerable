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

Try other IDs: `/bookings/3`, `/bookings/4`, etc. Each sequential integer is another user's data.

---

## Step 3 — Exploit the IDOR via curl

As `admin`, use curl to fetch another user's booking by ID:

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

## Step 3 — Understand why it works

Open `src/BookingDojo.Api/Authorization/ResourceOwnerRequirement.cs` and find the vulnerable path in the handler:

```csharp
if (_workshop.Value.BookingIdorAccess == "Vulnerable")
{
    // ownership check skipped — every authenticated caller succeeds
    context.Succeed(requirement);
    return;
}
```

> **Workshop note:** the `[Authorize(Policy = "ResourceOwner")]` attribute is still present on the action in vulnerable mode because the workshop needs a single codebase with a toggle. In a real vulnerable application there would be no attribute at all — just a plain action with no ownership check:
>
> ```csharp
> [HttpGet("{id:int}")]
> public async Task<IActionResult> GetBookingById(int id)
> {
>     var booking = await _db.Bookings.Include(b => b.Hotel)
>                                     .FirstOrDefaultAsync(b => b.Id == id);
>     if (booking == null) return NotFound();
>     return Ok(ToDto(booking, booking.Hotel.Name));
> }
> ```

The server looks up the booking by ID and returns it to **any authenticated user** without checking whether that booking belongs to them. Authentication passed — authorisation was never applied.

---

## The fix

The fixed code path in `BookingsController.cs`:

```csharp
[HttpGet("{id:int}")]
[Authorize(Policy = "ResourceOwner")]
[OwnedResource(typeof(Booking))]
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

The controller contains no ownership logic at all. The `[Authorize(Policy = "ResourceOwner")]` attribute enforces access control before the action body runs. `[OwnedResource(typeof(Booking))]` tells the handler which entity type to load from the route.

### How the authorization flow works at runtime

```
GET /api/bookings/2
        │
        ▼
[Authorize(Policy = "ResourceOwner")]  ← ASP.NET Core sees this attribute
        │  looks up policy, finds it requires...
        ▼
ResourceOwnerRequirement               ← a token: "ownership must be proven"
        │  framework finds the handler registered for this requirement...
        ▼
ResourceOwnerAuthorizationHandler      ← the actual logic:
        │  reads id=2 from route
        │  loads Booking from DB
        │  compares booking.UserId to JWT sub claim
        │
        ├─ match    → context.Succeed() → request continues → action runs
        └─ no match → nothing called   → framework returns 403
```

`ResourceOwnerRequirement` is just a label — no logic. `ResourceOwnerAuthorizationHandler` is the logic. The framework matches them: when a policy requires `ResourceOwnerRequirement`, it finds any handler registered for that type and calls it.

The `"ResourceOwner"` policy is backed by `ResourceOwnerAuthorizationHandler` in `Authorization/ResourceOwnerRequirement.cs`:

```csharp
public class ResourceOwnerAuthorizationHandler : AuthorizationHandler<ResourceOwnerRequirement>
{
    private readonly IHttpContextAccessor _http;
    private readonly IOptions<WorkshopOptions> _workshop;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement)
    {
        if (_workshop.Value.BookingIdorAccess == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH — ownership check is absent
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
```

The handler resolves the resource itself using `IHttpContextAccessor` — reading the route `id` and loading the entity via `DbContext.FindAsync`. Because `Booking` implements `IOwnedResource` (a one-property interface exposing `UserId`), the handler is not coupled to `Booking` specifically: any model that implements the interface and is decorated with `[OwnedResource]` gets the same protection.

The policy and handler are registered once in `Program.cs`:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorization(options =>
    options.AddPolicy("ResourceOwner", policy =>
        policy.Requirements.Add(new ResourceOwnerRequirement())));
builder.Services.AddSingleton<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>();
```

---

## Step 5 — Discussion: what makes a good fix?

| Approach | Secure? | Notes |
|----------|---------|-------|
| Switch to GUIDs | ✗ | Obscurity is not access control. GUIDs appear in URLs, logs, and API responses — they leak. |
| Inline ownership check | ✓ | Correct, but mixes access control logic with business logic. Easy to forget when adding new endpoints. |
| Policy-based ownership check (what we did) | ✓ | Correct. Ownership logic lives in one place; any resource that implements `IOwnedResource` gets the same protection automatically. |
| Role-based admin override | ✓ (optional) | Admins may need to view any booking for support reasons — but that must be an explicit check, not an absent one. |
| Return 404 instead of 403 | debatable | Hides the resource's existence from attackers. But 403 is more honest during development and easier to debug. |

---

## Key Takeaways

- **Authentication ≠ Authorisation.** Being logged in does not grant access to every object.
- **The list endpoint is not enough.** Even if `GET /bookings` returns only your own bookings, a missing check on `GET /bookings/{id}` still exposes all data.
- **Sequential integer IDs are a red flag.** Prefer UUIDs as references — and still add ownership checks.
- IDOR is OWASP A01 for a reason: it is trivial to introduce and easy to miss in code review.

---

## Further Reading

- [OWASP IDOR Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Insecure_Direct_Object_Reference_Prevention_Cheat_Sheet.html)
- [OWASP A01:2021 — Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
