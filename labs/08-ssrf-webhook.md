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
