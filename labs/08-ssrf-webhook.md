# Exercise 08 — Server-Side Request Forgery (SSRF): Webhook Test

**Difficulty:** Intermediate  
**Category:** Server-Side Request Forgery  
**OWASP Top 10:** A10:2021 — Server-Side Request Forgery

---

## Scenario

Partners can register a webhook URL to receive booking events. The platform provides a "Test Webhook" endpoint (`POST /api/webhooks/test`) that POSTs a sample payload to the supplied URL and returns the response. Because the server makes this outbound HTTP request on the caller's behalf, any URL the attacker supplies becomes a request **from the server's network perspective** — not the client's.

BookingDojo also runs an internal configuration endpoint at `GET/POST /api/internal/secret`. It has no authentication — it relies entirely on not being routable from the public internet. The BFF deliberately does not expose it.

SSRF bridges that gap: the attacker cannot reach the internal endpoint directly, but the server can — and the webhook feature lets the attacker tell the server what to fetch.

> **Local dev note:** When running locally, port 8888 is reachable directly from your browser because everything runs on one machine. In a real deployment the internal service would be on an isolated container network, a private subnet, or bound to a non-routable interface — invisible to anyone outside the server process. The exploitation path is identical in both cases: you are making the *server* issue the request, not your browser. The interesting property of SSRF is not that the URL is secret — it is that the request carries the server's network identity and trust relationships, which your browser does not have.

---

## Background

**Server-Side Request Forgery (SSRF)** occurs when an attacker can cause the server to make an HTTP request to an attacker-controlled target. Because the request originates from the server's network:

- Internal services that only listen on `127.0.0.1` become reachable
- Services that rely on "not being on the internet" as their only access control are fully exposed
- Cloud metadata endpoints (`169.254.169.254`) leak IAM credentials
- Firewall rules that protect internal services from the public internet are bypassed

Common SSRF entry points: webhook URL fields, image/avatar fetch-by-URL, PDF generation from user-supplied URLs, import-from-URL features.

---

## Step 1 — Understand the internal target

The API exposes an internal configuration endpoint on a **separate port (8888)** that simulates a service on an isolated internal network:

```
GET  http://localhost:8888/api/internal/secret   (no authentication required)
POST http://localhost:8888/api/internal/secret
```

Its response contains sensitive internal configuration — DB credentials, Stripe keys, JWT signing secret.

Port 8888 is intentionally **not forwarded** in the Codespace. VS Code is configured to ignore it (`"onAutoForward": "ignore"`). Try opening it in your browser:

```
http://localhost:8888/api/internal/secret
```

When the server makes an outbound HTTP request (via SSRF), it originates from *inside* the same machine. `localhost:8888` is reachable from the server's loopback. The firewall sees: server → server — always allowed.

---

## Step 2 — Exploit SSRF via the UI

Log in as `partner / Partner1234!` and navigate to **Integrations**.

The page has two sections: **Webhooks** (save a URL permanently) and **Test a URL** (one-off probe). Use **Test a URL** for the attack below.

In the **URL** field, enter:

```
http://localhost:8888/api/internal/secret
```

Click **Send Test**. The response panel shows:

```json
{
  "service": "internal-config-v1",
  "warning": "INTERNAL USE ONLY — protected by network access controls.",
  "database": {
    "primary": {
      "host": "postgres.bookingdojo.internal",
      "port": 5432,
      "name": "bookingdojo_prod",
      "username": "bookingdojo_app",
      "password": "Pr0dD4t4b4s3S3cr3t-2024!"
    }
  },
  "stripe": {
    "secretKey": "sk_live_aBcDeFgHiJkLmN0PqRsTuVwXyZ1234567890",
    "webhookSecret": "whsec_XyZ987654321abcdefghijklmn0pqrstu"
  },
  "internalApiKey": "int-api-k3y-N0tF0rPubl1cUse-2024",
  "jwtSigningSecret": "Pr0dJwtS3cr3t!D0-NOT-SHARE-2024"
}
```

