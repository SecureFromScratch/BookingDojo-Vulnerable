#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

echo "WARNING: This will drop and recreate the bookingdojo database."
read -rp "Are you sure? (yes/no): " confirm
if [[ "$confirm" != "yes" ]]; then
  echo "Aborted."
  exit 0
fi

echo "Dropping database..."
docker-compose -f "$ROOT_DIR/docker-compose.yml" exec -T postgres \
  psql -U bookingdojo -c "DROP DATABASE IF EXISTS bookingdojo;" postgres

echo "Creating database..."
docker-compose -f "$ROOT_DIR/docker-compose.yml" exec -T postgres \
  psql -U bookingdojo -c "CREATE DATABASE bookingdojo;" postgres

echo "Re-seeding..."
bash "$SCRIPT_DIR/setup.sh"

echo "Done."
