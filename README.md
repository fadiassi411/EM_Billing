# BlackDog EM

BlackDog Energy — Watch Every Watt. Local web-based energy metering and shop billing system by MicroBrain, built with ASP.NET Core 8, Entity Framework Core, and SQLite.

## Customer installation

1. Download `BlackDog-EM-Setup-1.0.0-win-x64.exe` from the GitHub release.
2. Run the installer and approve the Windows administrator prompt.
3. Keep the default desktop shortcut selected.
4. Double-click **BlackDog EM**; the local service starts silently and opens the dashboard.
5. Register the first account, sign in, then visit `/Admin/Bootstrap` once to claim Administrator access.

The installer is self-contained; customers do not need to install .NET. Program files are installed under `Program Files\BlackDog EM`. The live database and backups are stored separately under `ProgramData\BlackDog EM`, and upgrades do not replace that customer data.

## Features

- Physical Modbus RTU communication over Windows serial ports
- Multiple controllers with up to 36 meter channels per controller
- Configurable register address, data type, word order, and scaling per meter
- Live dashboard with automatic refresh and communication status
- Independent tariff and ISO currency selection for every meter
- Shop management and meter commissioning
- Monthly billing data, invoice history, and professional PDF invoices
- Administrator-only controller configuration and audit history
- CSV reading export
- Guarded SQLite backup and restore
- Self-contained Windows x64 publishing

## Requirements

- Windows 10/11 or Windows Server x64 for physical serial communication
- A compatible USB-RS485 adapter and its Windows driver
- .NET 8 SDK only when building from source; self-contained published builds do not require .NET installation

## Run from source

```powershell
dotnet restore src/MallEnergyBilling.Web/MallEnergyBilling.Web.csproj
dotnet run --project src/MallEnergyBilling.Web --urls http://localhost:5080
```

Open `http://localhost:5080`. Register the first account, sign in, then visit `/Admin/Bootstrap` once to claim Administrator access.

## Build and test

```powershell
dotnet test tests/MallEnergyBilling.Tests/MallEnergyBilling.Tests.csproj --configuration Release
dotnet publish src/MallEnergyBilling.Web/MallEnergyBilling.Web.csproj --configuration Release --runtime win-x64 --self-contained true --output outputs/MallEnergyBilling-Production
```

## Typical Delta DDC configuration

The commissioned reference setup uses Modbus RTU, 9600 baud, even parity, 8 data bits, 1 stop bit, slave address 1, and Windows COM1. A Delta `D100/D101` UInt32 value is addressed as holding register `4196`, decoded `LowHigh`, and scaled by `0.01` for kWh. Always verify these values against the actual DDC program and Windows COM assignment.

## Data protection

Runtime databases and backups are intentionally excluded from Git. Do not commit `app.db`, backup files, customer details, account data, meter readings, or invoices. Use the Administrator backup and restore interface and store additional copies on protected external storage.

## Branding

BlackDog Energy — Watch Every Watt. By MicroBrain / Fadi Assi.
