# Usage and advanced features

## Status and advanced views

Account & startup includes a **country / server region selector**, populated from the library's discovery API. The versioned catalog covers 101 countries across 16 international server regions. Choose the country where the account was registered; server and API language follow its profile. Only listed international email-login accounts are covered; mainland China is outside this catalog. Region routing/signing is tested offline; live evidence remains Australian only.

Changing country persists the selection, signs out, clears account/vehicle data and turns polling/controls off. It does not authenticate or load another country's wallet entry automatically. Wallet entries are country-scoped, including different countries sharing a server. Startup sign-in uses the selected country's entry once. The old Australian entry and old boolean-only preferences remain compatible. The app converts Windows timezone IDs to IANA names for protocol requests.

The app opens on **Status**, including when opened again from the tray: battery, range, charging, locks/openings and freshness, with **Refresh status** and **Vehicle location map**. Account fields, preferences and command buttons live on **Settings & advanced**. Switching views does not fetch data or send a command.

The second view has collapsible sections for account/startup, remote controls, detailed vehicle readings, reported feature availability, charging/schedules, climate/departure, windows/boot, seat/battery comfort and energy use. Detailed temperature, odometer and tyre readings come from the existing status response; tyre pressures are converted to kPa only when the source unit is known. Missing/sentinel values remain unknown.

**Load supported features** is read-only. Extra control buttons require reported support, a status read and session control opt-in. Window venting also requires an explicit vehicle learning flag. Unsupported/unreported actions stay disabled. Find-my-car, horn and flashing-light controls are intentionally excluded.

**Read charging details**, **Read climate & bookings**, and **Read energy statistics** load their respective sections on demand, at most once per minute per section. These reads may wake the car. Opening a section does not fetch data. Bookings omitted by BYD are labelled as not reported, rather than proof none exist. Advanced data stays in memory and is cleared when changing vehicle or signing in again.

Added commands include charging start/stop, replacement charging windows (once/daily/weekdays/weekends, including overnight/until-full), climate booking create/update/remove, window closing/venting, powered boot opening/closing, front-seat heating/ventilation, steering-wheel heating and battery heating. Availability depends on vehicle equipment and reported capabilities. Existing departure/off-peak charging schedules are displayed read-only; the editor writes the standard charging-window schedule supported by the reference client.

Each **Review** button opens a confirmation showing the exact action/settings, vehicle and masked PIN. Draft edits and reviews do not send a command. Persisted schedules are explicitly described as able to run after the app closes; disabling this session's controls does not cancel a saved vehicle schedule. PIN verification and capability checks run again for each approved command. Seat changes fetch the current comfort state and preserve other reported settings; an unknown supported setting blocks dispatch rather than replacing it with off. New advanced commands have offline coverage and remain untested on the user's vehicle. The previously tested four core controls retain their separate live-validation record.

## Incremental live test

1. Launch normally on Status. Open Settings & advanced → Account & startup for sign-in. Leave remote controls and periodic refresh unchecked.
2. Enter the BYD email and password **in the local app window**, never in chat, a source file, a shell argument or environment variable. An integration account with shared access may avoid interfering with your mobile-app session, but must be validated separately.
3. Press **Sign in — read only**. This authenticates and obtains the vehicle list only. Check the vehicle identity using its model and VIN suffix.
4. Press **Refresh status** once. This can wake the car. Compare battery, range, charging and door status with the official app. Unknown fields are not treated as zero or locked. Record only whether each field matched; do not upload raw responses or a VIN.
5. Test optional wallet saving and **Use saved account**. Linux uses KWallet 6 directly over D-Bus. Windows uses Credential Manager. Wallet errors never fall back to plaintext storage. **Forget saved account** removes only this app's account entry.
6. Test the tray menu and optional 15-minute polling. Manual refresh is limited to one attempt per minute. Polling pauses after failure; sign-in does not silently retry.
7. Only after status validation, tick **Enable remote controls for this session**. This is a local permission change on the existing client: it sends no request, needs no saved credentials or second sign-in, and retains the selected vehicle and readings. The note below **Check remote setup** states whether setup is ready or which step is missing. That button opens a separate **Check PIN only** dialog; only submitting that dialog checks lock support and PIN access. Enter the PIN locally once; rejected PINs may restrict access. Share only the safe result message. A passing check is not a live lock test and grants no command approval.
8. If status has not yet been read for this selection, **Refresh status** once. Toggling controls does not clear existing readings. Each actual command opens a confirmation naming the vehicle/action, with temperature/duration for climate and a masked six-digit PIN. PINs are never saved. Never run a real command test without explicit approval of that specific action. Errors before dispatch identify the support/PIN stage and state that no vehicle command was sent. Unknown outcomes after dispatch require checking the vehicle/official app before another attempt; there are no automatic resends. Unchecking controls revokes earlier approvals. Re-enabling requires a fresh confirmation/PIN, and does not cancel or reverse a vehicle-stored schedule. The checkbox is unavailable while an operation is running.

The C# client uses the official cloud through an unofficial protocol. Each refresh briefly subscribes to the authenticated account's MQTT topic, accepts only the selected vehicle's matching `vehicleInfo` response, and disconnects afterwards. It never publishes MQTT messages and has no telemetry cache. HTTP polling runs concurrently so a fresh result can finish without waiting for the push timeout; older snapshots remain a fallback. Zero-filled placeholders are rejected. Vehicle freshness is based on the vehicle timestamp, not just a successful request. A missing/old timestamp is marked stale or unverified. The completion message shows elapsed seconds.

