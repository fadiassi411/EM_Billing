# Watchdog Energy Management V2.4.0 — BACnet/IP commissioning

This update keeps the application and installer version **2.4.0**. It extends the existing system; Modbus TCP/RTU, SQLite history, tariffs, invoices, SMTP, licensing, users, grid/generator and water features remain in place. This repository implements SQLite, not SQL Server.

## Upgrade an existing installation

1. Make a database backup using the existing Administrator backup page. Keep a protected copy of the customer data directory before installing.
2. Install the updated V2.4.0 package. The existing installation identity and `C:\ProgramData\Watch Dog EM` data location are retained.
3. Startup applies `20260909061947_BacnetMeterPoints`. This adds point configuration/quality, controller diagnostics and a counter-review flag, and makes Device Instance nullable. It does not delete existing customer records, billing, SMTP configuration, users or licenses.
4. Existing Modbus meters retain their configuration. Legacy BACnet object mappings and scaling are copied into point records as **Unmapped**, with billing confirmation off. Open each existing BACnet meter's **BACnet Points**, verify its object/unit/scale, select **TotalImportEnergy**, and explicitly confirm billing. Previously configured instance **0 stays 0**; it is not changed to unknown. New devices may leave the instance blank until discovery.
5. BACnet billing stays unavailable until its cumulative energy mapping is confirmed. Existing invoice/history records are retained.

## First physical meter — step by step

1. Connect the Watchdog PC and meter to the same IPv4 LAN/subnet. For example, PC `192.168.1.20` and meter `192.168.1.50`; use your actual addressing and subnet mask.
2. Enable BACnet/IP on the meter, set its IP and UDP port (normally **47808**), and determine its Device Instance. Normal configured instances are **0–4194302**; 4194303 is reserved. Zero is valid.
3. Open **Settings > Controllers > Add controller**. Name the device and select **BACnet/IP (UDP)**. Slave/unit, Modbus registers, byte/word order and serial fields are hidden.
4. Select the local adapter connected to the BACnet LAN. The dropdown lists IPv4 adapters on the **server**, even when the browser is on another PC. Nothing is automatically selected. VPN/virtual/loopback adapters are marked; choose one only intentionally.
5. Set the BACnet UDP port, default polling **5 seconds**, timeout **1000 ms**, retries **2**. Keep the device disabled until commissioning is finished.
6. Click **Discover BACnet Devices**. Who-Is is transmitted and I-Am responses are listed with instance, address, port, vendor and available name/model. Click **Select** to populate the fields. Discovery is optional: enter the meter IP/hostname and known Device Instance manually if broadcasts are blocked.
7. Click **Test BACnet Connection**. This performs BACnet Device Object reads and checks the returned object identifier. It does not use ping as proof of communication. Review the reported name/instance/address, then save.
8. Create a shop if needed. On the controller's channels page create the physical meter entry, set its real serial number/shop/grid-or-generator assignment, and open **BACnet Points**.
9. Click **Browse BACnet Objects**. Watchdog reads Object_List, falling back to indexed reads when the whole array cannot be read. The table shows object name, description, Present_Value, units, reliability and status flags where available. Large/restricted lists can be configured manually instead.
10. Select the cumulative import-energy object from the actual meter documentation. It can be an Analog Input, Analog Value, Accumulator or another supported numerical object. **Do not copy example object instances without checking your meter.** Select/Map fills the object fields but leaves the measurement **Unmapped** and billing confirmation off.
11. Set the measurement to **TotalImportEnergy**, property **85 (Present_Value)**, final unit **kWh**, scaling **1** and offset **0** unless the manufacturer's units require a deliberate conversion. For a raw Wh counter use scale **0.001** to obtain kWh. Check the explicit billing-confirmation box only after verifying this is cumulative imported energy, not instantaneous kW, exported energy or a resettable period counter.
12. Click **Test Read** and compare raw and scaled readings with the meter display. Test Read reports object/property, raw value, final value/unit, quality and timestamp. Save the point.
13. Add other points as needed: phase/line voltages, currents, phase/total active power, reactive/apparent power, phase/total power factor, frequency, export/reactive energy, demand and meter status. Each point has independent mapping, scale, offset and enabled state. A meter may have only one enabled point for a given mapped measurement.
14. Mark the meter **Commissioned and active**, enable the controller and save. Polling runs in the background and resumes after service/Windows restart without opening a browser.
15. Confirm the cumulative reading, Online status and fresh timestamp on the existing dashboard and grid/generator page. **View meter** shows all configured measurements and their quality. History and billing consume the same cumulative-energy MeterReading records used by the existing system; instantaneous kW is never integrated for BACnet billing.
16. Disconnect the meter. Confirm Stale/Timeout, an alarm count and an unchanged last-success timestamp; no fresh billing readings should be created. Reconnect and confirm automatic recovery. Verify the site's existing Modbus meters continue polling.

