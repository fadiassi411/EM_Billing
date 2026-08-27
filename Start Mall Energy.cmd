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
dotnet run --no-build --no-restore --project "src\MallEnergyBilling.Web\MallEnergyBilling.Web.csproj" --urls "http://localhost:5080"
echo.
echo Watch Dog EM stopped or could not start. Review the message above.
pause