## Vehicle location map

After signing in, choose the vehicle and press **Vehicle location map**. Opening this view requests one reported GPS fix; it can wake the car but requires no control opt-in or PIN. Location requests are limited to once per minute. The marker uses the GPS fix timestamp, with a stale/unverified warning for old or undated results. This is a last-reported position, not continuous tracking. Missing GPS fixes do not create a marker at zero coordinates.

Press **Load OpenStreetMap** to load street tiles around the marker. This explicit action shares the viewed area with the public tile provider; it sends no BYD account, VIN, PIN or token. Attribution remains visible. The map supports pan, zoom and **Centre on vehicle**. Map tiles have a bounded seven-day cache in memory while the app runs; neither coordinates nor tiles are written to disk. Closing the view clears its GPS fix and disconnects the read. There is no location history, reverse-geocoding request or bulk/offline map download.

In `--demo`, the location button opens a labelled synthetic public-landmark preview with network requests disabled. See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for map dependencies and provider terms. The user confirmed live GPS and online tile rendering match the official app; automated tests use only synthetic locations.

## Logging and credentials

Only allow-listed event names (including which read path succeeded) and UTC timestamps are logged, with rotation after 128 KiB. No free-form exception text, account, password, PIN, token, VIN, battery, location or API payload enters the log.

Synthetic demos and UI tests do not write to the live user's audit log.

- Linux: `~/.local/share/BydTray/events.log` (or .NET's local application-data directory), private permissions.
- Windows: `%LOCALAPPDATA%\BydTray\events.log`.
- KWallet entries: folder `BYD Tray`; existing AU entry `Australian account`, other countries `Account/XX`.
- Windows Credential Manager targets: existing AU `BydTray/Australia/Account`, other countries `BydTray/XX/Account`.

The checkbox to save an account is off by default. **Use saved account** loads credentials for a manual sign-in. **Sign in automatically at startup (system wallet)** is a separate persistent checkbox, off for a new installation: when enabled, app startup reads the wallet once, signs in once and refreshes status once. A missing/locked wallet or failure leaves the app available for manual recovery, with no automatic authentication retry. If there are multiple vehicles, choose one manually.

Only `AutoSignInOnStartup` and the non-secret `CountryCode` are saved in `preferences.json` beside the event log. Account data remains in the OS vault. Invalid country preferences safely disable automatic sign-in and restore the AU default; legacy preferences without a country keep AU. The **Refresh every 15 minutes** checkbox is independent and starts unchecked. Sessions, periodic polling, GPS and command opt-in are not restored. Managed strings cannot be guaranteed erased from memory.

## Tray hover

Status and tooltip decisions use the library's typed charging/lock enums; English labels are produced by the app. Hovering shows one line with battery percentage, charging status and opening/lock warnings. For example, with synthetic data: `BYD · 78% · Not charging · ⚠ unlocked, door/boot open, window/roof open`.

Door/window codes are interpreted separately: door `0` means closed, while window `0` is unknown. Positive window-opening percentages and reported open boot/front-compartment/sunroof states produce warnings. Missing core opening, boot, roof or lock states show `states unknown`; old data shows `stale`, and a failed refresh keeps the previous readings labelled `refresh failed`. The main window gives more opening details. No tooltip data is written to the event log.

The pinned Avalonia 12.1.6 Linux backend leaves the SNI `ToolTip` property empty. A narrow Linux adapter supplies that property for both `Get` and `GetAll`, while forwarding activation/menu requests to the original backend. It also supplies a stable `BydTray` item ID. The adapter reattaches when the tray host returns. It relies on version-specific Avalonia fields and must be revalidated when upgrading Avalonia; Windows keeps the normal tray implementation.

## User service and Windows startup

Save the account in KWallet and choose the automatic-sign-in checkbox as desired. Quit any manually launched instance from its tray menu before starting the service. From an extracted Linux release folder:

```sh
./scripts/install-user-service.sh
systemctl --user enable --now byd-tray.service
```

For a source checkout, publish with `./scripts/publish.sh linux-x64` first; the same installer detects that output. You can also pass the absolute executable path as its first argument. The installer writes the unit only; the explicit enable command activates startup. The service waits for the KDE graphical session/Plasma shell and launches with `--start-in-tray`, without opening the main window. Use **Open BYD Tray** from the tray menu. Automatic sign-in follows the checkbox; otherwise it starts disconnected. Unchecking that box stops automatic sign-in on the next launch while retaining tray startup.

`systemctl --user disable --now byd-tray.service` stops automatic startup. Remove the unit from `~/.config/systemd/user/` and run `systemctl --user daemon-reload` to uninstall. To update a running installation, stop the service before publishing a replacement binary, then start it again. The installer preserves existing units rather than overwriting them. `Restart=no` prevents crash/authentication restart loops.

On Windows, extract the complete portable release and run `Byd.Tray.exe`. For source development, clone this repository and run `scripts\run.ps1`; the pinned library restores from NuGet. For a self-contained Windows release archive, use `scripts\package-release.ps1`. No Python or separately installed .NET runtime is needed for a self-contained build. Use a shortcut to `Byd.Tray.exe --start-in-tray` in your Windows Startup folder if you choose to start it at login. The same automatic-sign-in checkbox uses Windows Credential Manager. A Windows Service is unsuitable for an interactive tray app. Windows runtime testing is still pending.
