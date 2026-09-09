# BACnet/IP implementation — V2.4.0

Architecture inspected: ASP.NET Core 8 Razor Pages, SQLite EF Core migrations, Controller network configuration, Meter cumulative billing source, MeterPollingService, ModbusService, invoice scheduler/manual invoice generation, meter/history/dashboard/grid/generator views, daily logging, DI and 32 baseline tests. No SQL Server provider or separate instantaneous-measurement model exists.

1. Retain BACnet 4.0.0 and version 2.4.0. Isolate its API behind a transport and application service.
2. Add nullable device instance, per-meter configured numerical points and persisted point quality/status. Migrate legacy BACnet mappings without guessing billing confirmation. Preserve Modbus configuration and all history.
3. Implement selected-interface Who-Is/I-Am, Device validation, Object_List (indexed fallback), property metadata and batched RPM with individual fallback.
4. Add administrator discovery, diagnostics, point CRUD and real test reads using existing Razor styling. Require explicit import-energy confirmation.
5. Poll BACnet independently with bounded concurrency, isolate failed points, retain stale timestamps, and route confirmed cumulative kWh to existing MeterReading/billing. Flag decreases for review.
6. Test conversion, configuration, transport fallback, quality, migration, billing guard and real UDP exchanges using a test-only peer. Build all projects and run existing Modbus regressions. Document physical-meter commissioning; physical LAN acceptance remains an on-site test.
