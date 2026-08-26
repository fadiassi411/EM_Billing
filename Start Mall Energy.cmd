@echo off
title EM Billing
cd /d "%~dp0"
dotnet run --project "src\MallEnergyBilling.Web\MallEnergyBilling.Web.csproj" --urls "http://localhost:5080"
pause
