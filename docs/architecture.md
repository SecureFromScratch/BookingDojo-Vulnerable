# BookingDojo — Architecture

BookingDojo is a deliberately vulnerable travel booking platform. Every lab targets a real vulnerability in the running application.

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

