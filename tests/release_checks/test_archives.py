"""Offline regressions for unsafe release contents; no application is launched."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("release_check", Path(__file__).resolve().parents[2] / "tools/verify-release.py")
release_check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release_check)


class ArchiveBoundaryTests(unittest.TestCase):
    def test_bundled_readme_links_and_images_must_resolve(self):
        files = {"README.md": b"[Usage](docs/usage.md) ![Demo](docs/demo.png)",
                 "docs/usage.md": b"[Licence](../LICENSE) [Web](https://example.invalid)",
                 "docs/demo.png": b"synthetic", "LICENSE": b"Synthetic licence"}
        release_check.validate_links(files)
        del files["docs/demo.png"]
        with self.assertRaisesRegex(AssertionError, "Broken bundled documentation link"):
            release_check.validate_links(files)

    def test_rejects_traversal_and_windows_absolute_paths(self):
        for path in ("../account.txt", "/tmp/account.txt", "app/../account.txt", "C:/account.txt", "app\\account.txt"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                release_check.validate_names([path])

    def test_rejects_settings_secrets_logs_debug_symbols_and_source(self):
        for path in ("preferences.json", "events.log", "credentials.txt", ".env", "Byd.Tray.pdb", "Program.cs", "extra.json", "private/inventory.json"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                release_check.validate_names(["app/" + path])

    def test_rejects_duplicate_and_multiple_roots(self):
        for paths in (["app/LICENSE", "app/license"], ["app/LICENSE", "other/LICENSE"]):
            with self.subTest(paths=paths), self.assertRaises(ValueError):
                release_check.validate_names(paths)

    def test_rejects_symlinks_even_if_named_like_a_required_binary(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "bad.zip"
            with zipfile.ZipFile(archive, "w") as bundle:
                entry = zipfile.ZipInfo("app/Byd.Tray.exe")
                entry.create_system = 3
                entry.external_attr = (0o120777 << 16)
                bundle.writestr(entry, "../../private-file")
            with self.assertRaisesRegex(ValueError, "Symlink"):
                release_check.verify(archive)

    def test_rejects_archive_with_only_documents(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "incomplete.zip"
            with zipfile.ZipFile(archive, "w") as bundle:
                bundle.writestr("app/README.md", "Synthetic incomplete archive")
            with self.assertRaises((KeyError, AssertionError)):
                release_check.verify(archive)


if __name__ == "__main__":
    unittest.main()
