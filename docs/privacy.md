# Privacy and security boundaries

Credentials go to the selected account country's BYD cloud endpoints through the unofficial Byd.Client protocol. [The library privacy guide](https://github.com/craigchandler/byd-client-dotnet/blob/v0.1.0-alpha.1/docs/privacy.md) describes HTTP/TLS MQTT requests and in-memory sessions. There is no automatic login retry or command replay. Changing country clears account/vehicle data and control permission.

Optional saving uses KWallet 6 or Windows Credential Manager. Entries are country-scoped, with the legacy AU entry retained. Windows uses a user-owned generic credential with local-machine persistence: it remains available to that user's later logins on that computer. Vault failures do not create plaintext credential files. Managed strings cannot be guaranteed erased; PINs are never saved.

Non-secret `preferences.json` stores only country and the startup-sign-in checkbox. Fresh profiles have startup sign-in disabled. Polling, GPS and control permission are not restored. Closing the window leaves the app running; quit through the tray.

Logs accept UTC timestamps and allow-listed event names only, rotating after 128 KiB. No exception text, account data, telemetry or location is logged. Paths: `%LOCALAPPDATA%\BydTray\events.log` on Windows, and .NET's local application-data directory plus `BydTray/events.log` on Linux. Demos/headless tests suppress audit writes. No analytics or crash-report uploader is installed.

GPS is requested explicitly. Only clicking **Load OpenStreetMap** enables `tile.openstreetmap.org` requests; the provider receives the viewed area and your IP address, not BYD credentials/identifiers. Coordinates/tile caches stay in memory, with no history, reverse geocoding, bulk download or disk cache. [OSMF privacy policy](https://osmfoundation.org/wiki/Privacy_Policy).

Sign-in can displace the mobile session; reads may wake the car. Start with manual refresh. Optional fifteen-minute polling pauses after failures; no universal safe polling rate is established. Every command requires an exact confirmation/PIN. Vehicle-stored schedules may run after exit.

Reports must use synthetic/redacted data. Never upload vault contents, raw HTTP/MQTT payloads, memory dumps, locations or private screenshots. See [security reporting](../SECURITY.md).
