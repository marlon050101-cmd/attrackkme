@echo off
echo Starting Server...
start cmd /k "cd ServerAtrrak && dotnet run"
timeout /t 5
echo Starting Frontend...
start cmd /k "cd Attrak && dotnet run"
echo Both applications started!
echo Server URL: https://localhost:7258
echo Frontend URL: http://localhost:7227
pause