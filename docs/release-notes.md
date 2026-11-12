# BYD Tray 0.1.0-alpha.1

First experimental prerelease of an unofficial desktop tray app for international BYD accounts. Not affiliated with or endorsed by BYD.

Download the Windows x64 ZIP or Linux x64 tar archive and SHA256SUMS. Extract everything together and launch Byd.Tray.exe (Windows) or ./Byd.Tray (Linux). No separate .NET runtime/Python installation is needed. Windows binaries are unsigned; read the included Windows guide and release evidence.

Use `--demo` for a labelled offline preview. Start account tests with controls/polling/startup sign-in disabled, select the account country, sign in locally and refresh once. Sign-in may displace the mobile session; reads can wake the car. Saving credentials in the system vault is optional; PINs are never saved.

Includes Status, tray battery/charging/opening warnings, separate settings, explicit GPS/OpenStreetMap and opt-in confirmed controls. Uses the published [Byd.Client](https://github.com/craigchandler/byd-client-dotnet) NuGet package.

Live evidence is one Australian Atto 3 on the previous KDE build. Other models/regions/advanced controls are unverified. Read VALIDATION.md; cross-build/demo success is not Windows 11 vault/vehicle evidence.

Do not share credentials, PINs, tokens, VINs, raw messages, locations or private screenshots. Every command needs explicit local review/PIN; check the official app/vehicle after unknown outcomes before another attempt.

MIT app code and retained dependency licences/notices are included. Protocol reference credits include [jkaberg/pyBYD](https://github.com/jkaberg/pyBYD/tree/995ef70a2b6bd287d58c1f8564e6b50eabc7f868).