The server fetched the internal endpoint **on your behalf**. The credentials never appeared in any BFF route, never in any authentication header — the firewall-equivalent "it's not routable" protection failed the moment the application was given a URL to fetch.

---

## Step 3 — Exploit via curl

Log in first, then send the SSRF probe:

```bash
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://localhost:8888/api/internal/secret"}' | jq .
```

Expected response:

```json
{
  "url": "http://localhost:8888/api/internal/secret",
  "statusCode": "200",
  "body": "{\"service\":\"internal-config-v1\",\"database\":{\"primary\":{\"password\":\"Pr0dD4t4b4s3S3cr3t-2024!\"...}}",
  "error": null
}
```

---

## Step 4 — Real-world targets (for context)

In a real deployment the same technique reaches:

**Cloud instance metadata (AWS IMDSv1):**
```
http://169.254.169.254/latest/meta-data/iam/security-credentials/
```
Returns IAM role names and temporary credentials (`AccessKeyId`, `SecretAccessKey`, `Token`) — full programmatic access to the AWS account. This address is only reachable from within the EC2 instance; SSRF makes the server fetch it on the attacker's behalf.

> This address does not respond in the local workshop environment — it is shown for real-world context only.

**Other internal services:**
```
http://10.0.1.5:8080/admin/status     # internal admin API
https://10.96.0.1/api/v1/namespaces   # Kubernetes API server
```

> These are fictional addresses used for illustration. In a real deployment they would represent services that rely on VPC boundaries for access control.

---

## Step 5 — Apply the fix

**File:** `src/BookingDojo.Api/Controllers/WebhooksController.cs`  
**File:** `src/BookingDojo.Api/Services/DnsResolver.cs` (new)

The fix has four parts:

1. **Domain allowlist** — only explicitly permitted hostnames are accepted. Everything else is rejected before any DNS lookup or outbound request is made. A blocklist alone cannot enumerate every internal address; an allowlist closes that gap entirely.
2. **IP blocklist as defence-in-depth** — even allowlisted domains must resolve to a public IP. This catches DNS rebinding: an attacker who controls an allowed domain's DNS could point it at `127.0.0.1` or the cloud metadata address.
3. **IP pinning via `ConnectCallback`** so the actual TCP connection always goes to the IP that was validated, not whatever DNS returns at connect time.
4. **`AllowAutoRedirect = false`** so a redirect to an internal address after validation is not silently followed.

### Part 1 — Create `DnsResolver.cs`

Create `src/BookingDojo.Api/Services/DnsResolver.cs`:

```csharp
using System.Net;

namespace BookingDojo.Api.Services;

public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host);
}

public class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host) =>
        Dns.GetHostAddressesAsync(host);
}
```

The interface allows tests to inject a fake resolver that returns a known public IP without hitting real DNS.

Register it in `Program.cs`:

```csharp
builder.Services.AddSingleton<IDnsResolver, SystemDnsResolver>();
```

Also update the webhook `HttpClient` registration to set `AllowAutoRedirect = false`:

```csharp
builder.Services.AddHttpClient("webhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false
});
```

### Part 2 — Rewrite `WebhooksController.cs`

**Remove the entire original controller body.** The vulnerable controller has no URL validation — it passes the URL directly to `HttpClient.PostAsync`. Replace it with the implementation below.

The controller needs these `using` directives at the top of the file:

```csharp
using System.Net;
using System.Net.Sockets;
using BookingDojo.Api.Services;
```

**Constructor** — inject `IDnsResolver` (drop `IHttpClientFactory`, `PingUrl` creates its own `HttpClient` per request so the factory is not needed):

```csharp
private readonly BookingDojoDbContext _db;
private readonly IDnsResolver _dns;

public WebhooksController(BookingDojoDbContext db, IDnsResolver dns)
{
    _db = db;
    _dns = dns;
}
```

