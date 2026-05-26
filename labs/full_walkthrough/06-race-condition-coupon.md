# Exercise 06 — Race Conditions: Coupon Redemption

**Difficulty:** Intermediate  
**Category:** Race Conditions / TOCTOU  
**OWASP Top 10:** API4:2023 — Unrestricted Resource Consumption / A04:2021 — Insecure Design

---

## Scenario

The coupon redemption endpoint (`POST /api/coupons/redeem`) enforces a per-coupon `MaxUses` limit by reading the current use count, checking it, waiting slightly, then writing the increment back. Because the check and the write are two separate operations with a gap between them, two concurrent requests can both pass the check before either writes — allowing a single-use coupon to be redeemed multiple times.

---

## Background

A **Time of Check / Time of Use (TOCTOU)** race condition occurs when:

1. A value is read and validated (Time of Check)
2. Some time passes during which another actor can change that value
3. The system acts based on the now-stale validation result (Time of Use)

The window between check and write is the **race window**. In a web API, this window is often only milliseconds — but that is enough for concurrent requests from the same user or multiple users to all pass the check simultaneously.

Common real-world examples:
- Coupon / discount code reuse
- Referral bonus double-claim
- Gift card balance bypass
- Double-spend in payment flows
- Inventory over-sell (booking a sold-out seat)

---

## Step 1 — Observe normal behaviour via the UI

The database is seeded with:

| Code     | Discount | Max Uses |
|----------|----------|----------|
| SAVE10   | 10%      | 1        |
| SUMMER20 | 20%      | 3        |

Log in as `partner / Partner1234!` at `http://localhost:5173`. Go to **Hotels**, pick any hotel, and add it to your cart. Then go to **Cart**.

> **Important:** you must have a hotel item in your cart before applying coupons. The coupon section only appears when there is something to buy.

In the **Checkout** section you'll see a coupon input. Enter `SUMMER20` and click **Apply**. The discount appears:

```
SUMMER20 — 20% off
```

Apply it two more times (remove and re-enter the code each time). On the fourth attempt the server returns an error — `"Coupon has already been fully redeemed"` (`409 Conflict`). Three uses are exactly the limit.

Now exploit the race condition with `SAVE10` (limit: 1 use) using the browser console.

**Capture the request — do not proceed to checkout.** Open **DevTools → Network**. In the cart coupon box type `SAVE10` and click **Apply**. In the Network tab, right-click the `redeem` request → **Copy → Copy as Fetch**. You now have something like:

```javascript
fetch("http://localhost:5001/bff/coupons/redeem", {
  "method": "POST",
  "headers": { "content-type": "application/json" },
  "body": "{\"code\":\"SAVE10\"}",
  "credentials": "include"
})
```

> **Do not proceed to checkout.** MFA is required to complete a purchase and will block you. The race condition is on coupon redemption only.

**Reset the coupon** by clicking **Remove** on the applied coupon in the cart. This decrements `SAVE10` back to 0 uses — no DB reset needed.

**Open the browser console** and fire both fetches simultaneously with `Promise.all`, then reload the page to see the result:

