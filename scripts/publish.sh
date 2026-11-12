#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
runtime=${1:-linux-x64}
case "$runtime" in linux-x64|linux-arm64|win-x64|win-arm64) ;; *) echo 'Unsupported runtime' >&2; exit 1;; esac
dotnet publish "$repo_dir/src/Byd.Tray/Byd.Tray.csproj" -c Release -r "$runtime" --self-contained true -o "$repo_dir/artifacts/$runtime"
