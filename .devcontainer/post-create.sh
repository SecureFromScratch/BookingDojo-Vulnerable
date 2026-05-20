#!/usr/bin/env bash
set -euo pipefail

echo "============================================"
echo " BookingDojo Workshop — Post-Create Setup   "
echo "============================================"

echo ""
echo "[0/5] Installing AWS CLI..."
APT_CMD="apt-get"
command -v sudo &>/dev/null && APT_CMD="sudo apt-get"
$APT_CMD update -qq && $APT_CMD install -y -qq awscli

# Wait for Docker daemon to be ready and able to pull images
echo "[1/5] Waiting for Docker daemon..."
until docker info > /dev/null 2>&1; do sleep 1; done
# The daemon answers docker info before its network stack is ready — give it a moment
sleep 3

# Start PostgreSQL and LocalStack via docker-compose (retry on transient EOF)
echo "[2/5] Starting PostgreSQL and LocalStack..."
for attempt in 1 2 3 4 5; do
  docker compose up -d postgres localstack && break
  echo "  Attempt $attempt failed, retrying in 5s..."
  sleep 5
done

echo "[3/5] Restoring .NET packages..."
dotnet restore BookingDojo.sln

echo "[4/5] Installing UI dependencies..."
cd src/bookingdojo-ui
npm install
cd -

echo "[5/5] Seeding database..."
bash scripts/setup.sh

echo ""
echo "============================================"
echo " Setup complete!"
echo ""
echo " To start the application:"
echo "   Terminal 1: cd src/BookingDojo.Api  && dotnet run"
echo "   Terminal 2: cd src/BookingDojo.Bff  && dotnet run"
echo "   Terminal 3: cd src/bookingdojo-ui   && npm run dev"
echo ""
echo " Default credentials:"
echo "   admin   / Admin1234!   (AdminUser)"
echo "   partner / Partner1234! (PartnerUser)"
echo "   support / Support1234! (SupportUser)"
echo "============================================"
