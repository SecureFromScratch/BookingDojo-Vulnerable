# Lab 12 — Audit Log Manipulation

**Category:** Logging & Monitoring Failures (OWASP A09:2021)  
**Difficulty:** Intermediate  
**Two sub-labs:** A) CRLF Log Injection · B) Audit Log Deletion

---

## Background

Audit logs are a critical security control: they provide the evidence trail needed to detect attacks, investigate incidents, and hold users accountable. This lab covers two ways attackers undermine that control:

- **Log Injection (CRLF)** — injecting newlines into log messages to forge fake log lines  
- **Audit Log Deletion** — abusing over-permissive delete endpoints to silently erase evidence

---

## Lab 12A — CRLF Log Injection

### The vulnerability

The vulnerable `AuditLogService` string-interpolates user-supplied values directly into the `ILogger` message:

```csharp
// AuditLogService.cs — VULNERABLE
_logger.LogInformation($"AUDIT [{action}] user={username} ip={ip} :: {details}");
```

A `\n` inside `username` terminates the current log line and starts a new one. Anyone reading the console or a log file sees the injected content as a legitimate log entry.

---

### Step 1 – Observe the vulnerability

Run this while the server console is visible:

```bash
curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d $'{"username":"alice\\n[CRITICAL] 2026-05-05 11:00:00 admin ROLE_CHANGED to SuperAdmin","password":"x"}'
```

The server console shows:

```
info: BookingDojo.Api.Services.AuditLogService[0]
      AUDIT [LOGIN_FAILED] user=alice
[CRITICAL] 2026-05-05 11:00:00 admin ROLE_CHANGED to SuperAdmin ip=::1 :: ...
```

The injected `[CRITICAL]` line looks indistinguishable from a real log entry. An automated alert on `ROLE_CHANGED` would fire on fabricated data.

---

### Step 2 – Apply the fix

**File:** `src/BookingDojo.Api/Services/AuditLogService.cs`

Two changes together close the vulnerability.

**Add `Sanitize` and `NonPrintable` to the class:**

```csharp
using System.Text.RegularExpressions;

// Allowlist: strip every Unicode "Other" character (control, format, surrogate,
// private-use, unassigned). Anything with no printable glyph is removed.
private static readonly Regex NonPrintable = new(@"\p{C}", RegexOptions.Compiled);

private static string Sanitize(string value) =>
    NonPrintable.Replace(value, "");
```

`\p{C}` covers control characters (`\n`, `\r`, `\t`, …), format characters (zero-width joiners, BOM, …), surrogates, and private-use code points — anything without a printable glyph.

**Replace the `LogInformation` call in `LogAsync`:**

```csharp
// VULNERABLE — remove:
_logger.LogInformation($"AUDIT [{action}] user={username} ip={ipAddress ?? "unknown"} :: {details}");

// Replace with:
_logger.LogInformation(
    "AUDIT {Action} user={Username} ip={IpAddress} :: {Details}",
    action, Sanitize(username), ipAddress ?? "unknown", Sanitize(details));
```

Using named placeholders (structured logging) means the log provider treats each value as a typed field — it never embeds `username` raw into the message string regardless of what characters it contains.

> **Note:** The database still stores the raw username unchanged. Sanitization applies only to the `ILogger` output, not to what is persisted.

---

### Step 3 – Verify

Restart the API and repeat the same curl command. The server console now shows a single line:

```
info: BookingDojo.Api.Services.AuditLogService[0]
      AUDIT LOGIN_FAILED user=alice[CRITICAL] 2026-05-05 11:00:00 admin ROLE_CHANGED to SuperAdmin ip=::1 :: ...
```

The `\n` is stripped. The injected text appears on the same line as the legitimate entry — it is clearly garbage data, not a forged log line.

---

### How it works at runtime

```
POST /bff/auth/login {"username": "alice\n[CRITICAL] admin ROLE_CHANGED..."}
        │
        ▼
AuditLogService.LogAsync(username = "alice\n[CRITICAL]...")
        │
        ├─ Vulnerable: username interpolated raw into log message string
        │       │
        │       ▼
        │  ILogger writes two lines:
        │  "AUDIT [LOGIN_FAILED] user=alice
        │   [CRITICAL] admin ROLE_CHANGED..."      ← looks like a real log entry
        │
        └─ Fixed: Sanitize() + structured logging
                │
                ▼
           Sanitize("alice\n[CRITICAL]...") → "alice[CRITICAL]..."  ← \n removed
           _logger.LogInformation("AUDIT {Action} user={Username}...", action, sanitized)
                │
                └─► one line, control characters gone, no injection possible
```

---

## Lab 12B — Audit Log Deletion

### The vulnerability

The vulnerable `DELETE /api/audit-logs/{id}` is open to any authenticated user, including `SupportUser`:

```csharp
// AuditLogsController.cs — VULNERABLE
[HttpDelete("{id:guid}")]
[Authorize]
public async Task<IActionResult> DeleteLog(Guid id)
{
    var log = await _db.AuditLogs.FindAsync(id);
    if (log == null) return NotFound();

    _db.AuditLogs.Remove(log);
    await _db.SaveChangesAsync();
    return NoContent();
}
```

A `SupportUser` who took a suspicious action can delete their own audit entry — and no secondary record is created. The deletion leaves **no trace**.

---

### Step 1 – Observe the vulnerability

**Via the UI:** Log in as `support / Support1234!` at `http://localhost:5173` and go to **Audit Logs**. Find your own `LOGIN_SUCCESS` row and click **Delete**. The row disappears immediately.