```javascript
Promise.all([
  fetch("http://localhost:5001/bff/coupons/redeem", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ code: "SAVE10" }),
    credentials: "include"
  }),
  fetch("http://localhost:5001/bff/coupons/redeem", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ code: "SAVE10" }),
    credentials: "include"
  })
])
.then(rs => Promise.all(rs.map(r => r.json())))
.then(results => { console.log(results); location.reload(); });
```
For Github Codespaces it will look like
```javascript
Promise.all([
  fetch("https://probable-spork-g9qg9gpjrqj29vg4-5173.app.github.dev/bff/coupons/redeem", {
  "headers": {
    "accept": "*/*",
    "accept-language": "en-GB,en-US;q=0.9,en;q=0.8",
    "cache-control": "no-cache",
    "content-type": "application/json",
    "pragma": "no-cache",
    "priority": "u=1, i",
    "sec-ch-ua": "\"Google Chrome\";v=\"147\", \"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"147\"",
    "sec-ch-ua-mobile": "?0",
    "sec-ch-ua-platform": "\"Linux\"",
    "sec-fetch-dest": "empty",
    "sec-fetch-mode": "cors",
    "sec-fetch-site": "same-origin"
  },
  "referrer": "https://probable-spork-g9qg9gpjrqj29vg4-5173.app.github.dev/cart",
  "body": "{\"code\":\"SAVE10\"}",
  "method": "POST",
  "mode": "cors",
  "credentials": "include"
}),
fetch("https://probable-spork-g9qg9gpjrqj29vg4-5173.app.github.dev/bff/coupons/redeem", {
  "headers": {
    "accept": "*/*",
    "accept-language": "en-GB,en-US;q=0.9,en;q=0.8",
    "cache-control": "no-cache",
    "content-type": "application/json",
    "pragma": "no-cache",
    "priority": "u=1, i",
    "sec-ch-ua": "\"Google Chrome\";v=\"147\", \"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"147\"",
    "sec-ch-ua-mobile": "?0",
    "sec-ch-ua-platform": "\"Linux\"",
    "sec-fetch-dest": "empty",
    "sec-fetch-mode": "cors",
    "sec-fetch-site": "same-origin"
  },
  "referrer": "https://probable-spork-g9qg9gpjrqj29vg4-5173.app.github.dev/cart",
  "body": "{\"code\":\"SAVE10\"}",
  "method": "POST",
  "mode": "cors",
  "credentials": "include"
})
])
.then(rs => Promise.all(rs.map(r => r.json())))
.then(results => { console.log(results); location.reload(); });

```


Both requests fire in the same JavaScript tick — they land at the server while the 500 ms delay is still running. After the page reloads, the cart shows:

```
SAVE10 — 10% off × 2 (race condition!)
```

The timing window is 500 ms (artificially widened for the workshop). In production the window is 5–15 ms — still exploitable with `Promise.all` but not with manual clicking.

---

## Step 2 — Understand the vulnerable code

A vulnerable implementation uses check-then-update:

```csharp
// VULNERABLE — TOCTOU (Time of Check / Time of Use)
// Time of Check
var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == request.Code);
if (coupon.UsesCount >= coupon.MaxUses)
    return Conflict(...);

// Race window — 500 ms or more of real I/O latency in production
await Task.Delay(500);

// Time of Use — stale data may have already been overwritten by another request
coupon.UsesCount++;
await _db.SaveChangesAsync();
```

The 500 ms delay is artificial and exists to make the race window exploitable in a workshop setting. In production code, the window is typically 1–10 ms — enough for concurrent HTTP requests to both pass the check.

---

## Step 3 — How it works at runtime

```
POST /bff/coupons/redeem (×2 concurrent, SAVE10 MaxUses=1)
        │
        ├─ Without the fix (TOCTOU)
        │       │
        │  Req 1: reads UsesCount=0 → 0 < 1 → check passes
        │  Req 2: reads UsesCount=0 → 0 < 1 → check passes   ← both pass simultaneously
        │       │
        │  [500ms race window — both requests are asleep here]
        │       │
        │  Req 1: UsesCount++ → SaveChanges → UsesCount=1
        │  Req 2: UsesCount++ → SaveChanges → UsesCount=2     ← MaxUses violated
        │       │
        │       └─► both return 200 OK — coupon redeemed twice
        │
        └─ With the fix: atomic UPDATE in DB
                │
                ▼
           UPDATE Coupons SET UsesCount = UsesCount + 1
           WHERE Code = @code AND UsesCount < MaxUses
                │
                ├─ Req 1 wins: 1 row affected → 200 OK
                └─ Req 2 loses: 0 rows affected → 409 Conflict
```

## Step 4 — Apply the fix

**File:** `src/BookingDojo.Api/Controllers/CouponsController.cs`  
**Method:** `Redeem`

**Remove** the entire TOCTOU block:

```csharp
// VULNERABLE PATH (TOCTOU race condition)
// Time of Check: read the coupon and validate remaining uses.
var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == request.Code);
if (coupon == null)
    return NotFound(new { message = $"Coupon '{request.Code}' not found" });

if (coupon.UsesCount >= coupon.MaxUses)
    return Conflict(new { message = "Coupon has already been fully redeemed" });

// Artificial delay widens the race window
await Task.Delay(500);

// Time of Use: a concurrent request may have already incremented this.
coupon.UsesCount++;
await SetCartCoupon(request.Code, coupon.DiscountPercent);
await _db.SaveChangesAsync();

return Ok(new { discountPercent = coupon.DiscountPercent, message = "Coupon applied" });
```

