@echo off
title Watchdog Energy Management - Watch Every Watt
cd /d "%~dp0"
echo Starting Watchdog Energy Management...
echo Keep this window open while using the software.
start "" powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 2; Start-Process 'http://localhost:5080'"
MallEnergyBilling.Web.exe --urls "http://0.0.0.0:5080"
echo Watchdog Energy Management has stopped.
pause
