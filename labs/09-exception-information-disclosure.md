# Exercise 09 — Information Disclosure via Exception Details

**Difficulty:** Beginner  
**Category:** Information Disclosure / Security Misconfiguration  
**OWASP Top 10:** A05:2021 — Security Misconfiguration  
**Config flag:** `BookingDojo:Workshop:ExceptionDetailDisclosure`

---

## Scenario

When an unhandled exception occurs in the API, the server returns the full exception details — message, type, source assembly, and stack trace — directly in the HTTP response body. The exception message in this lab contains a fake database connection string with credentials. In a real system this pattern leaks internal architecture, file paths, framework versions, and credentials embedded in error messages.

---

## Background

**Information disclosure** via exception details is a misconfiguration that is common when:

- A development exception page (`UseDeveloperExceptionPage`) is left enabled in production
- A custom exception handler returns the raw `Exception.Message` / `Exception.StackTrace`
- Error logging middleware echoes exception details back to the client
- A catch-all returns `ex.ToString()` as the response body

What leaks:
- **Stack trace** — internal file paths, class names, method names, line numbers; reveals architecture and attack surface
- **Exception type** — `System.Data.SqlException` confirms SQL Server; `NpgsqlException` confirms PostgreSQL; `FileNotFoundException` reveals filesystem paths
- **Exception message** — may contain connection strings, credentials, SQL queries, file paths
- **Source assembly** — reveals framework versions, internal project names

---

## Setup

```bash
docker compose up -d
dotnet run --project src/BookingDojo.Api -- --seed-and-exit
dotnet run --project src/BookingDojo.Api &
dotnet run --project src/BookingDojo.Bff &
cd src/bookingdojo-ui && npm run dev &
```

In `appsettings.json`:

```json
"ExceptionDetailDisclosure": "Vulnerable"
```

Log in as any user:

```bash
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin1234!"}' | jq .
```

---

## Step 1 — Trigger a server error

The debug endpoint throws an exception with sensitive information in its message:

```bash
curl -s -b cookies.txt http://localhost:5001/bff/debug/throw | jq .
```

Example vulnerable response:

```json
{
  "error": "Read replica connection failed: Host=db-replica.bookingdojo.internal;Port=5432;Database=bookingdojo_prod;Username=app_svc;Password=Pr0dS3cr3t-2024!;SSL Mode=Require;Trust Server Certificate=false",
  "type": "System.InvalidOperationException",
  "source": "BookingDojo.Api",
  "stackTrace": "   at BookingDojo.Api.Controllers.DebugController.TriggerError() in /home/app/src/BookingDojo.Api/Controllers/DebugController.cs:line 17\n   at lambda_method3(Closure, Object, Object[])..."
}
```

The response reveals:
- **Credentials** — database password (`Pr0dS3cr3t-2024!`) and username (`app_svc`)
- **Internal hostname** — `db-replica.bookingdojo.internal` (internal DNS name, network topology)
- **Database name** — `bookingdojo_prod` (production database name)
- **Source file path** — `/home/app/src/BookingDojo.Api/Controllers/DebugController.cs`
- **Framework internals** — `lambda_method3(Closure, ...)` reveals ASP.NET Core routing implementation details

---

## Step 2 — Also visible in the UI

In the Audit Logs page (accessible to AdminUser and SupportUser), there is an "Exception Disclosure Test" panel. Click **Trigger Server Error** to see the raw JSON response rendered directly in the browser. Switch the flag and observe how the response changes.

---

## Step 3 — What happens in real code

This endpoint is contrived. In practice, the same disclosure happens when:

- A database connection fails (real connection string in `NpgsqlException.Message`)
- A file operation fails (`FileNotFoundException.Message` contains the full path)
- An HTTP dependency returns unexpected content (URL, auth token in `HttpRequestException`)
- An unhandled `ArgumentNullException` leaks parameter names and calling context

A stack trace alone can reveal:
- All internal class and method names → identifies what the server does
- File system paths → reveals deployment layout, home directory, project structure
- Framework version (`Microsoft.AspNetCore.Mvc 8.0.11`) → allows targeted CVE research

---

## Step 4 — Apply the fix

In `appsettings.json`:

```json
"ExceptionDetailDisclosure": "Fixed"
```

Restart the API and trigger the same error:

```bash
curl -s -b cookies.txt http://localhost:5001/bff/debug/throw | jq .
```

Expected:

```json
{
  "message": "An internal error occurred."
}
```

HTTP status is still `500` — the error is acknowledged, but nothing internal is revealed.

The fixed exception handler:

```csharp
// Fixed: generic response, full details logged server-side only
await ctx.Response.WriteAsJsonAsync(new { message = "An internal error occurred." });
```

The exception is still caught, logged internally (in a real system, to a structured logging service), and a correlation ID could be added so support can look it up. The caller gets nothing exploitable.

---

## Step 5 — Proper error handling architecture

```
Exception thrown
     │
     ▼
Global exception middleware catches it
     │
     ├──► Log full details to structured log (Serilog, OpenTelemetry)
     │    with correlation ID, user ID, request path, timestamp
     │
     └──► HTTP response: { "message": "An internal error occurred.", "correlationId": "abc123" }
```

The correlation ID lets support look up the full details internally without exposing them to the caller.

---

## Step 6 — Discussion

| What leaks | Why it matters |
|------------|---------------|
| Stack trace | Reveals internal class names, file paths, framework; enables targeted attacks |
| Exception type | Identifies backend technology (SQL Server vs PostgreSQL, OS type) |
| Exception message | May contain credentials, SQL queries, file paths, internal hostnames |
| Source assembly | Reveals framework versions for CVE targeting |

**Logging is the right answer, not suppression**

Returning generic errors does not mean ignoring them. Every exception must be logged with full context — the goal is to move that context from the HTTP response to a log aggregator the attacker cannot read.

**Development vs production**

ASP.NET Core's `UseDeveloperExceptionPage()` is safe in development (you need access to the localhost) but catastrophic if left on in production. The workshop flag simulates the same effect without relying on the `ASPNETCORE_ENVIRONMENT` variable, which is easy to misconfigure.

---

## Key Takeaways

- **Exception details are free reconnaissance.** A single 500 response can reveal the full backend stack, database type, credentials, and file system layout.
- **The fix is one line.** Return a generic message; log the full details internally.
- **Add a correlation ID** so errors are traceable without being exploitable.
- **Never let `ex.ToString()` reach the response body.** Even `ex.Message` is dangerous — connection strings, SQL queries, and internal paths appear there routinely.

---

## Further Reading

- [OWASP A05:2021 — Security Misconfiguration](https://owasp.org/Top10/A05_2021-Security_Misconfiguration/)
- [CWE-209 — Information Exposure Through an Error Message](https://cwe.mitre.org/data/definitions/209.html)
- [ASP.NET Core Error Handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling)
- [OWASP Error Handling Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Error_Handling_Cheat_Sheet.html)
