 @echo off
 echo Launching BookingDojo Servers...
 
 :: Launch API
 start "" "C:\Program Files\Git\git-bash.exe" -c "title API && echo 'Starting API...'; dotnet run --project src/BookingDojo.Api; exec bash"
 
 :: Launch BFF
 start "" "C:\Program Files\Git\git-bash.exe" -c "title BFF && echo 'Starting BFF...'; dotnet run --project src/BookingDojo.Bff; exec bash"
 
 :: Launch UI
 start "" "C:\Program Files\Git\git-bash.exe" -c "title UI && echo 'Starting UI...'; cd src/bookingdojo-ui && npm run dev; exec bash"
 
 echo Done! The windows should now be opening.
