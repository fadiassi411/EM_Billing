# Implementation plan
1. Establish an ASP.NET Core 8 Razor Pages solution, SQLite/EF Core migrations and Identity.
2. Model controllers, shops, meters, readings, tariff versions, periods, invoices, payments and audit records.
3. Run a browser-independent simulation polling service with failure, freeze, reset, load, spike and manual-energy controls.
4. Provide responsive dashboard and protected operations views, CSV export and local database backup.
5. Isolate decimal billing and Modbus register conversion logic and cover it with automated tests.
6. Publish a Windows x64 deployment and document commissioning, security, backup and hardware-pending work.

## Delivery boundary
This build is a safe simulation-first foundation. Physical serial I/O, production-grade PDF generation, full CRUD workflows, credit notes and restore UI remain explicitly hardware/phase-two pending; they are not represented as validated.
