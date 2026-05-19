#!/usr/bin/env bash
set -euo pipefail

echo "============================================"
echo " BookingDojo Workshop — Post-Create Setup   "
echo "============================================"

# Start PostgreSQL and LocalStack via docker-compose
echo ""
echo "[1/4] Starting PostgreSQL and LocalStack..."
docker-compose up -d postgres localstack

echo "[2/4] Restoring .NET packages..."
dotnet restore BookingDojo.sln

echo "[3/4] Installing UI dependencies..."
cd src/bookingdojo-ui
npm install
cd -

echo "[4/4] Seeding database..."
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
