# BookingDojo

A deliberately vulnerable travel booking platform for secure coding workshops.

BookingDojo is designed for use in GitHub Codespaces. Each exercise is toggled by a configuration flag — both the vulnerable and fixed implementations live in the same codebase.

## Stack

| Component | Technology |
|-----------|-----------|
| API | .NET 8 Web API + EF Core + PostgreSQL |
| BFF | .NET 8 Web API (cookie proxy) |
| UI | React 18 + TypeScript + Vite |
| Database | PostgreSQL 16 |
| Dev Environment | GitHub Codespaces + Docker |

## Quick Start (GitHub Codespaces)

1. Open this repository in GitHub Codespaces
2. The devcontainer will automatically:
   - Start PostgreSQL
   - Restore NuGet packages and npm dependencies
   - Seed the database with workshop data
3. Start the three services:

```bash
# Terminal 1 — API (port 5000)
cd src/BookingDojo.Api && dotnet run

# Terminal 2 — BFF (port 5001)
cd src/BookingDojo.Bff && dotnet run

# Terminal 3 — UI (port 5173)
cd src/bookingdojo-ui && npm run dev
```

4. Open the forwarded port 5173 in your browser.

## Workshop Accounts

| Username | Password | Role |
|----------|----------|------|
| admin | Admin1234! | AdminUser |
| partner | Partner1234! | PartnerUser |
| support | Support1234! | SupportUser |

## Exercises

| # | Exercise | Configuration Key | Default |
|---|----------|------------------|---------|
| 01 | [Stored XSS in Audit Logs](labs/01-stored-xss-audit-logs.md) | `BookingDojo:Workshop:StoredXssAuditLogs` | `Vulnerable` |

## Toggling an Exercise

Edit `src/BookingDojo.Api/appsettings.json`:

```json
{
  "BookingDojo": {
    "Workshop": {
      "StoredXssAuditLogs": "Vulnerable"
    }
  }
}
```

Change the value to `"Fixed"` to switch to the secure implementation. Restart the API after changing.

## Resetting the Database

```bash
bash scripts/reset-db.sh
```

## Architecture

```
Browser (5173)
    │
    │  /bff/* (Vite proxy)
    ▼
BFF (5001)  ──────────────►  API (5000)
  httpOnly cookie              JWT validation
  bd_token                     EF Core
                               PostgreSQL (5432)
```

The BFF pattern keeps the JWT out of browser storage. The React app never sees the token directly.
