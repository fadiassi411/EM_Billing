@echo off
title Watch Dog EM - Watch Every Watt
cd /d "%~dp0"
echo Starting Watch Dog EM...
echo Keep this window open while using the software.
start "" powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 2; Start-Process 'http://localhost:5080'"
MallEnergyBilling.Web.exe --urls "http://localhost:5080"
echo Watch Dog EM has stopped.
pause
