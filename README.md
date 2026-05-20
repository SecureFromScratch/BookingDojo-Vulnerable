# BookingDojo

A deliberately vulnerable travel booking platform for secure coding workshops.

Each exercise is toggled by a configuration flag — both the vulnerable and fixed implementations live in the same codebase.

## Quick Start (GitHub Codespaces)

1. Click **Code → Codespaces → Create codespace on main**
2. Wait for setup to complete — the first build takes ~5 minutes (pulls images, restores packages, seeds the database)
3. Once setup finishes, open three terminals and run:

```bash
# Terminal 1 — API (port 5000)
cd src/BookingDojo.Api && dotnet run

# Terminal 2 — BFF (port 5001)
cd src/BookingDojo.Bff && dotnet run

# Terminal 3 — UI (port 5173)
cd src/bookingdojo-ui && npm run dev
```

4. Go to the **Ports** tab and click the globe icon next to port **5173** to open the UI in your browser.

> Subsequent opens of the same Codespace resume in seconds. Only the first build is slow.

## Quick Start (Local)

```bash
# Start PostgreSQL and LocalStack
docker compose up -d

# Seed SSM parameters and database (first time only)
bash scripts/setup.sh

# Terminal 1 — API (port 5000)
dotnet run --project src/BookingDojo.Api

# Terminal 2 — BFF (port 5001)
dotnet run --project src/BookingDojo.Bff

# Terminal 3 — UI (port 5173)
cd src/bookingdojo-ui && npm run dev
```

Open http://localhost:5173 in your browser.

## Workshop Accounts

| Username | Password | Role |
|----------|----------|------|
| admin | Admin1234! | AdminUser |
| partner | Partner1234! | PartnerUser |
| support | Support1234! | SupportUser |

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/architecture.md) | Stack overview, BFF pattern, workshop flags reference |
| [JWT & Session Security](docs/jwt.md) | How tokens are issued, encrypted, rotated, and persisted |

## Running the Labs

Each lab assumes the full stack is already running. Start it with the Quick Start instructions above (local or Codespaces), then set the relevant flag to `"Vulnerable"` in `src/BookingDojo.Api/appsettings.json` and restart the API.

| Lab | Flag |
|-----|------|
| 01 Stored XSS | `StoredXssAuditLogs` |
| 02 IDOR | `BookingIdorAccess` |
| 03 SQL Injection — Search | `BookingSearchSqlInjection` |
| 04 Blind SQL Injection — Login | `LoginSqlInjection` |
| 05 Resource Consumption | `BookingSearchSqlInjection` + `BookingSearchResourceConsumption` |
| 06 Race Condition — Coupon | `CouponRedemptionRaceCondition` |
| 07 Race Condition — Password Reset | `PasswordResetRaceCondition` |
| 08 SSRF — Webhook | `WebhookSsrf` |
| 09 Exception Disclosure | `ExceptionDetailDisclosure` |
| 10 PII Storage | `CardPiiStorage` |
| 11 MFA Brute Force | `MfaBruteForceProtection` |
| 12 Audit Log Manipulation | `LogInjection` / `AuditLogDeletion` |

## Exercises

| # | Exercise | OWASP | Flag |
|---|----------|-------|------|
| 01 | [Stored XSS in Audit Logs](labs/01-stored-xss-audit-logs.md) | A03 XSS | `StoredXssAuditLogs` |
| 02 | [IDOR on Bookings](labs/02-idor-bookings.md) | A01 Broken Access Control | `BookingIdorAccess` |
| 03 | [SQL Injection — Booking Search](labs/03-sql-injection-booking-search.md) | A03 Injection | `BookingSearchSqlInjection` |
| 04 | [Time-Based Blind SQL Injection — Login](labs/04-sql-injection-time-based.md) | A03 Injection | `LoginSqlInjection` |
| 05 | [Uncontrolled Resource Consumption](labs/05-uncontrolled-resource-consumption.md) | A05 Security Misconfiguration | `BookingSearchResourceConsumption` |
| 06 | [Race Condition — Coupon Redemption](labs/06-race-condition-coupon.md) | A04 Insecure Design | `CouponRedemptionRaceCondition` |
| 07 | [Race Condition — Password Reset](labs/07-race-condition-password-reset.md) | A04 Insecure Design | `PasswordResetRaceCondition` |
| 08 | [SSRF — Webhook](labs/08-ssrf-webhook.md) | A10 SSRF | `WebhookSsrf` |
| 09 | [Exception Information Disclosure](labs/09-exception-information-disclosure.md) | A05 Security Misconfiguration | `ExceptionDetailDisclosure` |
| 10 | [Sensitive Data Exposure — PII Storage](labs/10-sensitive-data-exposure-pii.md) | A02 Cryptographic Failures | `CardPiiStorage` |
| 11 | [MFA Brute Force](labs/11-mfa-brute-force.md) | A07 Auth Failures | `MfaBruteForceProtection` |
| 12 | [Audit Log Manipulation](labs/12-audit-log-manipulation.md) | A09 Logging Failures | `LogInjection` / `AuditLogDeletion` |

## Toggling an Exercise

Edit `src/BookingDojo.Api/appsettings.json` under `BookingDojo:Workshop`:

```json
{
  "BookingDojo": {
    "Workshop": {
      "StoredXssAuditLogs": "Vulnerable"
    }
  }
}
```

Set to `"Vulnerable"` to enable the attack surface, `"Fixed"` to switch to the secure implementation. Restart the API after changing. See [docs/architecture.md](docs/architecture.md) for the full flags reference.

## Resetting the Database

```bash
bash scripts/reset-db.sh
```