**Replace with:**

```csharp
var rows = await _db.Database.ExecuteSqlRawAsync(
    "UPDATE bookingdojo.\"Coupons\" " +
    "SET \"UsesCount\" = \"UsesCount\" + 1 " +
    "WHERE \"Code\" = {0} AND \"UsesCount\" < \"MaxUses\"",
    request.Code);

if (rows == 0)
    return Conflict(new { message = "Coupon has already been fully redeemed" });

var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == request.Code);
await SetCartCoupon(request.Code, coupon!.DiscountPercent);

return Ok(new { discountPercent = coupon.DiscountPercent, message = "Coupon applied" });
```

The `UPDATE ... WHERE UsesCount < MaxUses` is a single atomic statement. Both concurrent requests race to execute it, but the database only increments once — whichever request gets there first satisfies the `WHERE` condition; the other sees 0 rows affected and receives 409.

---

## Step 5 — Verify

Click **Remove** on any applied coupon to reset to 0 uses, then run the same `Promise.all` from the browser console:

```javascript
Promise.all([
  fetch("http://localhost:5001/bff/coupons/redeem", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ code: "SAVE10" }),
    credentials: "include"
  }),
  fetch("http://localhost:5001/bff/coupons/redeem", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ code: "SAVE10" }),
    credentials: "include"
  })
])
.then(rs => Promise.all(rs.map(r => r.json())))
.then(results => { console.log(results); location.reload(); });
```

Expected console output: **one `"Coupon applied"`** and **one `"Coupon has already been fully redeemed"`**. After the page reloads the cart shows a single `SAVE10 — 10% off` with no duplication.

---

## Step 6 — Discussion

| Control | Description | Applied here |
|---------|-------------|--------------|
| Atomic UPDATE with WHERE | Check and write in one DB statement | ✓ |
| Optimistic concurrency (row version) | Retry on conflict; reject stale writes | Alternative |
| Pessimistic locking (SELECT FOR UPDATE) | Holds a row lock during check-then-write | Alternative |
| Distributed lock (Redis, DB advisory) | Prevents concurrent execution at app level | Over-engineered for this case |
| Idempotency keys | Client supplies a unique request ID; duplicates rejected | Complementary |

**Why the atomic UPDATE wins here**

- No extra round-trips: single SQL statement, no lock acquisition overhead
- Database enforces the constraint — nothing in application code can accidentally bypass it
- Works under horizontal scaling (multiple API instances share one database)
- Naturally handles the "coupon not found" case by returning 0 rows

**Why optimistic concurrency also works**

EF Core `[Timestamp]` / `RowVersion` causes `SaveChangesAsync` to throw `DbUpdateConcurrencyException` when a row was modified between read and write. Callers catch the exception and return 409. The downside: requires a retry-or-abort decision at the application layer.

**Why `SELECT FOR UPDATE` is usually worse**

Row-level locks held for the duration of the check-then-write block other readers on the same coupon, reducing throughput. The atomic UPDATE is lock-free from the application's perspective.

---

## Key Takeaways

- **TOCTOU is not a theoretical risk.** A 500 ms window is artificial; 5 ms is realistic. Concurrent HTTP requests from the same client can exploit even tiny windows.
- **Reads do not reserve state.** Reading `UsesCount = 0` does not mean another request cannot read the same value before your write lands.
- **The fix lives in the database, not the application.** Application-level locks (mutexes, semaphores) only protect a single process; they fail silently under horizontal scaling.
- **Atomic SQL is the simplest correct solution** for bounded-use resources: check and act in one statement.

---

## Further Reading

- [OWASP API4:2023 — Unrestricted Resource Consumption](https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/)
- [OWASP Race Condition](https://owasp.org/www-community/vulnerabilities/Race_Condition)
- [CWE-362: Concurrent Execution Using Shared Resource with Improper Synchronization](https://cwe.mitre.org/data/definitions/362.html)
- [EF Core Optimistic Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [PostgreSQL Locking](https://www.postgresql.org/docs/current/explicit-locking.html)
