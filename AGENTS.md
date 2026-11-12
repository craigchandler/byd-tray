# Working agreement

- This repository contains the desktop app. The sibling `../byd-client-dotnet` contains the reusable protocol client.
- Preserve both repositories; do not merge their histories or silently copy library source into this app.
- Keep controls and automatic polling off by default. Require a specific confirmation and PIN for every command.
- Never send real vehicle commands without explicit user approval of the exact vehicle/action/parameters.
- Keep credentials, tokens, PINs, VINs and raw telemetry out of source, logs, screenshots and chat. Use synthetic data for UI previews and tests.
- Keep secrets in KWallet / Windows Credential Manager; no plaintext fallback or automatic authentication retry.
- Run the offline client suite and relevant UI tests. Distinguish build/headless evidence from live KDE, KWallet, account, vehicle and Windows evidence.
- Do not commit, push, publish, create a remote, tag or enable startup without an explicit request.
