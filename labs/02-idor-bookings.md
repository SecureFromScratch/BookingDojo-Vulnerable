# Exercise 02 — IDOR: Booking ID Enumeration

**Difficulty:** Beginner  
**Category:** Broken Access Control / IDOR  
**OWASP Top 10:** A01:2021 — Broken Access Control  
**Config flag:** `BookingDojo:Workshop:BookingIdorAccess`

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

## Setup

Start the stack:

```bash
docker compose up -d
dotnet run --project src/BookingDojo.Api -- --seed-and-exit
dotnet run --project src/BookingDojo.Api &
dotnet run --project src/BookingDojo.Bff &
cd src/bookingdojo-ui && npm run dev &
```

In `src/BookingDojo.Api/appsettings.json`, confirm:

```json
"Workshop": {
  "BookingIdorAccess": "Vulnerable"
}
```

Restart the API if you change the flag.

The database is pre-seeded with two bookings owned by different users:

| Booking ID | Owner   | Card last 4 |
|-----------|---------|-------------|
| 1         | admin   | 1234        |
| 2         | partner | 4242        |

---

## Step 1 — Observe the attack surface

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

## Step 2 — Exploit the IDOR

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

Open `src/BookingDojo.Api/Controllers/BookingsController.cs` and find `GetBookingById`:

```csharp
if (_workshop.Value.BookingIdorAccess == "Vulnerable")
{
    // WORKSHOP: VULNERABLE PATH
    // No ownership check — any authenticated user can fetch any booking by ID.
    // Sequential integer IDs make enumeration trivial.
    return Ok(ToDto(booking, booking.Hotel.Name));
}
```

The server looks up the booking by ID and returns it to **any authenticated user** without checking whether that booking belongs to them. Authentication passed — authorisation was never applied.

---

## Step 4 — Apply the fix

In `appsettings.json`, change the flag:

```json
"BookingIdorAccess": "Fixed"
```

Restart the API and re-run the curl command from Step 2.

**Expected result:** `403 Forbidden`.

The fixed code path:

```csharp
// WORKSHOP: FIXED PATH
var callerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
if (booking.UserId != callerId)
    return Forbid();
```

The server compares the booking's owner against the caller's JWT subject claim. If they don't match, the request is denied before any data is returned.

---

## Step 5 — Discussion: what makes a good fix?

| Approach | Secure? | Notes |
|----------|---------|-------|
| Switch to GUIDs | ✗ | Obscurity is not access control. GUIDs appear in URLs, logs, and API responses — they leak. |
| Ownership check (what we did) | ✓ | Correct. Authorisation enforced at the object level, every time. |
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
