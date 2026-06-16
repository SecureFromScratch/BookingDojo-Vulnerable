 @echo off
 echo =========================================
 echo Initializing Docker and Database Setup...
 echo =========================================
 
 echo.
 echo [1/2] Starting Docker Containers...
 docker compose up -d
 
 echo.
 echo Waiting 5 seconds for PostgreSQL to initialize...
 timeout /t 5 /nobreak
 
 echo.
 echo [2/2] Seeding the Database and LocalStack...
 :: Pointing Windows to the exact location of the Bash engine
 "C:\Program Files\Git\bin\bash.exe" -c "MSYS_NO_PATHCONV=1 bash scripts/setup.sh"
 
 echo.
 echo Setup complete! You can now run your launch-servers script.
 pause
 