**`TestWebhook` and `RegisterWebhook`** — the vulnerable versions call `PingUrl(url)` directly after only a format check, with no validation of what the URL points to. Two things change in the fix: a `ValidateUrlAsync` call is inserted before the fetch, and `PingUrl` receives the resolved IP so it can pin the TCP connection.

**`TestWebhook` — remove:**

```csharp
[HttpPost("test")]
public async Task<IActionResult> TestWebhook([FromBody] WebhookTestRequest request)
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
        return BadRequest(new { message = "Invalid URL format" });

    // VULNERABLE — no URL validation, any URL is fetched (SSRF)
    var (statusCode, body, error) = await PingUrl(request.Url);
    return Ok(new { url = request.Url, statusCode, body, error });
}
```

**Replace with:**

```csharp
[HttpPost("test")]
public async Task<IActionResult> TestWebhook([FromBody] WebhookTestRequest request)
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
        return BadRequest(new { message = "Invalid URL format" });

    var (allowed, reason, resolvedIp) = await ValidateUrlAsync(request.Url);
    if (!allowed)
        return BadRequest(new { message = $"URL not allowed: {reason}" });

    var (statusCode, body, error) = await PingUrl(request.Url, resolvedIp!);
    return Ok(new { url = request.Url, statusCode, body, error });
}
```

The only lines that changed: `ValidateUrlAsync` was inserted before `PingUrl`, and `PingUrl` now receives `resolvedIp!` so the TCP connection is pinned to the validated IP.

---

**`RegisterWebhook` — remove:**

```csharp
[HttpPost]
public async Task<IActionResult> RegisterWebhook([FromBody] RegisterWebhookRequest request)
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
        return BadRequest(new { message = "Invalid URL format" });

    // VULNERABLE — no URL validation, any URL is accepted (SSRF)
    var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    var webhook = new Webhook { UserId = userId, Url = request.Url };
    _db.Webhooks.Add(webhook);
    await _db.SaveChangesAsync();

    var (pingStatusCode, pingBody, pingError) = await PingUrl(request.Url);
    return Ok(new RegisterWebhookResponse(
        new WebhookDto(webhook.Id, webhook.Url, webhook.CreatedAt),
        pingStatusCode,
        pingBody,
        pingError));
}
```

**Replace with:**

```csharp
[HttpPost]
public async Task<IActionResult> RegisterWebhook([FromBody] RegisterWebhookRequest request)
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
        return BadRequest(new { message = "Invalid URL format" });

    var (allowed, reason, resolvedIp) = await ValidateUrlAsync(request.Url);
    if (!allowed)
        return BadRequest(new { message = $"URL not allowed: {reason}" });

    var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    var webhook = new Webhook { UserId = userId, Url = request.Url };
    _db.Webhooks.Add(webhook);
    await _db.SaveChangesAsync();

    var (pingStatusCode, pingBody, pingError) = await PingUrl(request.Url, resolvedIp!);
    return Ok(new RegisterWebhookResponse(
        new WebhookDto(webhook.Id, webhook.Url, webhook.CreatedAt),
        pingStatusCode,
        pingBody,
        pingError));
}
```

Same two changes: `ValidateUrlAsync` inserted after the format check, `PingUrl` updated to pass `resolvedIp!`.

**`AllowedWebhookHosts`** — the domain allowlist. Add your real partner domains here; everything else is rejected before any DNS lookup:

```csharp
private static readonly HashSet<string> AllowedWebhookHosts = new(StringComparer.OrdinalIgnoreCase)
{
    "hooks.slack.com",
    "api.stripe.com",
    "webhook.site",
    "webhook.example.com",   // replace with real partner domains
};
```

**`ValidateUrlAsync`** — allowlist first, then DNS validation as defence-in-depth:

