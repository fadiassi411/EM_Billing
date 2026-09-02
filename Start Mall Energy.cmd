@echo off
title Watch Dog EM - Watch Every Watt
cd /d "%~dp0"
echo Starting Watch Dog EM...
echo Keep this window open while using the software.
netstat -ano | findstr /R /C:":5080 .*LISTENING" >nul
if not errorlevel 1 (
    echo Watch Dog EM is already running. Opening it now...
    start "" "http://localhost:5080"
    exit /b 0
)
start "" powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 3; Start-Process 'http://localhost:5080'"
set "ASPNETCORE_ENVIRONMENT=Development"
dotnet run --configuration Release --no-build --no-restore --no-launch-profile --project "src\MallEnergyBilling.Web\MallEnergyBilling.Web.csproj" --urls "http://localhost:5080"
echo.
echo Watch Dog EM stopped or could not start. Review the message above.
pause
