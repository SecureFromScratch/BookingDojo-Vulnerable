# Lab 11 – Brute Force MFA Protection

## Learning objectives

- Understand why short OTPs without rate limiting are trivially enumerable
- See how a 4-digit code (10,000 possibilities) can be brute-forced in seconds
- Learn the mitigation: invalidate the challenge after N failed attempts

## Background

Multi-factor authentication adds a second proof of identity beyond a password.
A common implementation generates a short numeric code (OTP) that is delivered
out-of-band (SMS, email, authenticator app) and must be submitted within a short window.

In BookingDojo, **checkout requires MFA**. Before a payment is processed, the server
checks the JWT for an `mfa_verified_at` claim issued within the last 5 minutes. If the
claim is absent or stale, the checkout endpoint returns `403 { "requiresMfa": true }`.

This is a realistic pattern — banks call it "step-up authentication": low-risk actions
(browse, search) require only a password; high-risk actions (pay, transfer) require a
second factor.

The weakness: if the verification endpoint does not limit failed attempts, an attacker
who already has a session (e.g., stolen cookie) can enumerate all possible codes. For a
4-digit OTP that is only 10,000 requests — trivially fast.

In **Vulnerable** mode: no attempt counting. All 10,000 codes can be tried; the correct
one always succeeds, stamping `mfa_verified_at` into the session and unblocking checkout.

In **Fixed** mode: after 5 wrong guesses the challenge is invalidated and the endpoint
returns `429 Too Many Requests`. A new challenge must be requested, starting the clock
and (in a real system) triggering a new out-of-band delivery to the legitimate user.

---

## Attack scenario

```
1. Attacker steals the victim's session cookie (e.g., via XSS — Lab 01)
2. Attacker calls POST /bff/cart/checkout  →  403 { requiresMfa: true }
3. Attacker calls POST /bff/auth/mfa/challenge  →  new OTP generated
4. Attacker brute-forces POST /bff/auth/mfa/verify (0000 … 9999)
5. Correct code found  →  200, session cookie updated with mfa_verified_at
6. Attacker retries POST /bff/cart/checkout  →  200, booking created
```

---

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/bff/cart/checkout`      | Requires `mfa_verified_at` in JWT (< 5 min old) — returns 403 if absent |
| `POST` | `/bff/auth/mfa/challenge` | Generate a fresh 4-digit OTP (replaces any existing one) |
| `GET`  | `/bff/auth/mfa/otp`       | **Workshop only** — returns the current code (simulates SMS/email) |
| `POST` | `/bff/auth/mfa/verify`    | Submit a code — vulnerable to brute force without rate limiting |

All endpoints require an active session cookie (log in via `POST /bff/auth/login` first).

---

## Step 1 – Confirm checkout is blocked without MFA

```bash
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

curl -s -b cookies.txt -X POST http://localhost:5001/bff/cart/checkout \
  -H "Content-Type: application/json" \
  -d '{}' | jq .
