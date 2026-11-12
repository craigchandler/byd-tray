"""Optional KDE runtime check. Starts only a synthetic demo; never prints D-Bus payloads."""
import argparse
import re
import subprocess
import sys
import time
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("app", type=Path, help="Published Byd.Tray executable or debug Byd.Tray.dll")
args = parser.parse_args()
app = args.app.resolve()
if sys.platform != "linux" or app.name not in ("Byd.Tray", "Byd.Tray.dll") or not app.is_file():
    raise SystemExit("Use an existing BYD Tray build in a Linux desktop session.")
command = (["dotnet", str(app)] if app.suffix == ".dll" else [str(app)]) + ["--demo", "--start-in-tray"]
process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
service = f"org.kde.StatusNotifierItem-{process.pid}-0"

def call(method, *arguments, path="/StatusNotifierItem"):
    return subprocess.run(["gdbus", "call", "--session", "--dest", service, "--object-path", path,
                           "--method", method, *arguments], capture_output=True, text=True, timeout=5)

try:
    deadline = time.monotonic() + 12
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError("Demo exited; quit any existing demo before running this check.")
        tooltip = call("org.freedesktop.DBus.Properties.Get", "org.kde.StatusNotifierItem", "ToolTip")
        if tooltip.returncode == 0 and "78%" in tooltip.stdout and "DEMO" in tooltip.stdout:
            break
        time.sleep(0.15)
    else:
        raise RuntimeError("Native ToolTip did not contain the synthetic demo status.")
    all_properties = call("org.freedesktop.DBus.Properties.GetAll", "org.kde.StatusNotifierItem")
    menu = call("org.freedesktop.DBus.Properties.Get", "org.kde.StatusNotifierItem", "Menu")
    match = re.search(r"'(/[A-Za-z0-9_/]+)'", menu.stdout)
    if match is None:
        raise RuntimeError("Native menu path was unavailable.")
    layout = call("com.canonical.dbusmenu.GetLayout", "0", "1", "[]", path=match.group(1))
    checks = {
        "ToolTip has battery and charging": "78%" in tooltip.stdout and "Not charging" in tooltip.stdout,
        "ToolTip is labelled DEMO and one line": "DEMO" in tooltip.stdout and "\\n" not in tooltip.stdout,
        "GetAll includes ToolTip": all_properties.returncode == 0 and "ToolTip" in all_properties.stdout and "78%" in all_properties.stdout,
        "Stable desktop identity": "'Id': <'BydTray'>" in all_properties.stdout,
        "Original tray menu works": layout.returncode == 0 and "Open BYD Tray" in layout.stdout and "Refresh status" in layout.stdout and "Quit" in layout.stdout,
    }
    for name, passed in checks.items():
        print(f"{'PASS' if passed else 'FAIL'}: {name}")
    if not all(checks.values()):
        raise SystemExit(1)
except (OSError, subprocess.TimeoutExpired, RuntimeError) as error:
    # Only locally defined error messages are printed, never service replies or status data.
    print("Runtime check failed:", str(error) if isinstance(error, RuntimeError) else "Desktop tool or connection unavailable.")
    raise SystemExit(1)
finally:
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
