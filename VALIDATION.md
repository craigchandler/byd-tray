# Validation record — 12 November 2026

Evidence is separated by layer. Preparation did not sign in, read vehicle data, send commands, read real vault contents, or restart the installed Linux service.

| Layer | Current evidence |
| --- | --- |
| Dependency | App uses Byd.Client 0.1.0-alpha.1 from NuGet. Locked restore succeeds without a sibling project reference. The library's independent 248 offline tests pass. |
| UI | All 35 headless tests pass against the published package: defaults, mocked sign-in, country-scoped vault/preferences, local control opt-in without reauthentication, exact confirmation/PIN dialogs, typed status, explicit model/VIN-suffix picker labels and map/demo isolation. |
| Build | .NET SDK 10.0.404, .NET runtime 10.0.15, Avalonia 12.1.6. Release build has zero warnings/errors. |
| Packaging | Windows x64 portable ZIP and Linux x64 tar.gz cross-build successfully. Verifier checks executable architecture, self-contained runtime, published NuGet client, dependency licence texts/hashes and archive privacy boundaries. Windows executable is unsigned. Local unborn/dirty source correctly records no committed revision. |
| Release boundary | Eight Python release/installer tests pass: unsafe paths, duplicate roots/case collisions, account/settings/log/debug/source files, symlinks incomplete releases and broken bundled links are rejected; service installation is checked to avoid enabling startup or overwriting units. Vendor Skia/HarfBuzz debug PDBs are explicitly excluded. |
| Native KDE demo | The newly packaged Linux demo passes native tooltip Get/GetAll, synthetic battery/charging/demo labels, stable tray identity and original menu checks. It cannot access the account wallet or call BYD. This is separate from visual hover/account behaviour. |
| Hosted CI | [Initial source run](https://github.com/craigchandler/byd-tray/actions/runs/43153076875) passed on Ubuntu and Windows at commit f1a67c78d8d2da47f200d7a7c61de7b6562c3295, including the native Windows demo-window smoke check. This proves a responding synthetic window on the hosted Windows runner; Windows 11 user-session/tray/vault checks remain separate. |
| Windows 11 | Binary cross-build and package inspection pass. Windows 11 visual window/tray/hover, Credential Manager, sign-in and login-startup tests are NOT_RUN. ARM64 builds/runtime are NOT_RUN. |
| Historical KDE/account | On the previously installed build, the owner confirmed AU Atto 3 sign-in, KWallet save/load/delete, status matching the mobile app, GPS/map, visual tooltip and lock/unlock/climate start/stop. One refresh took 2.7 seconds; no latency guarantee. These checks were not repeated for the release candidate. |
| New flow/advanced features | Typed state and local control-permission refinements have headless coverage but were not redeployed or live-checked during release preparation. Advanced reads/controls and non-AU models/regions remain unverified live. |
| Privacy/provenance | Bounded scan of intended text source found no credential/token/private-key patterns. Account fixture emails use reserved example domains. The initial source review covered the complete intended tree; no prior committed history existed. Full package/native/runtime notices are retained; no real account/settings/log/capture files are packaged. |

Windows and live checks must be reported separately from builds and demos. Start the Windows candidate with --demo, then check tray/reopen/quit. If account testing is chosen later, enter credentials locally with controls and polling off. Do not share private screenshots, raw responses or locations. No remote vehicle command is part of package acceptance.