Log in as `admin / Admin1234!` and check the Audit Logs — the entry is simply gone. There is no `LOG_ENTRY_DELETED` record anywhere.

**Via curl:**

```bash
# Log in as SupportUser
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"support","password":"Support1234!"}' | jq .

# Find your own LOGIN_SUCCESS entry
ENTRY_ID=$(curl -s -b cookies.txt http://localhost:5001/bff/audit-logs | \
  jq -r '[.[] | select(.action=="LOGIN_SUCCESS")] | last | .id')

# Delete it — 204, no trace
curl -s -o /dev/null -w "%{http_code}" \
  -X DELETE "http://localhost:5001/bff/audit-logs/$ENTRY_ID" \
  -b cookies.txt

# Confirm it is gone
curl -s -b cookies.txt http://localhost:5001/bff/audit-logs | jq '[.[] | .action]'
```

---

### Step 2 – Apply the fix

Two changes across two files.

**File 1: `src/BookingDojo.Api/Program.cs`**

Find the `AddAuthorization` call and add the `"AdminOnly"` policy to it.

If you have not applied any previous lab fixes, the call looks like this — replace it:

```csharp
// Before:
builder.Services.AddAuthorization();

// After:
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("AdminUser"));
});
```

If you already have policies registered (e.g. from Lab 02), just add the new policy inside the existing `options` block:

```csharp
builder.Services.AddAuthorization(options =>
{
    // ... existing policies ...

    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("AdminUser"));
});
```

**File 2: `src/BookingDojo.Api/Controllers/AuditLogsController.cs`**

Add `[Authorize(Policy = "AdminOnly")]` to `DeleteLog` and add the immutable secondary record before the removal:

```csharp
// VULNERABLE — remove:
[HttpDelete("{id:guid}")]
public async Task<IActionResult> DeleteLog(Guid id)
{
    var log = await _db.AuditLogs.FindAsync(id);
    if (log == null) return NotFound();
    _db.AuditLogs.Remove(log);
    await _db.SaveChangesAsync();
    return NoContent();
}

// Replace with:
[HttpDelete("{id:guid}")]
[Authorize(Policy = "AdminOnly")]
public async Task<IActionResult> DeleteLog(Guid id)
{
    var log = await _db.AuditLogs.FindAsync(id);
    if (log == null) return NotFound();

    await _auditLogService.LogAsync(
        CallerUsername,
        "LOG_ENTRY_DELETED",
        $"Audit entry {id} deleted (action={log.Action}, user={log.Username}, ts={log.Timestamp:u})",
        HttpContext.Connection.RemoteIpAddress?.ToString());

    _db.AuditLogs.Remove(log);
    await _db.SaveChangesAsync();
    return NoContent();
}
```

The `[Authorize(Policy = "AdminOnly")]` attribute makes the framework enforce the role check and return `403 Forbidden` before the method body runs — no manual `if` check needed. `CallerUsername` is a helper property on the controller that reads the name claim from the JWT.

---

### Step 3 – Verify

```bash
# SupportUser is now rejected
curl -s -o /dev/null -w "%{http_code}" \
  -X DELETE "http://localhost:5001/bff/audit-logs/$ENTRY_ID" \
  -b cookies.txt
# → 403

# Log in as AdminUser
curl -s -c cookies_admin.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin1234!"}' | jq .

# AdminUser can delete — but the deletion is logged
curl -s -o /dev/null -w "%{http_code}" \
  -X DELETE "http://localhost:5001/bff/audit-logs/$ENTRY_ID" \
  -b cookies_admin.txt
# → 204

# The LOG_ENTRY_DELETED record is there
curl -s -b cookies_admin.txt http://localhost:5001/bff/audit-logs | \
  jq '[.[] | select(.action=="LOG_ENTRY_DELETED")]'
```

The `LOG_ENTRY_DELETED` record cannot be silently erased — if AdminUser tries to delete it, that deletion creates another `LOG_ENTRY_DELETED` record. Erasure is always audited.

> **Further exercise:** The Delete button is still visible in the UI for SupportUser — it just returns 403 when clicked. In a real application you would also hide or disable the button for roles that lack permission, so the UI matches what the server enforces. The server-side check is the security control; the UI change is a usability improvement. Try implementing it in the frontend as an optional exercise.

---

### How it works at runtime

```
DELETE /bff/audit-logs/{id}  (SupportUser)
        │
        ▼
AuditLogsController.DeleteLog(id)
        │
        ├─ Vulnerable: [Authorize] only — any authenticated user can delete
        │       │
        │       ▼
        │  entry removed, SaveChanges
        │       │
        │       └─► 204, entry gone, no trace in DB
        │
        └─ Fixed: role check + immutable secondary record
                │
                ▼
           CallerRole != "AdminUser" → 403 Forbidden  (SupportUser blocked)
                │
           AdminUser deletes:
           LogAsync("LOG_ENTRY_DELETED", ...)   ← written before removal
           db.AuditLogs.Remove(entry)
                │
                └─► 204, deletion recorded — erasure always leaves a trace
```

---

## Key takeaways

- Never interpolate user-controlled strings into log messages. Use structured logging with named placeholders.
- Sanitize control characters at the logging boundary — they are valid data in many contexts, so removing them at input would be wrong.
- Audit log deletion is a privilege that should require the highest role.
- Every destructive action on audit infrastructure must itself produce an audit record — otherwise the log can be silently cleared.