```

Response:
```json
{ "requiresMfa": true, "message": "Payment requires MFA verification. Please verify your identity and try again." }
```

HTTP status `403`. The server enforces MFA at the API layer — bypassing the UI makes no difference.

---

## Step 2 – Request a challenge

```bash
curl -s -b cookies.txt -X POST http://localhost:5001/bff/auth/mfa/challenge | jq
```

Response:
```json
{ "expiresAt": "2026-05-04T12:10:00Z", "ttlMinutes": 10 }
```

---

## Step 3 – Retrieve the OTP (workshop delivery simulation)

```bash
curl -s -b cookies.txt http://localhost:5001/bff/auth/mfa/otp | jq
```

Response (Vulnerable mode):
```json
{
  "code": "3742",
  "expiresAt": "2026-05-04T12:10:00Z",
  "attemptsRemaining": null,
  "workshopNote": "This endpoint exists only for the workshop — it simulates SMS/email delivery."
}
```

`attemptsRemaining: null` means the server has no attempt counter at all — the concept
doesn't exist in the vulnerable implementation. This is itself a signal: a `null` where a
rate-limit value should be is a reliable indicator that the protection is absent.

Response (Fixed mode):
```json
{
  "code": "3742",
  "expiresAt": "2026-05-04T12:10:00Z",
  "attemptsRemaining": 5,
  ...
}
```

`attemptsRemaining: 5` confirms the counter was initialised when the challenge was created.
Each wrong guess decrements it; reaching 0 invalidates the challenge.

---

## Step 4 – Brute-force attack (Vulnerable mode)

Set `MfaBruteForceProtection` to `"Vulnerable"` in `appsettings.json` and restart the API.

The script in `labs/hacking_scripts/brute_force_otp_and_checkout.sh`:
1. Adds a hotel to the cart (simulating the victim's in-progress booking)
2. Confirms checkout is blocked with 403
3. Requests a fresh OTP challenge
4. Enumerates 0000–9999 until verify returns 200
5. Immediately attempts checkout to prove the session is now MFA-stamped

> **Note:** `-c "$COOKIES"` on the verify call is critical — the BFF re-issues the session
> cookie after a successful verify (stamping `mfa_verified_at`), and curl must save it.

```bash
sh labs/hacking_scripts/brute_force_otp_and_checkout.sh cookies.txt
```

Expected output:
```
Adding item to cart…
Cart item added (hotel a620e356-…).
Confirming checkout requires MFA…
Confirmed: checkout blocked (403). Starting brute force…
Brute-forcing OTP (0000–9999)…
Found OTP: 3742 (after 3742 attempts)
Attempting checkout…
{
  "bookings": [{ "id": 42, "hotelName": "Beach Paradise Resort", ... }],
  ...
}
```

On average 5,000 requests are needed; at 200 req/s that is ~25 seconds.

---

## Step 5 – Observe the Fixed mode

Set `MfaBruteForceProtection` to `"Fixed"` and restart the API.

```bash
# Request a new challenge first
curl -s -b cookies.txt -X POST http://localhost:5001/bff/auth/mfa/challenge > /dev/null

# Send 5 wrong codes
for code in 0000 0001 0002 0003 0004; do
  curl -s -b cookies.txt -X POST http://localhost:5001/bff/auth/mfa/verify \
    -H "Content-Type: application/json" \
    -d "{\"code\":\"$code\"}" | jq .
done
```

After 5 failures you receive:
```json
{ "message": "Too many failed attempts. Challenge invalidated — request a new one." }
```

HTTP status `429`. The challenge is deleted from the database. The attacker must call
`POST /bff/auth/mfa/challenge` again — in a real system that triggers a new SMS/email to
the legitimate user, who would notice the repeated messages.

---

## How it works at runtime

```
Attacker (stolen cookie) calls POST /bff/cart/checkout
        │
        ▼
CartController.Checkout()
        │
        ├─ checks JWT for mfa_verified_at claim
        │       │
        │       └─► absent or > 5 min old → 403 { requiresMfa: true }
        │
Attacker calls POST /bff/auth/mfa/challenge  →  new OTP created in DB
        │
Attacker calls POST /bff/auth/mfa/verify {"code": "XXXX"}  (repeated)
        │
        ├─ Vulnerable: no attempt counter, no lockout
        │       │
        │  attempt N:  code correct → 200 OK
        │       │      BFF re-issues session cookie with mfa_verified_at
        │       │
        │       └─► Attacker retries checkout → 200, booking created
        │           10,000 possible codes, ~25s to exhaust at 200 req/s
        │
        └─ Fixed: attempt counter per challenge, lockout after 5
                │
                ▼
           attempt 1-4: code wrong → 401, AttemptCount++
           attempt 5:   code wrong → 429, challenge deleted from DB
                │
                └─► attacker must request a new challenge (new OTP sent to user)
                    brute-force resets to 0 — impractical in practice
```

## Key takeaways

| | Vulnerable | Fixed |
|---|---|---|
| Attempts before lockout | Unlimited | 5 |
| Time to brute-force (200 req/s) | ~25 seconds | Impractical |
| Status on lockout | 401 forever | 429, challenge deleted |
| Checkout unblocked? | Yes, after correct code | No — attacker stuck in a loop |

**Additional mitigations used in production:**
- Longer codes (6–8 digits, or alphanumeric)
- Per-user IP-based rate limiting on the challenge endpoint
- Exponential back-off between attempts
- Challenge bound to the device/session that requested it
- Short TTL (60–120 seconds instead of 10 minutes)
- `mfa_verified_at` window of 5 minutes limits the useful window even after a successful verify
