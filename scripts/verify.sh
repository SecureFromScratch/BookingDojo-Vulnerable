#!/usr/bin/env bash
# Verify that BookingDojo setup completed successfully.
# Run this after the Codespace finishes its first build, or any time you're unsure.

PASS=0
FAIL=0

ok()   { echo "  ✅  $1"; PASS=$((PASS+1)); }
fail() { echo "  ❌  $1"; FAIL=$((FAIL+1)); }

_aws() {
  AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_DEFAULT_REGION=us-east-1 \
    aws --endpoint-url=http://localhost:4566 "$@" 2>/dev/null
}

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  BookingDojo — Setup Verification"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# PostgreSQL
if (echo > /dev/tcp/localhost/5432) 2>/dev/null; then
  ok "PostgreSQL    reachable on :5432"
else
  fail "PostgreSQL    not reachable on :5432  →  run: docker compose up -d postgres"
fi

# LocalStack
if curl -sf http://localhost:4566/_localstack/health > /dev/null 2>&1; then
  ok "LocalStack    reachable on :4566"
else
  fail "LocalStack    not reachable on :4566  →  run: docker compose up -d localstack"
fi

# SSM — connection string
if _aws ssm get-parameter --name "/bookingdojo/ConnectionStrings/BookingDojo" --with-decryption > /dev/null; then
  ok "SSM param     ConnectionStrings/BookingDojo present"
else
  fail "SSM param     ConnectionStrings/BookingDojo missing  →  run: bash scripts/setup.sh"
fi

# SSM — JWT secret
if _aws ssm get-parameter --name "/bookingdojo/BookingDojo/Jwt/Secret" --with-decryption > /dev/null; then
  ok "SSM param     BookingDojo/Jwt/Secret present"
else
  fail "SSM param     BookingDojo/Jwt/Secret missing  →  run: bash scripts/setup.sh"
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if [ "$FAIL" -eq 0 ]; then
  echo ""
  echo "  ✅  All checks passed — ready to use!"
  echo ""
  echo "  Start the stack:"
  echo "    Terminal 1:  cd src/BookingDojo.Api && dotnet run"
  echo "    Terminal 2:  cd src/BookingDojo.Bff && dotnet run"
  echo "    Terminal 3:  cd src/bookingdojo-ui  && npm run dev"
  echo ""
  echo "  Then open port 5173 in the Ports tab."
else
  echo ""
  echo "  ❌  $FAIL check(s) failed — see hints above."
  echo ""
fi

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
