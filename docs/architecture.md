# BookingDojo — Architecture

BookingDojo is a deliberately vulnerable travel booking platform. Every vulnerability has both a vulnerable and a fixed implementation, toggled by a flag in `appsettings.json`. The same binary runs both paths.

---

## Stack

```
Browser (port 5173)
    │
    │  all requests go to /bff/* via Vite proxy
    ▼
BFF — BookingDojo.Bff (port 5001)
    │  ASP.NET Core, cookie auth, JWT never touches the browser
    │
    │  forwards authenticated requests with Authorization: Bearer <jwt>
    ▼
API — BookingDojo.Api (port 5000)
    │  ASP.NET Core Web API, EF Core, JWT validation
    ▼
PostgreSQL (port 5432)
```

| Component | Technology |
|-----------|------------|
| UI | React 18 + TypeScript + Vite |
| BFF | .NET 8 Web API |
| API | .NET 8 Web API + EF Core |
| Database | PostgreSQL 16 |
| Key storage | LocalStack SSM (local AWS emulator) |

---

## The BFF Pattern

The React UI never holds a JWT. After login:

1. The UI sends credentials to `POST /bff/auth/login`
2. The BFF calls the API, receives a JWT and refresh token
3. The BFF encrypts both into an `httpOnly` cookie (`bd_token`) using ASP.NET Core Data Protection
4. All subsequent requests from the UI carry the cookie automatically — no JavaScript involved
5. The BFF decrypts the cookie, extracts the JWT, and forwards it to the API as a `Bearer` token

The cookie is `httpOnly` (JavaScript cannot read it) and `SameSite=Lax` (not sent on cross-site requests). The JWT is never in `localStorage`, `sessionStorage`, or any JavaScript variable.

See [jwt.md](jwt.md) for why this matters and how the encryption works.

---

## Running Locally

```bash
# Start PostgreSQL and LocalStack
docker compose up -d

# Terminal 1 — API (port 5000)
dotnet run --project src/BookingDojo.Api

# Terminal 2 — BFF (port 5001)
dotnet run --project src/BookingDojo.Bff

# Terminal 3 — UI (port 5173)
cd src/bookingdojo-ui && npm run dev
```

The API seeds the database automatically on first start. To reset:

```bash
bash scripts/reset-db.sh
```

---

## Workshop Accounts

| Username | Password | Role |
|----------|----------|------|
| admin | Admin1234! | AdminUser |
| partner | Partner1234! | PartnerUser |
| support | Support1234! | SupportUser |

---

## Workshop Flags

Each exercise is controlled by a flag in `src/BookingDojo.Api/appsettings.json` under `BookingDojo:Workshop`. Set to `"Vulnerable"` to enable the attack surface, `"Fixed"` to switch to the secure implementation. Restart the API after changing.

| Flag | Lab | What it controls |
|------|-----|-----------------|
| `StoredXssAuditLogs` | 01 | Audit log details returned raw (unsanitized) vs. HTML-encoded |
| `BookingIdorAccess` | 02 | Booking endpoint enforces ownership check vs. returns any booking |
| `BookingSearchSqlInjection` | 03 | Booking search uses raw SQL concatenation vs. EF Core parameterized |
| `LoginSqlInjection` | 04 | Login uses raw SQL concatenation vs. EF Core parameterized |
| `BookingSearchResourceConsumption` | 05 | Search returns unbounded results vs. hard-capped at 50 |
| `CouponRedemptionRaceCondition` | 06 | Coupon redeem has TOCTOU window vs. atomic UPDATE |
| `PasswordResetRaceCondition` | 07 | Password reset has TOCTOU window vs. atomic UPDATE |
| `WebhookSsrf` | 08 | Webhook test pings any URL vs. validates against allowlist |
| `ExceptionDetailDisclosure` | 09 | Unhandled exceptions return stack trace vs. generic message |
| `CardPiiStorage` | 10 | Full card number stored and returned vs. tokenized to last 4 digits |
| `MfaBruteForceProtection` | 11 | MFA verify has no attempt limit vs. locked after 5 failures |
| `LogInjection` / `AuditLogDeletion` | 12 | Log injection + unrestricted audit log deletion vs. fixed |
| `ProfileAvatarSsrf` | 13 | Avatar URL fetch proxies any URL vs. validates before fetching |
