#!/usr/bin/env python3
"""Build an isolated, self-contained desktop archive. Never starts the app."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "src/Byd.Tray/Byd.Tray.csproj"


def fields(path):
    return {e.tag.rsplit("}", 1)[-1]: e for e in next(iter(ET.parse(path).getroot()))}


def copy_notices(publish, runtime):
    assets = json.loads((PROJECT.parent / "obj/project.assets.json").read_text())
    deps = json.loads((publish / "Byd.Tray.deps.json").read_text())
    fallback = json.loads((REPO / "licenses/manifest.json").read_text())
    destination = publish / "THIRD-PARTY-LICENSES"
    destination.mkdir()
    inventory = {}
    for identity, data in sorted(deps["libraries"].items()):
        if data["type"] != "package":
            continue
        package = assets["libraries"][identity]
        package_dir = next((Path(p) / package["path"] for p in assets["packageFolders"]
                            if (Path(p) / package["path"]).is_dir()), None)
        if package_dir is None:
            raise ValueError("Package not restored: " + identity)
        metadata = fields(next(package_dir.glob("*.nuspec")))
        licence = metadata.get("license")
        sources = [p for p in package_dir.iterdir() if p.is_file() and
                   any(word in p.name.lower() for word in ("license", "licence", "notice", "copying", "copyright"))]
        # Honour a nested license-file path declared by the package as well.
        if licence is not None and licence.get("type") == "file":
            declared = (package_dir / licence.text).resolve()
            if not declared.is_relative_to(package_dir.resolve()) or not declared.is_file():
                raise ValueError("Invalid package licence path: " + identity)
            if declared not in sources:
                sources.append(declared)
        upstream = fallback["packages"].get(identity)
        if not sources:
            if upstream is None:
                raise ValueError("Retain upstream licence text before releasing " + identity)
            for name in upstream["files"]:
                source = REPO / "licenses" / name
                assert hashlib.sha256(source.read_bytes()).hexdigest() == fallback["files"][name]["sha256"], name
                sources.append(source)
        target = destination / identity.replace("/", "-")
        target.mkdir()
        for source in sources:
            shutil.copyfile(source, target / source.name)
        repository = metadata.get("repository")
        inventory[identity] = {
            "license": licence.text if licence is not None else "See retained licence",
            "repository": repository.attrib if repository is not None else None,
            "upstream": upstream,
            "files": {str(p.relative_to(publish)).replace(os.sep, "/"): hashlib.sha256(p.read_bytes()).hexdigest()
                      for p in sorted(target.iterdir())},
        }
    framework = json.loads((publish / "Byd.Tray.runtimeconfig.json").read_text())["runtimeOptions"]["includedFrameworks"]
    runtime_version = next(x["version"] for x in framework if x["name"] == "Microsoft.NETCore.App")
    runtime_package = f"microsoft.netcore.app.runtime.{runtime}/{runtime_version}"
    package_dir = next((Path(p) / runtime_package for p in assets["packageFolders"]
                        if (Path(p) / runtime_package).is_dir()), None)
    if package_dir is None:
        raise ValueError("Runtime licence package missing: " + runtime_package)
    target = destination / ("dotnet-" + runtime_version)
    target.mkdir()
    for name in ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"):
        shutil.copyfile(package_dir / name, target / name)
    inventory[runtime_package] = {
        "license": "MIT and retained third-party notices",
        "files": {str(p.relative_to(publish)).replace(os.sep, "/"): hashlib.sha256(p.read_bytes()).hexdigest()
                  for p in sorted(target.iterdir())},
    }
    (destination / "inventory.json").write_text(json.dumps(inventory, indent=2, sort_keys=True) + "\n")
    return runtime_version


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("runtime", choices=["win-x64", "win-arm64", "linux-x64", "linux-arm64"])
    parser.add_argument("--no-restore", action="store_true", help="Use an earlier locked solution restore")
    args = parser.parse_args()
    version = ET.parse(PROJECT).findtext("./PropertyGroup/Version")
    assert re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?", version)
    client = next(p.get("Version") for p in ET.parse(PROJECT).iter("PackageReference") if p.get("Include") == "Byd.Client")
    artifacts = REPO / "artifacts"
    artifacts.mkdir(exist_ok=True)
    output = artifacts / "releases"
    output.mkdir(exist_ok=True)
    if not args.no_restore:
        subprocess.run(["dotnet", "restore", str(REPO / "Byd.Tray.slnx"), "--locked-mode"], check=True, cwd=REPO)
    name = f"BydTray-{version}-{args.runtime}"
    with tempfile.TemporaryDirectory(prefix="release-", dir=artifacts) as temporary:
        publish = Path(temporary) / name
        subprocess.run(["dotnet", "publish", str(PROJECT), "-c", "Release", "-r", args.runtime,
                        "--self-contained", "true", "--no-restore", "-p:PublishTrimmed=false",
                        "-p:DebugType=None", "-p:DebugSymbols=false", "-p:CopyOutputSymbolsToPublishDirectory=false",
                        "-p:ContinuousIntegrationBuild=true", "-o", str(publish)], check=True, cwd=REPO)
        # Native Skia/HarfBuzz packages also copy PDBs as native assets; the SDK's
        # managed-symbol publish switch does not exclude those vendor debug files.
        for symbols in publish.rglob("*.pdb"):
            symbols.unlink()
        for source in ("LICENSE", "README.md", "THIRD-PARTY-NOTICES.md", "VALIDATION.md", "CHANGELOG.md", "SECURITY.md", "CONTRIBUTING.md"):
            shutil.copyfile(REPO / source, publish / source)
        (publish / "docs").mkdir()
        for source in sorted((REPO / "docs").glob("*.md")):
            shutil.copyfile(source, publish / "docs" / source.name)
        (publish / "docs/images").mkdir()
        shutil.copyfile(REPO / "docs/images/status-demo.png", publish / "docs/images/status-demo.png")
        if args.runtime.startswith("linux-"):
            (publish / "scripts").mkdir()
            installer = publish / "scripts/install-user-service.sh"
            shutil.copyfile(REPO / "scripts/install-user-service.sh", installer)
            installer.chmod(0o755)
        runtime_version = copy_notices(publish, args.runtime)
        revision = subprocess.run(["git", "rev-parse", "HEAD"], cwd=REPO, capture_output=True, text=True)
        clean = subprocess.run(["git", "status", "--porcelain"], cwd=REPO, capture_output=True, text=True)
        commit = revision.stdout.strip() if revision.returncode == 0 and clean.returncode == 0 and not clean.stdout.strip() else None
        (publish / "release.json").write_text(json.dumps({"schema": 1, "version": version,
            "runtime": args.runtime, "clientVersion": client, "dotnetRuntime": runtime_version,
            "sourceRevision": commit, "signing": "unsigned"}, indent=2) + "\n")
        executable = "Byd.Tray.exe" if args.runtime.startswith("win-") else "Byd.Tray"
        (publish / "START-HERE.txt").write_text(
            f"BYD Tray {version} ({args.runtime})\n\n"
            f"Extract the entire archive to a folder, then run {executable}. Keep its supporting files together.\n"
            "No separate .NET runtime, Python, installer or administrator access is required.\n"
            "For an offline preview, launch it with --demo. Demo cannot sign in or operate a car.\n"
            "A fresh installation starts disconnected; saving an account, startup sign-in, polling and controls are opt-in.\n"
            "Closing the window leaves the tray running. Use the tray menu to quit.\n"
            "This is an unsigned experimental application. See docs/windows.md for Windows checks and setup.\n"
            "Live evidence is limited to one Australian Atto 3. Read docs/privacy.md and docs/compatibility.md.\n"
            "Unofficial; not affiliated with or endorsed by BYD.\n")
        suffix = ".zip" if args.runtime.startswith("win-") else ".tar.gz"
        archive = output / (name + suffix)
        candidate = Path(temporary) / (name + suffix)
        if suffix == ".zip":
            with zipfile.ZipFile(candidate, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
                for path in sorted(publish.rglob("*")):
                    if path.is_file():
                        bundle.write(path, path.relative_to(publish.parent))
        else:
            with tarfile.open(candidate, "w:gz") as bundle:
                bundle.add(publish, arcname=name)
        subprocess.run([sys.executable, str(REPO / "tools/verify-release.py"), str(candidate)], check=True)
        digest = hashlib.sha256(candidate.read_bytes()).hexdigest()
        candidate.replace(archive)
        (output / (archive.name + ".sha256")).write_text(digest + "  " + archive.name + "\n")
    print("Release archive:", archive)
    print("SHA-256:", digest)


if __name__ == "__main__":
    main()
