@echo off
echo Starting Server...
cd ServerAtrrak
start "Server" cmd /k "dotnet run"
timeout /t 3
echo Starting Frontend...
cd ..\Attrak
start "Frontend" cmd /k "dotnet run"
echo Both applications are starting...
pause
