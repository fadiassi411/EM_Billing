# Watchdog License Manager

Private Windows desktop application used by MicroBrain to issue offline Watchdog Energy Management licenses.

## Use

1. Copy the Installation ID from the customer's **Settings → License** page.
2. Open **Watchdog License Manager**.
3. Enter the customer name and paste the Installation ID.
4. Select the licensed meter capacity in blocks of 50.
5. Leave expiry disabled for a permanent license, or select an expiry date.
6. Confirm the private-key and output-folder paths.
7. Select **Generate signed license**.
8. Send only the generated `.wdlicense` file to the customer.

The application remembers only the key path and output-folder path. It never stores the private-key contents in its settings.

## Install on another trusted computer

Run `Watchdog-License-Manager-Setup-1.0.0-win-x64.exe`. The installer creates Start Menu and optional Desktop shortcuts. After installation, copy `watchdog-license-private.pem` and `watchdog-license-public.pem` to a secure local folder on the trusted licensing computer. Open the application, press **Browse** beside **Private key**, and select the private PEM file. The application remembers that location for later use.

The installer intentionally does not include either key. Never publish a private-key file in a GitHub release or send it to a customer.

## Security

Keep `watchdog-license-private.pem` on the protected MicroBrain licensing PC. Never distribute it, commit it, or include it in the customer installer. The published desktop executable belongs under the ignored `private-license-authority` directory so it can find the protected key one directory above it.

## Publish

```powershell
dotnet publish tools\WatchdogLicenseManager\WatchdogLicenseManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o private-license-authority\WatchdogLicenseManager
```

Then compile `installer\WatchdogLicenseManager.iss` with Inno Setup 6.
