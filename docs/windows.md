# Windows 11 setup and checks

Extract the complete portable Windows x64 ZIP, then run `Byd.Tray.exe` as your normal user. It targets 64-bit Intel/AMD PCs. Do not launch from inside the ZIP or copy the executable without its supporting files. No .NET/Python installation is needed on the target PC.

## Verify and preview

Compare the archive against the release's SHA256SUMS or local `.sha256` sidecar:

```powershell
Get-FileHash .\BydTray-0.1.0-alpha.1-win-x64.zip -Algorithm SHA256
```

The build is **unsigned**: no Authenticode certificate/publisher verification is configured. Windows may show SmartScreen or reputation warnings. A checksum checks file integrity against the release, not the publisher's identity. Review the source/release evidence and decide whether you trust it; keep Windows security protections enabled.

Run `./Byd.Tray.exe --demo` from PowerShell in the extracted folder. Check the labelled synthetic Status view, tray menu, hover and Settings & advanced. Demo does not load real startup preferences, read the wallet, authenticate, request GPS, fetch map tiles or send commands. Close/reopen the window through the tray, then choose Quit. The icon may be in notification-area overflow.

## First account check

1. Launch normally, select the account's registered country, and leave controls, startup sign-in and polling off.
2. Enter credentials only in the local window, sign in once and refresh once. Compare with the official app. Sign-in may invalidate its session; reads may wake the car.
3. If you choose account saving, test save → quit → reopen → Use saved account → Forget saved account. The country-scoped generic credential belongs to your Windows user on this computer. There is no plaintext fallback. Vault round-trips are separate runtime checks.
4. Test controls only after separate approval of an exact action. Enter the PIN locally per command. After an unknown outcome, check the official app/vehicle before another attempt.

Report OS/architecture/app version and safe pass/fail/error categories. Never share account identifiers, passwords, PINs, tokens, VINs, raw responses, locations or unredacted screenshots.

## Optional login startup

Use Win+R → `shell:startup`, then create a shortcut to the extracted executable with `--start-in-tray`. Keep its folder stable and update the shortcut when moving it. Remove the shortcut to disable login startup. An interactive tray app should not run as a Windows Service.

Startup and BYD sign-in are independent. **Sign in automatically at startup (system wallet)** defaults off and permits one saved-account sign-in/status attempt. No automatic authentication retry, restored polling, GPS or control opt-in occurs.

## Updating and removal

Quit through the tray, extract a new version to a new folder and update your shortcut. No updater runs. Removing the app folder/startup shortcut removes the binaries/startup entry. First choose **Forget saved account** if its vault entry should also be removed. Non-secret preferences and fixed-event logs remain under `%LOCALAPPDATA%\BydTray`; delete that folder separately if desired.

Windows CI checks a responding labelled demo window. That does not prove Windows 11 tray/hover, Credential Manager or real account/vehicle compatibility. Wine is not Windows 11 evidence either.
