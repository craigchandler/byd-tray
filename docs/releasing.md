# Desktop release setup

Repository: [craigchandler/byd-tray](https://github.com/craigchandler/byd-tray). Desktop app versions and releases are independent of the Byd.Client library.

## Local build

Use .NET SDK 10 and Python 3.10+. `python tools/build-release.py win-x64` produces a portable ZIP; `linux-x64` produces a tar.gz. Fresh staging avoids stale/private output. Publishing is self-contained/untrimmed, excludes private debug symbols and retains runtime/package licences. Verification checks architecture, bundled runtime, pinned client, notices and archive privacy boundaries. Output stays ignored under artifacts/.

Update app version, notes and docs together for later releases. Dependency updates may need pinned licence texts/hashes under licenses/. Do not suppress missing-attribution checks. Never package account state or captures.

## Repository safeguards

1. Review the source, dependency licences and privacy scan. Commit only intended source/docs/fixtures/workflows. Never include per-user state, credentials, captures or generated archives in source history.
2. Require Desktop validation to pass on Ubuntu and Windows for the reviewed source. No BYD credentials, vault or sibling checkout is needed.
3. Enable private vulnerability reporting, secret scanning/push protection and Dependabot security updates. Keep default workflow tokens read-only. Protect main with PRs and `validate (ubuntu-latest)` / `validate (windows-latest)` checks; block deletion/force push. A solo maintainer can use zero independent approvals.
4. Protect v* tag creation separately from update/deletion; allow reviewed maintainer creation and prohibit changing published tags. Enable release immutability. Create environment **github-release**, main only, with maintainer approval (allow self-review if sole reviewer). Declaring the environment in YAML does not configure these protections.

## Controlled release

1. Record Windows 11 demo/tray/vault evidence in VALIDATION.md. No vehicle controls are required for packaging validation; leave untested rows explicit.
2. Update README/changelog publication wording for the planned app release before tagging. Review green main and separately approve a matching version tag, initially `v0.1.0-alpha.1`. Do not move published tags.
3. Run **Prepare reviewed desktop release** manually from main with that existing tag, first with **create_draft=false**. It checks tag/version/current source and reuses offline CI and packaging. Inspect both artifacts/sourceRevision metadata. Unborn/dirty local builds intentionally record no source revision and cannot become a CI draft.
4. After review run with **create_draft=true**, then approve the protected github-release environment. Only the draft job gets contents:write. It checks downloaded archives/source, creates combined SHA256SUMS and creates an existing-tag **draft prerelease**. It does not create tags, publish releases or upload NuGet packages.
5. Inspect all draft assets/notes/checksums. Explicitly approve publication only when complete; immutable assets cannot be replaced afterward. Verify public downloads and record publication in README/changelog. Authenticode needs a real signing service/certificate and remains external setup.

Ordinary PRs, pushes and tag pushes do not publish. Windows CI smoke starts only the synthetic demo and proves a responding window, not Windows 11 user-session/vault/vehicle compatibility.

References: [Avalonia deployment](https://docs.avaloniaui.net/deployment), [self-contained .NET deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [GitHub draft releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository), [immutable releases](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/establish-provenance-and-integrity/prevent-release-changes), [Windows credentials](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw).
