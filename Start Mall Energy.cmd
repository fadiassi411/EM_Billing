@echo off
title Black Dog - Watch Every Watt
cd /d "%~dp0"
echo Starting Black Dog...
echo Keep this window open while using the software.
netstat -ano | findstr /R /C:":5080 .*LISTENING" >nul
if not errorlevel 1 (
    echo Black Dog is already running. Opening it now...
    start "" "http://localhost:5080"
    exit /b 0
)
start "" powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 3; Start-Process 'http://localhost:5080'"
dotnet run --no-build --no-restore --project "src\MallEnergyBilling.Web\MallEnergyBilling.Web.csproj" --urls "http://localhost:5080"
echo.
echo Black Dog stopped or could not start. Review the message above.
pause
