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

In Vulnerable mode, user-supplied values (such as the login `username`) are string-interpolated directly into `ILogger` messages:

```csharp
// AuditLogService.cs — Vulnerable
_logger.LogInformation($"AUDIT [{action}] user={username} ip={ip} :: {details}");
```

A `\n` (newline) inside `username` terminates the current log line and starts a new one. Anyone reading raw log output—a terminal, a log file, a SIEM that ingests plain text—sees the injected content as a legitimate log entry.

### Attack

Run while the server is up and watch its console:

```bash
curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d $'{"username":"alice\\n[CRITICAL] 2026-05-05 11:00:00 admin ROLE_CHANGED to SuperAdmin","password":"x"}'
```

The server console shows something like:

```
info: BookingDojo.Api.Services.AuditLogService[0]
      AUDIT [LOGIN_FAILED] user=alice
[CRITICAL] 2026-05-05 11:00:00 admin ROLE_CHANGED to SuperAdmin ip=::1 :: Failed login attempt...
```

The injected `[CRITICAL]` line looks indistinguishable from a real log entry to a human reading the output. An automated alert on `ROLE_CHANGED` would fire on fabricated data.

### How it works at runtime

```
POST /bff/auth/login {"username": "alice\n[CRITICAL] admin ROLE_CHANGED..."}
        │
        ▼
AuditLogService.LogAsync(username = "alice\n[CRITICAL]...")
        │
        ├─ Vulnerable: username interpolated into log message string
        │       │
        │       ▼
        │  ILogger writes to console/file:
        │  "AUDIT [LOGIN_FAILED] user=alice
        │   [CRITICAL] admin ROLE_CHANGED..."      ← looks like a real log line
        │       │
        │       └─► SIEM alert fires on fabricated event
        │
        └─ Fixed: control characters sanitized + structured logging
                │
                ▼
           Sanitize("alice\n[CRITICAL]...") → "alice\\n[CRITICAL]..."
           _logger.LogInformation("AUDIT {Action} user={Username}...", action, sanitized)
                │
                └─► one log line, \n rendered as literal text, no injection
```

### The fix

Two changes together close the vulnerability:

1. **Sanitize control characters** before they reach the logger:

```csharp
private static string Sanitize(string value) =>
    value
        .Replace("\r\n", "\\r\\n")
        .Replace("\n",   "\\n")
        .Replace("\r",   "\\r")
        .Replace("\t",   "\\t");
```

2. **Use structured logging** (named placeholders, not string interpolation):

```csharp
// AuditLogService.cs — Fixed
_logger.LogInformation(
    "AUDIT {Action} user={Username} ip={IpAddress} :: {Details}",
    action, Sanitize(username), ipAddress ?? "unknown", Sanitize(details));
```

With structured logging, the log provider treats each value as a typed field — it never embeds `username` raw into the message string regardless of what characters it contains.

### What the fix does NOT change

The database still stores the raw username (newline and all). The `LOG_ENTRY_DELETED` audit records and the UI's `white-space: pre` cell make injected newlines visible as data — this is intentional, so participants can see what was submitted. Sanitization is only about the `ILogger` output, not about what is persisted.

---

## Lab 12B — Audit Log Deletion

### The vulnerability

In Vulnerable mode the `DELETE /api/audit-logs/{id}` endpoint is open to any authenticated user, including `SupportUser`:

```csharp
// AuditLogsController.cs — Vulnerable
[HttpDelete("{id:guid}")]
[Authorize]
public async Task<IActionResult> DeleteLog(Guid id)
{
    var entry = await _db.AuditLogs.FindAsync(id);
    if (entry == null) return NotFound();
    _db.AuditLogs.Remove(entry);
    await _db.SaveChangesAsync();
    return NoContent();
}
```

A `SupportUser` who took a suspicious action can:

1. Note the ID of their own audit log entry.
2. Call `DELETE /api/audit-logs/{id}` with their own Bearer token.
3. The entry disappears — no secondary record is created. The deletion leaves **no trace**.

### Attack via the UI

Log in as `support / Support1234!` at `http://localhost:5173` and navigate to **Audit Logs**.

The table shows all log entries. Find your own `LOGIN_SUCCESS` row (it will be at the top). Each row has a **Delete** button — click it. The row disappears immediately with no confirmation dialog and no trace left in the table.

Log in as `admin / Admin1234!` and check the Audit Logs — the SupportUser's login entry is simply gone. There is no `LOG_ENTRY_DELETED` record because the vulnerable path writes nothing.

