#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

echo "Waiting for PostgreSQL to be ready..."
for i in $(seq 1 30); do
  if docker-compose -f "$ROOT_DIR/docker-compose.yml" exec -T postgres \
      pg_isready -U bookingdojo -d bookingdojo > /dev/null 2>&1; then
    echo "PostgreSQL is ready."
    break
  fi
  echo "  Waiting... ($i/30)"
  sleep 2
done

echo "Running database setup via API startup..."
cd "$ROOT_DIR/src/BookingDojo.Api"

# Run the API briefly — it calls EnsureCreated + DataSeeder on startup, then exits.
ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --no-launch-profile -- --seed-and-exit 2>&1 | tail -20

echo "Database seeded successfully."