## Data quality and billing

- Numerical Real, Double, signed/unsigned integers, appropriate enumerations and booleans are converted safely. Null, arrays, text, NaN, infinity and overflow produce a bad point. Conversion is exactly `raw × scale + offset`.
- Missing optional metadata does not prevent reading a supported value. Reported reliability faults or active status flags conservatively mark a point bad. Last known values retain their original successful timestamp.
- A good point becomes Stale for display after three polling intervals (minimum 15 seconds). Disabled devices/meters are displayed as Disabled. Failed energy reads do not update the meter's cumulative timestamp or write a successful history sample.
- History retains the application's approximately 15-minute sampling interval. BACnet invoice generation requires a good sample near the period end (16 minutes plus one poll interval), so an outage cannot silently substitute a much older closing reading.
- Counter decreases/negative cumulative readings create a review record and latch **Counter Review**, retaining the previous accepted cumulative reading. Billing is suspended for that meter. This update does not guess rollover size, reset the physical meter, or automatically clear review flags. Have the installer/billing administrator investigate a reset, replacement or incorrect mapping and reconcile the history before resuming billing; there is no automatic counter-rebase workflow.
- Network and point status changes are logged; identical failures are not written at normal level every poll. Protocol diagnostics are available at Debug level in the existing log system. Successful polls are not logged at Info level.

## Network troubleshooting

- The selected adapter must be up and own the selected IPv4 address. A hostname must resolve to IPv4. Save an explicit interface; Wi-Fi/VPN route changes must not silently choose another interface.
- The installer adds an application-scoped inbound UDP **47808** rule for **Private/Domain** profiles and **LocalSubnet**. A nonstandard UDP port needs an equivalent site firewall rule for that port. Public-profile networks and routed BACnet traffic are outside this default rule.
- Another application exclusively using the selected interface/port can prevent startup. Close the conflicting BACnet tool or coordinate its port assignment; changing only Watchdog's target port without changing the meter will not work.
- If discovery finds nothing, verify the adapter, subnet and UDP port, then try manual device entry. Ping alone does not validate BACnet.
- Check the meter's Device Instance, supported object/property and read permissions when receiving BACnet Error/Reject/Abort. A mismatched Device Object response fails the connection test. Timeouts on a wrong Device Instance may be indistinguishable from an unreachable device; discovery can help confirm its identity.
- This release targets normal same-subnet BACnet/IP. The transport abstraction isolates network addressing for future BBMD/foreign-device support; no BBMD registration UI is provided.
- Object browsing is capped at 4096 objects and a two-minute UI operation limit; use manual point entry for devices with huge/slow lists. RPM failure falls back to individual reads; polling retries RPM after ten minutes. Each device has one active poll and up to eight devices poll concurrently, independently from Modbus.

## Dependency and verification

No NuGet dependency was added. The existing **BACnet 4.0.0** package remains pinned, using its net8.0 build and asynchronous APIs. The stack is maintained at [ela-compil/BACnet](https://github.com/ela-compil/BACnet) and distributed on [NuGet](https://www.nuget.org/packages/BACnet/4.0.0). Third-party calls are confined to `BacnetTransport`; pages and polling use `IBacnetIpService` / `IBacnetTransport`.

Automated verification covers numeric conversion, object parsing/ranges, instance zero/unknown, explicit energy mapping, scaling/offset, invalid values, RPM/fallback, indexed Object_List, status faults, timeout/stale/recovery, counter review, SQLite migration and preserved Modbus configuration. Tests include actual loopback UDP exchanges with a **test-only** peer; production uses the real BACnet stack and includes no simulator.

Browser QA additionally verified discovery selection, manual instance-zero configuration, connection timeouts, object browsing, real UDP test reads, confirmation enforcement, dashboard propagation and disconnection/reconnection. **Physical-meter LAN interoperability is still an on-site acceptance test**, not a claim established by a loopback peer.

Build individual projects with .NET 8. The repository's existing `.slnx` solution format needs a newer SDK/MSBuild; the installed .NET 10 SDK can build the complete solution from a working directory outside the repository's .NET 8 `global.json` scope. The application still targets .NET 8.
