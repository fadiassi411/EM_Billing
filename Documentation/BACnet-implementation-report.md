# V2.4.0 BACnet/IP implementation report

Application and installer version: **2.4.0** (unchanged).

## Result

Implemented real BACnet/IP discovery, Device Object verification, Object_List browsing, per-meter point CRUD, numeric conversion with scale/offset, RPM with ReadProperty fallback, independent background polling, persisted point quality/timestamps and confirmed cumulative-energy integration with the existing dashboard/history/billing paths.

## Verification

- Complete solution build, including Windows license tools: succeeded, zero warnings/errors.
- Final automated suite: **71 passed, 0 failed, 0 skipped**, including all 32 original tests.
- BACnet UDP integration: real loopback Device Object/ReadProperty/ReadPropertyMultiple exchanges, numeric reading and timeout.
- SQLite upgrade test: legacy BACnet object/scale and actual device instance 0 retained; legacy energy mapping requires explicit confirmation; existing Modbus serial/register/scale and readings retained; no pending model changes.
- Polling integration: good cumulative energy writes existing history; failed energy preserves previous timestamp; another point continues; reconnect recovers; counter decrease latches review without repeatedly inserting review rows.
- Browser QA in an isolated test database: protocol switching hides Modbus fields; adapter selection; blank instance versus zero; real timeout feedback; I-Am discovery/selection; object browse and metadata; billing confirmation enforcement; real Test Read of 18532.6 kWh; existing dashboard shows Online, then Stale/alarm on peer shutdown, then Online on restart.
- Inno Setup compilation: successful. Self-contained Windows x64 package includes no customer database or license file.
- Physical-meter/subnet acceptance remains an on-site test; loopback peer testing is not a substitute for testing the installed meter.

## Database migration

**20260909061947_BacnetMeterPoints** plus its EF designer and model snapshot. Adds BacnetPoints, controller diagnostic fields, nullable Device Instance and meter counter-review state. Legacy BACnet mappings are copied without guessing billing consent. SQLite support is retained; this project had no SQL Server provider to preserve.

## Dependencies

No new NuGet package. Existing **BACnet 4.0.0** retained behind IBacnetTransport/IBacnetIpService; application target remains net8.0. Self-contained publishing uses the installed .NET 8 SDK/runtime patch. Installer version stays 2.4.0.

## Commissioning

See [BACnet-IP-commissioning.md](BACnet-IP-commissioning.md) for the full first-meter workflow, firewall/custom-port requirements, upgrade behavior, data-quality and counter-review handling. Confirm the cumulative kWh point explicitly before enabling a meter. No instantaneous-power integration or physical meter writes are used.

## Files changed
- Documentation/BACnet-implementation-plan.md
- Documentation/BACnet-IP-commissioning.md
- installer/Install Watch Dog EM Service.ps1
- installer/Remove Watch Dog EM Service.ps1
- README.md
- src/MallEnergyBilling.Web/Data/ApplicationDbContext.cs
- src/MallEnergyBilling.Web/Data/Migrations/20260909061947_BacnetMeterPoints.cs
- src/MallEnergyBilling.Web/Data/Migrations/20260909061947_BacnetMeterPoints.Designer.cs
- src/MallEnergyBilling.Web/Data/Migrations/ApplicationDbContextModelSnapshot.cs
- src/MallEnergyBilling.Web/Models/BacnetPoint.cs
- src/MallEnergyBilling.Web/Models/Domain.cs
- src/MallEnergyBilling.Web/Pages/Admin/Controllers/Channels.cshtml
- src/MallEnergyBilling.Web/Pages/Admin/Controllers/Channels.cshtml.cs
- src/MallEnergyBilling.Web/Pages/Admin/Controllers/Edit.cshtml
- src/MallEnergyBilling.Web/Pages/Admin/Controllers/Edit.cshtml.cs
- src/MallEnergyBilling.Web/Pages/Admin/Controllers/Index.cshtml
- src/MallEnergyBilling.Web/Pages/Admin/Controllers/Meter.cshtml
- src/MallEnergyBilling.Web/Pages/Admin/Controllers/Meter.cshtml.cs
- src/MallEnergyBilling.Web/Pages/Admin/Controllers/Points.cshtml
- src/MallEnergyBilling.Web/Pages/Admin/Controllers/Points.cshtml.cs
- src/MallEnergyBilling.Web/Pages/Index.cshtml
- src/MallEnergyBilling.Web/Pages/Meters/Details.cshtml
- src/MallEnergyBilling.Web/Pages/Meters/Details.cshtml.cs
- src/MallEnergyBilling.Web/Pages/Operations/Index.cshtml.cs
- src/MallEnergyBilling.Web/Pages/Shared/_PowerSourceMeters.cshtml
- src/MallEnergyBilling.Web/Program.cs
- src/MallEnergyBilling.Web/Services/BacnetBillingGuard.cs
- src/MallEnergyBilling.Web/Services/BacnetIpService.cs
- src/MallEnergyBilling.Web/Services/BacnetPollingService.cs
- src/MallEnergyBilling.Web/Services/BacnetTransport.cs
- src/MallEnergyBilling.Web/Services/InvoiceSchedulerService.cs
- src/MallEnergyBilling.Web/Services/MeterPollingService.cs
- src/MallEnergyBilling.Web/wwwroot/css/admin.css
- src/MallEnergyBilling.Web/wwwroot/js/bacnet-commissioning.js
- tests/MallEnergyBilling.Tests/BacnetMigrationTests.cs
- tests/MallEnergyBilling.Tests/BacnetPollingTests.cs
- tests/MallEnergyBilling.Tests/BacnetTests.cs
- tests/MallEnergyBilling.Tests/BacnetUdpTests.cs
- Documentation/BACnet-implementation-report.md
