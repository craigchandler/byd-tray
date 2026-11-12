# Contributing

Use the pinned NuGet client. Protocol changes belong in [byd-client-dotnet](https://github.com/craigchandler/byd-client-dotnet); keep this desktop repo independent.

Run the README's locked restore/build/headless tests and Python archive-boundary tests. Tests use synthetic accounts, mocked HTTP and demo locations; they must not read the developer's wallet or contact BYD. Dependency changes must retain licence texts; see `licenses/manifest.json` for pinned upstream revisions/hashes.

Never commit credentials, PINs, tokens, VINs, raw responses, locations, per-user settings, logs or private screenshots. Keep controls/polling off by default. Do not add replay or authentication loops. Every command requires a specific review and PIN; unknown outcomes remain unknown.

Report build/headless, Linux/Windows desktop, vault, account and vehicle evidence separately. Start with demo checks. Vehicle commands need separate explicit approval of the exact action. Open focused PRs with validation, and see [release setup](docs/releasing.md) before tagging/publishing.
