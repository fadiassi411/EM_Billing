@echo off
title Black Dog - Watch Every Watt
cd /d "%~dp0"
echo Starting Black Dog...
echo Keep this window open while using the software.
start "" powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 2; Start-Process 'http://localhost:5080'"
MallEnergyBilling.Web.exe --urls "http://localhost:5080"
echo Black Dog has stopped.
pause
