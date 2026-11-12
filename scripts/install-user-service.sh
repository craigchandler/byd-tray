#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
app_path=${1:-$repo_dir/Byd.Tray}
if [[ $# -eq 0 && ! -x "$app_path" ]]; then app_path="$repo_dir/artifacts/linux-x64/Byd.Tray"; fi
if [[ ! -x "$app_path" ]]; then echo 'Use an extracted Linux release or publish the source build first.' >&2; exit 1; fi
app_path=$(realpath -- "$app_path")
if [[ "$app_path" == *$'\n'* || "$app_path" == *$'\r'* ]]; then echo 'Executable paths cannot contain line breaks.' >&2; exit 1; fi
unit_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
mkdir -p -- "$unit_dir"
if [[ -e "$unit_dir/byd-tray.service" ]]; then echo 'Existing unit left unchanged. Review it before replacing.' >&2; exit 1; fi
# systemd specifiers and quoted ExecStart escaping.
escaped_path=${app_path//\\/\\\\}
escaped_path=${escaped_path//\"/\\\"}
escaped_path=${escaped_path//%/%%}
escaped_path=${escaped_path//\$/\$\$}
umask 077
cat > "$unit_dir/byd-tray.service" <<EOF
[Unit]
Description=BYD Tray desktop application
PartOf=graphical-session.target
After=graphical-session.target plasma-plasmashell.service

[Service]
Type=simple
ExecStart="$escaped_path" --start-in-tray
Restart=no
UMask=0077
LimitCORE=0

[Install]
WantedBy=graphical-session.target
EOF
systemctl --user daemon-reload
echo 'Installed only. To start: systemctl --user start byd-tray.service'
echo 'To enable at graphical login: systemctl --user enable byd-tray.service'