Switch `AuditLogDeletion` to `"Fixed"` and repeat: the Delete button returns an error for SupportUser (`403 Forbidden`), and when AdminUser deletes, a `LOG_ENTRY_DELETED` entry appears at the top of the log.

### Attack via curl

```bash
# 1. Log in as SupportUser
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"support","password":"Support1234!"}' | jq .

# 2. Find your own audit log entry (LOGIN_SUCCESS)
ENTRY_ID=$(curl -s -b cookies.txt http://localhost:5001/bff/audit-logs | \
  jq -r '[.[] | select(.action=="LOGIN_SUCCESS")] | last | .id')

# 3. Delete it — no error, no trace
curl -s -o /dev/null -w "%{http_code}" \
  -X DELETE http://localhost:5001/bff/audit-logs/$ENTRY_ID \
  -b cookies.txt
# → 204

# 4. Confirm it is gone
curl -s -b cookies.txt http://localhost:5001/bff/audit-logs | jq '[.[] | .action]'
```

### How it works at runtime

```
DELETE /bff/audit-logs/{id}  (called by SupportUser)
        │
        ▼
AuditLogsController.DeleteLog(id)
        │
        ├─ Vulnerable: [Authorize] only — any authenticated user can delete
        │       │
        │       ▼
        │  entry found → db.AuditLogs.Remove(entry) → SaveChanges
        │       │
        │       └─► 204 No Content — entry gone, no trace left in DB
        │
        └─ Fixed: role check + immutable secondary record
                │
                ▼
           CallerRole != "AdminUser" → 403 Forbidden (SupportUser blocked)
                │
           AdminUser deletes:
           db.AuditLogs.Remove(entry)
           AuditLogService.LogAsync("LOG_ENTRY_DELETED", ...)   ← cannot be erased silently
                │
                └─► deletion recorded — erasure always leaves a trace
```

### The fix

Two controls added together:

**1. Role check** — only `AdminUser` may delete:

```csharp
if (CallerRole != "AdminUser")
    return Forbid();
```

**2. Immutable secondary record** — every deletion is itself audited:

```csharp
await _auditLogService.LogAsync(
    CallerUsername,
    "LOG_ENTRY_DELETED",
    $"Admin deleted audit log entry {id} (action={entry.Action}, user={entry.Username})",
    HttpContext.Connection.RemoteIpAddress?.ToString());
```

The `LOG_ENTRY_DELETED` entry cannot be deleted by the same `AdminUser` because that deletion would itself create another `LOG_ENTRY_DELETED` record — erasure is always audited.

### Verifying the fix

```bash
# Log in as SupportUser
curl -s -c cookies_support.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"support","password":"Support1234!"}' | jq .

# SupportUser is now rejected
curl -s -o /dev/null -w "%{http_code}" \
  -X DELETE http://localhost:5001/bff/audit-logs/$ENTRY_ID \
  -b cookies_support.txt
# → 403

# Log in as AdminUser
curl -s -c cookies_admin.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin1234!"}' | jq .

# AdminUser can delete, but the deletion is logged
curl -s -o /dev/null -w "%{http_code}" \
  -X DELETE http://localhost:5001/bff/audit-logs/$ENTRY_ID \
  -b cookies_admin.txt
# → 204

curl -s -b cookies_admin.txt http://localhost:5001/bff/audit-logs | \
  jq '[.[] | select(.action=="LOG_ENTRY_DELETED")]'
# → the LOG_ENTRY_DELETED record is there
```

---

## Workshop toggle

| Setting | Effect |
|---|---|
| `LogInjection: Vulnerable` | `\n` in username creates fake log lines in server console |
| `LogInjection: Fixed` | Sanitized + structured logging; `\n` shown as `\n` literal |
| `AuditLogDeletion: Vulnerable` | Any authenticated role can delete; no secondary record |
| `AuditLogDeletion: Fixed` | AdminUser only; every deletion creates `LOG_ENTRY_DELETED` |

Set in `appsettings.json` → `BookingDojo.Workshop`.

---

## Key takeaways

- Never interpolate user-controlled strings into log messages. Use structured logging.
- Sanitize control characters (`\n`, `\r`, `\t`) at the logging boundary, not at input validation — they are valid data in many contexts.
- Audit log deletion is a privilege that should require the highest role.
- Every destructive action on audit infrastructure must itself produce an audit record — otherwise the audit log can be silently cleared.
- "Who can delete the audit logs?" is a question your threat model must answer explicitly.
