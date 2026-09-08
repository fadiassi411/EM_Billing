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

## Security

Keep `watchdog-license-private.pem` on the protected MicroBrain licensing PC. Never distribute it, commit it, or include it in the customer installer. The published desktop executable belongs under the ignored `private-license-authority` directory so it can find the protected key one directory above it.

## Publish

```powershell
dotnet publish tools\WatchdogLicenseManager\WatchdogLicenseManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o private-license-authority\WatchdogLicenseManager
```
