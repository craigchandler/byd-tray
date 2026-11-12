# Map component and provider

The map view uses [Mapsui.Avalonia12 5.1.0](https://www.nuget.org/packages/Mapsui.Avalonia12/5.1.0), MIT, copyright Mapsui Developers, and its [BruTile 6.0.0](https://www.nuget.org/packages/BruTile/6.0.0) tile dependency. BruTile's licence is supplied in its NuGet package as `license.txt`. Other transitive dependencies retain their own licences in their packages.

Street tiles are requested only after the user presses **Load OpenStreetMap**, from `https://tile.openstreetmap.org/{z}/{x}/{y}.png`. The view displays **© OpenStreetMap contributors** and the [copyright link](https://www.openstreetmap.org/copyright). See the [tile-use policy](https://operations.osmfoundation.org/policies/tiles/) and [OSMF privacy policy](https://osmfoundation.org/wiki/Privacy_Policy). No street tiles are bundled with this repository or its test images. There is no bulk download or offline-map feature.

Requests use the application's own User-Agent. The bounded tile cache is memory-only and retains entries for seven days while the process runs; it is discarded on app exit. GPS fixes are not persisted. Map-library free-form diagnostics are suppressed because tile URLs can reveal viewed areas.

The published `Byd.Client` NuGet package has its own protocol/upstream notices. Its MIT licence, pyBYD protocol attribution and country-catalog notices are copied into the desktop release's `THIRD-PARTY-LICENSES` folder. The map library and provider are not affiliated with BYD.

The Linux tooltip adapter references the [Avalonia 12.1.6 DBusTrayIconImpl implementation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.6/src/Avalonia.FreeDesktop/DBusTrayIconImpl.cs), MIT, copyright the Avalonia Project. It decorates D-Bus property handling and retains the original activation/menu backend; no fork of Avalonia is bundled. Its private-field bindings are specific to the pinned version and require native protocol revalidation on upgrades.

## Redistributed components

Self-contained archives include the .NET runtime and third-party UI, graphics, map and IPC assemblies. Their licences differ: MIT, BSD-3-Clause, Microsoft Public License (MS-PL), Apache-2.0 and retained native-component notices apply as indicated in each package. The app's MIT licence does not replace those terms.

The release packager copies package-supplied licence/notice files and the .NET runtime's LICENSE/THIRD-PARTY-NOTICES. When packages omit full text, `licenses/manifest.json` records exact upstream repository revisions, file URLs and SHA-256 hashes for the retained files in `licenses/`. These files are kept unchanged. In particular, Svg.Custom's MS-PL text is distinct from Svg.Skia's MIT text; NetTopologySuite components retain their BSD terms and RichTextKit its Apache terms.

Each archive includes `THIRD-PARTY-LICENSES/inventory.json` with shipped dependency identities, metadata, licence-file paths and hashes. Missing attribution blocks packaging. There are no bundled account captures, map tiles or upstream application examples. Original app icon assets are distributed under the repository's MIT licence; the ICO wraps the existing PNG without changing its pixels.
