"""Check that installation cannot enable startup or overwrite an existing unit."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/install-user-service.sh"


@unittest.skipUnless(sys.platform == "linux", "Linux user-service installer")
class ServiceInstallerTests(unittest.TestCase):
    def test_installs_only_reloads_and_preserves_existing_unit(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            app = root / "app with space%and$symbol" / "Byd.Tray"
            app.parent.mkdir()
            app.write_text("#!/bin/sh\nexit 0\n")
            app.chmod(0o755)
            commands = root / "commands"
            commands.mkdir()
            stub = commands / "systemctl"
            stub.write_text('#!/bin/sh\nprintf "%s\\n" "$*" >> "$TEST_SYSTEMCTL_LOG"\n')
            stub.chmod(0o755)
            log = root / "calls"
            env = dict(os.environ, XDG_CONFIG_HOME=str(root / "config"),
                       TEST_SYSTEMCTL_LOG=str(log), PATH=str(commands) + os.pathsep + os.environ["PATH"])
            subprocess.run(["bash", str(SCRIPT), str(app)], env=env, check=True, capture_output=True)
            self.assertEqual("--user daemon-reload\n", log.read_text())
            unit = root / "config/systemd/user/byd-tray.service"
            data = unit.read_text()
            self.assertIn("Restart=no", data)
            self.assertIn("--start-in-tray", data)
            self.assertIn("%%", data)
            self.assertIn("$$", data)
            again = subprocess.run(["bash", str(SCRIPT), str(app)], env=env, capture_output=True)
            self.assertNotEqual(0, again.returncode)
            self.assertEqual(data, unit.read_text())
            self.assertEqual("--user daemon-reload\n", log.read_text())

    def test_rejects_line_break_in_executable_path_before_writing_a_unit(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            app = root / "name\nwith-line-break"
            app.write_text("#!/bin/sh\nexit 0\n")
            app.chmod(0o755)
            env = dict(os.environ, XDG_CONFIG_HOME=str(root / "config"))
            result = subprocess.run(["bash", str(SCRIPT), str(app)], env=env, capture_output=True)
            self.assertNotEqual(0, result.returncode)
            self.assertFalse((root / "config").exists())
