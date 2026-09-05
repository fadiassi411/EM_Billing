# Watch Dog EM

Watch Dog EM — Watch Every Watt. Local web-based energy metering and shop billing system by MicroBrain, built with ASP.NET Core 8, Entity Framework Core, and SQLite.

## Customer installation

1. Download the Watch Dog EM v1.8.2 Windows package from the latest GitHub release.
2. Run the installer and approve the Windows administrator prompt.
3. Keep the default desktop shortcut selected.
4. The **Watch Dog EM Server** Windows Service starts automatically. Double-click **Watch Dog EM** to open the dashboard.
5. Sign in with the Administrator username and password created during initial setup.

The installer is self-contained; customers do not need to install .NET. Program files are installed under `Program Files\Watch Dog EM`. The live database and backups are stored separately under `ProgramData\Watch Dog EM`, and upgrades do not replace that customer data. Existing version 1.0 customer data is migrated automatically on first start.

### Unattended operation

- Locking Windows with **Win + L** is safe; the server continues running as a Windows Service.
- Set Windows **Sleep** to **Never when plugged in**. Sleep suspends the USB/RS-485 adapter and no readings can be collected while the computer sleeps.
- Windows may turn off the display; display-off does not stop metering.
- Keep the laptop connected to reliable AC power and configure Windows Update active hours/restart notifications.
- Service diagnostics are retained for 30 days in `C:\ProgramData\Watch Dog EM\Logs`.
- In Windows Services, **Watch Dog EM Server** should show `Running` and `Automatic (Delayed Start)`. The installer configures three automatic restart attempts after failures.

## Features

- Selectable Modbus RTU over Windows serial ports or Modbus TCP/IP over Ethernet
- Multiple controllers with up to 45 meter channels per controller
- Configurable register address, data type, word order, and scaling per meter
- Live dashboard with automatic refresh and communication status
- Independent tariff and ISO currency selection for every meter
- One-form tariff and monthly invoice publication settings
- Recurring invoice publication on a selectable day from the 1st through the 7th
- Searchable published-invoice archive by date, shop, tenant, meter, or invoice number
- Collapsible meter register and simplified Main/Settings navigation
- Shop management and meter commissioning
- Monthly billing data, invoice history, and professional PDF invoices
- Optional SMTP delivery of published PDF invoices, disabled by default
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

Open `http://localhost:5080` and sign in with an Administrator account to configure controllers, meters, users, tariffs, and billing.

## Optional invoice email

An Administrator can open **Settings > Invoice email**, enter the customer's SMTP server details, send a test email, and enable the feature. Published invoices then have an **Email PDF invoice** action that uses the shop email address by default. A separate global switch can automatically email every newly published invoice at period end; disabling that switch cancels unsent automatic deliveries and leaves manual sending available. Delivery is attempted up to five times at 15-minute intervals, and results are retained in the audit trail. SMTP passwords are encrypted with Windows data protection and are never displayed again or written to the audit log. For hosted email services, use the provider's SMTP app password when required.

## Build and test

```powershell
dotnet test tests/MallEnergyBilling.Tests/MallEnergyBilling.Tests.csproj --configuration Release
dotnet publish src/MallEnergyBilling.Web/MallEnergyBilling.Web.csproj --configuration Release --runtime win-x64 --self-contained true --output outputs/MallEnergyBilling-Production
```

## Typical Delta DDC configuration

The commissioned reference setup uses Modbus RTU, 9600 baud, even parity, 8 data bits, 1 stop bit, slave address 1, and Windows COM1. A Delta `D100/D101` UInt32 value is addressed as holding register `4196`, decoded `LowHigh`, and scaled by `0.01` for kWh. Always verify these values against the actual DDC program and Windows COM assignment.

For Modbus TCP/IP, select **Modbus TCP/IP (Ethernet)** on the controller page, enter the controller or gateway IP address, TCP port (normally `502`), and the slave/unit address. Meter register addresses, data types, word order, scaling, polling interval, timeout, and retry settings work the same way for both transports.

## Data protection

Runtime databases and backups are intentionally excluded from Git. Do not commit `app.db`, backup files, customer details, account data, meter readings, or invoices. Use the Administrator backup and restore interface and store additional copies on protected external storage.

## Branding

Watch Dog EM — Watch Every Watt. By MicroBrain / Fadi Assi.
