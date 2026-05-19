# Exercise 01 — Stored XSS in Audit Logs

**Difficulty:** Beginner  
**Category:** Injection / Cross-Site Scripting  
**Configuration flag:** `BookingDojo:Workshop:StoredXssAuditLogs`

---

## Scenario

You are a security researcher reviewing BookingDojo's admin dashboard. The application stores an audit log for every user action and displays it in the admin interface. You notice the audit log viewer renders the "Details" column differently from the rest of the table.

Your goal: inject a payload that executes JavaScript in the browser of any admin or support user who views the audit log.

---

## Background

**Stored XSS** (also called Persistent XSS) occurs when:
1. An attacker stores malicious HTML or JavaScript in the database via a legitimate-looking input
2. The application later retrieves that data and renders it in the browser without sanitizing it
3. The JavaScript executes in the victim's browser — with access to their cookies, DOM, and session

Stored XSS is considered more dangerous than Reflected XSS because the payload executes automatically for every user who views the affected page, without requiring the victim to click a crafted link.

> **Note on session tokens:** BookingDojo stores the JWT inside an `httpOnly` cookie, so `document.cookie` cannot expose it. XSS payloads here target the UI (defacement, keylogging, redirects) rather than direct token theft. See [docs/jwt.md](../docs/jwt.md) for how the cookie protection works.

---

## Setup

Ensure the exercise is in the vulnerable state:

```json
// src/BookingDojo.Api/appsettings.json
{
  "BookingDojo": {
    "Workshop": {
      "StoredXssAuditLogs": "Vulnerable"
    }
  }
}
```

Restart the API if you changed the config.

---

## Step 1 — Understand the Attack Surface

Log in as `partner / Partner1234!` and navigate to **Hotels**.

Click **+ Add Hotel** and read the workshop tip in the yellow banner. Notice that:
- The hotel description is stored in the database
- When a hotel is created, an audit log entry is written with the hotel name and description embedded in the `Details` field
- Admins and support users can see these audit log entries

The `Details` field is the attack surface.

---

## Step 2 — Inject the Payload

Create a hotel with this description:

```
<img src=x onerror="alert('XSS from ' + document.cookie)">
```

Fill in any hotel name and location, then click **Create Hotel**.

---

## Step 3 — Trigger the XSS

Log in as `admin / Admin1234!` (or `support / Support1234!`) and navigate to **Audit Logs**.

**Expected result:** An alert dialog appears, showing the message `XSS from ` followed by any cookies accessible to JavaScript.

---

## Step 4 — Understand Why It Works

### The vulnerable server-side code

**File:** `src/BookingDojo.Api/Controllers/AuditLogsController.cs`

```csharp
if (_workshopOptions.Value.StoredXssAuditLogs == "Vulnerable")
{
    // Details is returned as-is — the HTML payload from the database
    // is sent directly to the browser with no encoding.
    return Ok(logs.Select(l => new AuditLogDto(
        l.Id, l.Timestamp, l.Username, l.Action, l.Details)));
}
```

### The vulnerable client-side code

**File:** `src/bookingdojo-ui/src/pages/AuditLogsPage.tsx`

```tsx
{/* dangerouslySetInnerHTML causes React to set innerHTML directly,    */}
{/* bypassing React's normal XSS protections. The browser parses the  */}
{/* stored HTML and executes any embedded JavaScript.                  */}
<td dangerouslySetInnerHTML={{ __html: log.details }} />
```

The vulnerability is a **combination** of two mistakes:
1. The server returns raw, unsanitized HTML from the database
2. The client renders it as HTML instead of as text

Either fix alone is sufficient to prevent the exploit — but both should be fixed for defense in depth.

---

## Step 5 — Apply the Fix

### Fix A: Server-side encoding (recommended)

Change the config flag to switch to the fixed path:

```json
"StoredXssAuditLogs": "Fixed"
```

The fixed server-side code in `AuditLogsController.cs`:

```csharp
// HTML-encode the Details field before returning it.
// Even if the client renders it with innerHTML, the browser
// will display the encoded text rather than execute it.
var encoder = HtmlEncoder.Default;
return Ok(logs.Select(l => new AuditLogDto(
    l.Id, l.Timestamp, l.Username, l.Action,
    encoder.Encode(l.Details))));
```

`HtmlEncoder.Default.Encode()` converts `<`, `>`, `"`, `'`, and `&` to their HTML entity equivalents (`&lt;`, `&gt;`, etc.), so `<img onerror=...>` becomes the literal text `&lt;img onerror=...&gt;`.

### How it works at runtime

```
partner creates hotel with <img onerror="alert()"> in description
        │
        ▼
AuditLogService.LogAsync()         ← stores raw HTML string in DB
        │
        ▼
DB: Details = '<img onerror="alert()">'
        │
        ▼
admin views Audit Logs
        │
        ▼
AuditLogsController.GetLogs()
        │
        ├─ Vulnerable: returns Details as-is from DB
        │       │
        │       ▼
        │  React: <td dangerouslySetInnerHTML={{ __html: log.details }} />
        │       │
        │       └─► browser parses HTML → executes JS → alert fires
        │
        └─ Fixed: HtmlEncoder.Encode(Details) before returning
                │
                ▼
           React: <td dangerouslySetInnerHTML={{ __html: log.details }} />
                │
                └─► browser displays literal text: &lt;img onerror=...&gt;
```

### Fix B: Client-side — remove dangerouslySetInnerHTML

Replace the vulnerable `<td>` in `AuditLogsPage.tsx`:

```tsx
{/* FIXED: render as text, not HTML */}
<td>{log.details}</td>
```

React's default rendering always uses `textContent` instead of `innerHTML`, so even unencoded HTML from the server will be displayed as literal text.

---

## Step 6 — Verify the Fix

1. Apply Fix A (config flag) and restart the API
2. Log in as `partner / Partner1234!` and create another hotel with the XSS payload
3. Log in as `admin / Admin1234!` and navigate to **Audit Logs**
4. **Expected result:** The payload is displayed as literal text — no alert fires

---

## Key Takeaways

| | Vulnerable | Fixed |
|--|-----------|-------|
| **Server** | Returns raw DB content | HTML-encodes output |
| **Client** | `dangerouslySetInnerHTML` | Text rendering (React default) |
| **Defense in depth** | Neither layer protects | Both layers protect |

**Never use `dangerouslySetInnerHTML` with data from the server unless you have explicitly sanitized it on the server.** The name is a warning — React is telling you this is dangerous.

**Always encode output at the point of rendering.** Encoding at the API layer is the most reliable defense because it protects every client (browser, mobile app, API consumer), not just the one you control.

---

## Further Reading

- [OWASP XSS Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html)
- [React documentation: dangerouslySetInnerHTML](https://react.dev/reference/react-dom/components/common#dangerously-setting-the-inner-html)
- [OWASP Top 10: A03 Injection](https://owasp.org/Top10/A03_2021-Injection/)
