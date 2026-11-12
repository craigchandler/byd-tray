# Compatibility and evidence

Targets: .NET 10 / Avalonia 12.1.6, Windows 11 x64 and KDE Plasma Linux x64. Windows/Linux ARM64 can be built but are not initial CI/runtime claims. No macOS release is prepared.

The app consumes Byd.Client 0.1.0-alpha.1 from NuGet. Country/region discovery is routing metadata, not a compatibility guarantee. Only international email sign-in is implemented; mainland China is outside this catalog.

Historical live evidence is one Australian Atto 3 on the previously installed KDE build: sign-in, matching status, saved-account save/load/delete, location/map, lock/unlock and climate start/stop were owner-confirmed. One refresh measured 2.7 seconds; this is not a latency guarantee. These checks were not repeated during release preparation.

Offline tests cover defaults, redaction, mocked sign-in/session use, country-scoped preferences/vault selection, typed status, opening warnings, control opt-in without another login, exact confirmation/PIN dialogs and map/demo isolation. The dependency library has its own offline suite and live-evidence limits.

A cross-build proves creation of Windows files. Headless tests prove simulated UI behaviour; native demo smoke proves a responding labelled window on its host. None establishes Windows 11 tray/hover, vault, login-startup or real account/vehicle behaviour. See [VALIDATION.md](../VALIDATION.md) for actual results.

Advanced charging, bookings, windows/boot, seat/steering/battery comfort and energy operations have conservative capability gates/offline coverage but remain unverified live. Find-my-car/horn/flashing lights are omitted. Unknown/not-reported states remain inconclusive.
