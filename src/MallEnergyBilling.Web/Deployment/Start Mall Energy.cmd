@echo off
title Mall Energy Metering and Billing
cd /d "%~dp0"
echo Starting Mall Energy...
echo Keep this window open while using the software.
start "" powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 2; Start-Process 'http://localhost:5080'"
MallEnergyBilling.Web.exe --urls "http://localhost:5080"
echo Mall Energy has stopped.
pause
