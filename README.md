# BookingDojo
A deliberately vulnerable travel booking platform for secure coding workshops.

<img width="1312" height="411" alt="bookingdojo" onclick="alert(1)" src="https://github.com/user-attachments/assets/93d0dfba-cc05-4db0-9c28-ddc8ef51a5ac" />



## Quick Start (GitHub Codespaces)

1. Click **Code → Codespaces → Create codespace on main**
2. Wait for setup to complete — the first build runs automatically and takes ~10 minutes (pulls images, restores packages, seeds the database)
3. Once setup finishes, open three terminals and run:

```bash
# Terminal 1 — API (port 5000)
cd src/BookingDojo.Api && dotnet run

# Terminal 2 — BFF (port 5001)
cd src/BookingDojo.Bff && dotnet run

# Terminal 3 — UI (port 5173)
cd src/bookingdojo-ui && npm run dev
```

4. Once the terminal goes quiet, verify the setup completed successfully:

```bash
bash scripts/verify.sh
```

A passing run looks like this:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  BookingDojo — Setup Verification
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  ✅  PostgreSQL    reachable on :5432
  ✅  LocalStack    reachable on :4566
  ✅  SSM param     ConnectionStrings/BookingDojo present
  ✅  SSM param     BookingDojo/Jwt/Secret present

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  ✅  All checks passed — ready to use!
```

If any check fails the script prints the exact command to fix it.

5. Go to the **Ports** tab and click the globe icon next to port **5173** to open the UI in your browser.

> Subsequent opens of the same Codespace resume in seconds. Only the first build is slow.

> **When you're done, delete the Codespace to avoid consuming your GitHub credits.** Click the **three-dot menu (...)** next to your Codespace at github.com/codespaces and choose **Delete**.

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

Each lab file describes the vulnerability and ends after the Background section. Your job is to:
1. **Exploit** — follow the steps to observe the vulnerability in the running app
2. **Fix** — apply the code change described and verify the attack no longer works

If you get stuck or want to check your solution, the full step-by-step walkthrough (including the fix and verification steps) is in [`labs/full_walkthrough/`](labs/full_walkthrough/).

> **Spoiler warning:** the full walkthrough reveals the exact fix. Try to work through the lab on your own first.

Each lab assumes the full stack is already running. Start it with the Quick Start instructions above (local or Codespaces).

Labs that use curl need a session cookie. Log in once and reuse the cookie file throughout:

```bash
# Log in as partner (most labs)
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

# Log in as admin
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin1234!"}' | jq .

# Log in as support
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"support","password":"Support1234!"}' | jq .
```

All subsequent curl commands use `-b cookies.txt` to send the session cookie.

| # | Exercise | OWASP |
|---|----------|-------|
| 01 | [Stored XSS in Audit Logs](labs/01-stored-xss-audit-logs.md) | A03 XSS |
| 02 | [IDOR on Bookings](labs/02-idor-bookings.md) | A01 Broken Access Control |
| 03 | [SQL Injection — Booking Search](labs/03-sql-injection-booking-search.md) | A03 Injection |
| 04 | [Time-Based Blind SQL Injection — Login](labs/04-sql-injection-time-based.md) | A03 Injection |
| 05 | [Uncontrolled Resource Consumption](labs/05-uncontrolled-resource-consumption.md) | A05 Security Misconfiguration |
| 06 | [Race Condition — Coupon Redemption](labs/06-race-condition-coupon.md) | A04 Insecure Design |
| 07 | [Race Condition — Password Reset](labs/07-race-condition-password-reset.md) | A04 Insecure Design |
| 08 | [SSRF — Webhook](labs/08-ssrf-webhook.md) | A10 SSRF |
| 09 | [Exception Information Disclosure](labs/09-exception-information-disclosure.md) | A05 Security Misconfiguration |
| 10 | [Sensitive Data Exposure — PII Storage](labs/10-sensitive-data-exposure-pii.md) | A02 Cryptographic Failures |
| 11 | [MFA Brute Force](labs/11-mfa-brute-force.md) | A07 Auth Failures |
| 12 | [Audit Log Manipulation](labs/12-audit-log-manipulation.md) | A09 Logging Failures |

## Resetting the Database

```bash
bash scripts/reset-db.sh
```
