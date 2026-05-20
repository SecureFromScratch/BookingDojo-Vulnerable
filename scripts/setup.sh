#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

echo "Waiting for PostgreSQL to be ready..."
for i in $(seq 1 60); do
  if (echo > /dev/tcp/localhost/5432) 2>/dev/null; then
    echo "PostgreSQL is ready."
    break
  fi
  [ "$i" -eq 60 ] && { echo "PostgreSQL did not start in time."; docker compose -f "$ROOT_DIR/docker-compose.yml" logs postgres | tail -20; exit 1; }
  echo "  Waiting... ($i/60)"
  sleep 3
done

echo "Waiting for LocalStack to be ready..."
for i in $(seq 1 60); do
  if curl -sf http://localhost:4566/_localstack/health > /dev/null 2>&1; then
    echo "LocalStack is ready."
    break
  fi
  [ "$i" -eq 60 ] && { echo "LocalStack did not start in time."; docker compose -f "$ROOT_DIR/docker-compose.yml" logs localstack | tail -30; exit 1; }
  echo "  Waiting... ($i/60)"
  sleep 3
done

echo "Seeding SSM parameters..."
_aws() {
  AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_DEFAULT_REGION=us-east-1 \
    aws --endpoint-url=http://localhost:4566 "$@"
}

_aws ssm put-parameter \
  --name "/bookingdojo/ConnectionStrings/BookingDojo" \
  --value "Host=localhost;Port=5432;Database=bookingdojo;Username=bookingdojo;Password=bookingdojo" \
  --type "SecureString" --overwrite > /dev/null

_aws ssm put-parameter \
  --name "/bookingdojo/BookingDojo/Jwt/Secret" \
  --value "BookingDojoWorkshopSecret2024ForJwtTokenGeneration!!" \
  --type "SecureString" --overwrite > /dev/null

echo "SSM parameters seeded."

echo "Running database setup via API startup..."
cd "$ROOT_DIR/src/BookingDojo.Api"

# Run the API briefly — it calls EnsureCreated + DataSeeder on startup, then exits.
ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --no-launch-profile -- --seed-and-exit 2>&1 | tail -20

echo "Database seeded successfully."