```csharp
private async Task<(bool Allowed, string Reason, IPAddress? ResolvedIp)> ValidateUrlAsync(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return (false, "not a valid absolute URL", null);

    if (uri.Scheme != "https")
        return (false, "only HTTPS is permitted", null);

    var host = uri.Host.ToLowerInvariant();

    // Primary gate: reject anything not on the allowlist.
    if (!AllowedWebhookHosts.Contains(host))
        return (false, $"host '{host}' is not on the permitted list", null);

    // Defence-in-depth: even allowed domains must resolve to a public IP.
    if (!IsAllowedHostname(host, out var reason))
        return (false, reason, null);

    IPAddress[] addresses;
    try { addresses = await _dns.ResolveAsync(host); }
    catch { return (false, "hostname could not be resolved", null); }

    if (addresses.Length == 0)
        return (false, "hostname resolved to no addresses", null);

    foreach (var ip in addresses)
    {
        if (!IsPublicIp(ip, out reason))
            return (false, $"hostname resolves to a restricted address ({ip}): {reason}", null);
    }

    return (true, "", addresses[0]);
}
```

**`PingUrl`** — pins the TCP connection to the validated IP and disables automatic redirect following:

```csharp
private async Task<(string? StatusCode, string? Body, string? Error)> PingUrl(string url, IPAddress resolvedIp)
{
    var uri = new Uri(url);
    var port = uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port;

    var handler = new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, ct) =>
        {
            var socket = new Socket(resolvedIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(resolvedIp, port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    var payload = new StringContent(
        """{"event":"booking.created","test":true}""",
        System.Text.Encoding.UTF8, "application/json");
    try
    {
        var response = await client.PostAsync(url, payload);
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 2000) body = body[..2000] + "…[truncated]";
        return (((int)response.StatusCode).ToString(), body, null);
    }
    catch (HttpRequestException ex) { return (null, null, ex.Message); }
    catch (TaskCanceledException) { return (null, null, "Request timed out"); }
}
```

**`IsAllowedHostname`** — hostname-level blocklist (loopback names, `.internal`, `.local`, literal private IPs):

```csharp
private static bool IsAllowedHostname(string host, out string reason)
{
    reason = "";

    if (host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0")
    {
        reason = "loopback addresses are not permitted";
        return false;
    }

    if (host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".localhost"))
    {
        reason = "internal hostnames are not permitted";
        return false;
    }

    if (IPAddress.TryParse(host, out var literalIp))
        return IsPublicIp(literalIp, out reason);

    return true;
}
```

**`IsPublicIp`** — IP-level blocklist (loopback, RFC 1918, link-local, IPv6 ULA):

```csharp
private static bool IsPublicIp(IPAddress ip, out string reason)
{
    reason = "";
    var b = ip.GetAddressBytes();

    if (IPAddress.IsLoopback(ip))
    {
        reason = "loopback addresses are not permitted";
        return false;
    }

    if (b.Length == 4)
    {
        if (b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168))
        {
            reason = "private IP ranges are not permitted";
            return false;
        }
        if (b[0] == 169 && b[1] == 254)
        {
            reason = "link-local addresses are not permitted (potential cloud metadata endpoint)";
            return false;
        }
        if (b[0] == 0)
        {
            reason = "unspecified addresses are not permitted";
            return false;
        }
    }
    else if (b.Length == 16)
    {
        if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80)
        {
            reason = "IPv6 link-local addresses are not permitted";
            return false;
        }
        if ((b[0] & 0xfe) == 0xfc)
        {
            reason = "IPv6 unique-local addresses are not permitted";
            return false;
        }
    }

    return true;
}
```

---

## Step 6 — Verify

Log in and repeat the original SSRF probe:

```bash
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://localhost:8888/api/internal/secret"}' | jq .
```

Expected: `400 Bad Request`

```json
{ "message": "URL not allowed: only HTTPS is permitted" }
```

Try other blocked targets:

```bash
# Loopback rejected even with HTTPS
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"https://127.0.0.1/internal"}' | jq .

# Link-local (cloud metadata) rejected
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"https://169.254.169.254/latest/meta-data/"}' | jq .

# Private IP rejected
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"https://10.0.1.5:8080/admin"}' | jq .

# Internal hostname rejected
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"https://db.internal/status"}' | jq .
```

