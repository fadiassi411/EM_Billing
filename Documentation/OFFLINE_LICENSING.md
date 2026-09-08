# Watchdog Energy Management offline licensing

Watchdog uses ECDSA P-256 digital signatures. The installed application contains only the public verification key. The private signing key must remain with MicroBrain and must never be uploaded to GitHub, copied into an installer, emailed, or given to a customer.

## Commercial rules

- Free mode permits 5 active meters.
- Paid licenses are issued in blocks of 50 active meters: 50, 100, 150, and so on.
- One license applies to the main Watchdog PC. LAN browser clients do not need separate licenses.
- Licenses may be permanent or have an optional expiry date.
- Existing customer data is never deleted or hidden. When the allowance is reached, Watchdog blocks activation of another meter.

## First-time key creation

The production key pair has already been initialized locally under the ignored `private-license-authority` folder. Back up the private PEM file in at least two encrypted, access-controlled locations. Losing it means no future license can be issued for this public key. Anyone who obtains it can create unauthorized licenses.

To initialize a new authority only when intentionally replacing the licensing key:

```powershell
dotnet run --project tools/WatchdogLicenseGenerator -- keygen --private private-license-authority/watchdog-license-private.pem --public private-license-authority/watchdog-license-public.pem
```

Changing the embedded public key would invalidate previously issued licenses, so do not regenerate production keys during ordinary releases.

## Issue a customer license

1. Ask the customer to open **Settings → License**.
2. Copy the Installation ID shown on that page.
3. Run:

```powershell
dotnet run --project tools/WatchdogLicenseGenerator -- issue --private private-license-authority/watchdog-license-private.pem --customer "Customer Mall Ltd" --installation "WD-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX" --meters 50 --edition Commercial --out private-license-authority/issued/customer-mall.wdlicense
```

For a time-limited license, add `--expires 2027-12-31`.

4. Send only the generated `.wdlicense` file to the customer.
5. The customer imports it from **Settings → License**.

Optionally verify a generated file before sending it:

```powershell
dotnet run --project tools/WatchdogLicenseGenerator -- verify --public private-license-authority/watchdog-license-public.pem --license private-license-authority/issued/customer-mall.wdlicense
```

## Transfer to another PC

A license is bound to one Windows installation. On replacement hardware, obtain the new Installation ID and issue a new license. Keep a record of the old and replacement license IDs. Removing a license does not remove customer data.