All return `400 Bad Request`. A valid public HTTPS URL still works normally.

---

## Step 7 — How it works at runtime

```
POST /bff/webhooks/test {"url": "http://localhost:8888/api/internal/secret"}
        │
        ▼
WebhooksController.TestWebhook(url)
        │
        ├─ Without the fix: no URL validation
        │       │
        │       ▼
        │  HttpClient.PostAsync("http://localhost:8888/api/internal/secret")
        │       │
        │       └─► request originates from the SERVER'S loopback (LocalPort=8888)
        │           InternalSecretController: LocalPort==8888 → no auth required → 200
        │           DB password, Stripe key, JWT secret returned to attacker
        │
        └─ With the fix: ValidateUrlAsync → PingUrl(url, resolvedIp)
                │
                ▼
           1. scheme = "http" → reject immediately (only HTTPS)
              host  = "localhost" → reject (hostname blocklist)
                │
                └─► 400 Bad Request — no DNS lookup, no outbound request
                
        For a hostname that passes the name check (evil.example.com):
                │
                ▼
           2. DNS resolved → 93.184.216.34 → IsPublicIp passes
              ConnectCallback pins TCP to 93.184.216.34 port 443
              DNS flip after this point has no effect (rebinding closed)
                │
                └─► HTTPS POST to 93.184.216.34:443 — public server only
                
        For a server that tries to redirect to an internal address:
                │
                ▼
           3. AllowAutoRedirect = false → 3xx returned as-is, not followed
                │
                └─► redirect target never fetched
```

---

## Step 8 — Discussion

| Defence | Description | Limitations |
|---------|-------------|-------------|
| URL allowlist (known domains only) | Only specific partner-registered domains are callable | Must maintain list; DNS rebinding can bypass |
| IP blocklist (private ranges + loopback) | Block RFC 1918, link-local, loopback | DNS rebinding: attacker resolves to public IP, then flips DNS to 127.0.0.1 |
| Resolve DNS before fetch, validate IP | Resolve hostname → check IP → then fetch that IP | Requires re-validation on every redirect |
| Outbound firewall / egress filtering | Network-level block of private ranges | Defence-in-depth, not sufficient alone |
| Metadata service IMDSv2 | AWS: require token for metadata (SSRF can't get the token) | Doesn't prevent SSRF to other internal services |

**DNS rebinding (SSRF bypass)**

An attacker registers `evil.example.com` pointing at a public IP — it passes the validation. They then flip the DNS TTL-0 record to `127.0.0.1` before the actual fetch resolves. Mitigation: resolve the hostname once at validation time, record the IP, and make the actual connection to that IP — never re-resolve.

**Redirects**

If the external server responds with `HTTP 301 → http://169.254.169.254/`, a naive implementation follows the redirect to the internal address. Fix: disable `AllowAutoRedirect` on the HttpClient, or re-validate each redirect target.

**Why authentication alone is not enough**

`InternalSecretController` has no `[Authorize]`. Even if it did, SSRF can carry the server's own authentication context — if the internal service uses IP-based trust or mutual TLS certificates shared across services, SSRF inherits those too.

---

## Key Takeaways

- **"Not reachable from the internet" is not access control.** Any URL-fetching feature punches through that boundary from the inside.
- **Internal services need their own authentication.** Network controls are defence-in-depth, not the primary gate.
- **Cloud metadata is the highest-value SSRF target.** `169.254.169.254` hands the attacker IAM credentials for the entire account.
- **Validate the URL before the first DNS lookup.** Check scheme, then resolve the hostname to an IP and check that IP. Validation after the fetch is too late.

---

## Further Reading

- [OWASP A10:2021 — Server-Side Request Forgery](https://owasp.org/Top10/A10_2021-Server-Side_Request_Forgery_%28SSRF%29/)
- [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
- [PortSwigger SSRF](https://portswigger.net/web-security/ssrf)
- [AWS IMDSv2 — Requiring signed token for instance metadata](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/configuring-instance-metadata-service.html)
