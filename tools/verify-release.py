#!/usr/bin/env python3
"""Check desktop archives without starting an app or contacting BYD."""
import argparse
import hashlib
import json
import posixpath
from pathlib import Path, PurePosixPath
import re
import struct
import tarfile
import zipfile
from urllib.parse import unquote, urlsplit


def validate_names(names):
    seen = set()
    roots = set()
    allowed_json = {"Byd.Tray.deps.json", "Byd.Tray.runtimeconfig.json", "release.json", "THIRD-PARTY-LICENSES/inventory.json"}
    for name in names:
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or "\\" in name or ":" in name:
            raise ValueError("Unsafe archive path")
        if len(path.parts) < 2 or name.casefold() in seen:
            raise ValueError("Invalid root or duplicate path")
        seen.add(name.casefold())
        roots.add(path.parts[0])
        leaf = path.name.lower()
        if leaf.startswith(".") or leaf.endswith((".pdb", ".mdb", ".log", ".cs", ".csproj", ".nupkg", ".snupkg")):
            raise ValueError("Development/private file in release")
        if any(word in leaf for word in ("credential", "secret", "preferences", ".env")):
            raise ValueError("Account or settings file in release")
        if leaf.endswith(".json") and "/".join(path.parts[1:]) not in allowed_json:
            raise ValueError("Unexpected JSON file in release")
    if len(roots) != 1:
        raise ValueError("Release must contain one folder")
    return next(iter(roots))


def validate_links(files):
    for name, content in files.items():
        if not name.endswith(".md") or name.startswith("THIRD-PARTY-LICENSES/"):
            continue
        for link in re.findall(r"!?\[[^\]]*\]\(([^)]+)\)", content.decode("utf-8")):
            if urlsplit(link).scheme or link.startswith(("#", "//")):
                continue
            target = posixpath.normpath(posixpath.join(posixpath.dirname(name), unquote(link.split("#", 1)[0])))
            assert target in files, "Broken bundled documentation link: " + name + " -> " + link


def verify(archive):
    if archive.name.endswith(".zip"):
        with zipfile.ZipFile(archive) as bundle:
            if any((p.external_attr >> 16) & 0o170000 == 0o120000 for p in bundle.infolist()):
                raise ValueError("Symlink in release")
            content = {p.filename: bundle.read(p) for p in bundle.infolist() if not p.is_dir()}
            validate_names([p.filename for p in bundle.infolist() if not p.is_dir()])
    else:
        with tarfile.open(archive) as bundle:
            if any(not (p.isfile() or p.isdir()) for p in bundle.getmembers()):
                raise ValueError("Link or special file in release")
            content = {p.name: bundle.extractfile(p).read() for p in bundle.getmembers() if p.isfile()}
            validate_names([p.name for p in bundle.getmembers() if p.isfile()])
    root = validate_names(content)
    files = {n.removeprefix(root + "/"): data for n, data in content.items()}
    validate_links(files)
    metadata = json.loads(files["release.json"])
    assert root == f"BydTray-{metadata['version']}-{metadata['runtime']}"
    assert metadata["sourceRevision"] is None or re.fullmatch(r"[a-f0-9]{40}", metadata["sourceRevision"])
    required = {"Byd.Tray.dll", "Byd.Client.dll", "Byd.Tray.deps.json", "Byd.Tray.runtimeconfig.json",
                "LICENSE", "README.md", "THIRD-PARTY-NOTICES.md", "START-HERE.txt", "VALIDATION.md", "CHANGELOG.md", "SECURITY.md", "CONTRIBUTING.md",
                "docs/windows.md", "docs/privacy.md", "docs/compatibility.md", "docs/usage.md", "docs/releasing.md", "THIRD-PARTY-LICENSES/inventory.json"}
    runtime = metadata["runtime"]
    if runtime.startswith("win-"):
        required |= {"Byd.Tray.exe", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll"}
        pe = files["Byd.Tray.exe"]
        assert pe[:2] == b"MZ"
        offset = struct.unpack_from("<I", pe, 0x3C)[0]
        assert pe[offset:offset + 4] == b"PE\0\0"
        assert struct.unpack_from("<H", pe, offset + 4)[0] == (0x8664 if runtime == "win-x64" else 0xAA64)
    else:
        required |= {"Byd.Tray", "libcoreclr.so", "libhostfxr.so", "libhostpolicy.so", "System.Private.CoreLib.dll"}
        assert files["Byd.Tray"][:4] == b"\x7fELF"
    assert required <= files.keys(), "Missing release files: " + str(required - files.keys())
    config = json.loads(files["Byd.Tray.runtimeconfig.json"])["runtimeOptions"]
    assert config["tfm"] == "net10.0", "Review target framework changes before releasing"
    assert any(f["name"] == "Microsoft.NETCore.App" and f["version"] == metadata["dotnetRuntime"]
               for f in config["includedFrameworks"]), "Expected a self-contained .NET runtime"
    deps = json.loads(files["Byd.Tray.deps.json"])
    client = "Byd.Client/" + metadata["clientVersion"]
    assert deps["libraries"][client]["type"] == "package", "Published library package required"
    inventory = json.loads(files["THIRD-PARTY-LICENSES/inventory.json"])
    assert {n for n, p in deps["libraries"].items() if p["type"] == "package"} <= inventory.keys()
    for package in inventory.values():
        assert package["files"], "Missing dependency licence text"
        for name, digest in package["files"].items():
            assert name.startswith("THIRD-PARTY-LICENSES/")
            assert hashlib.sha256(files[name]).hexdigest() == digest, "Licence content mismatch"
    assert b"pyBYD" in b"\n".join(v for k, v in files.items() if k.startswith("THIRD-PARTY-LICENSES/"))
    return metadata


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    args = parser.parse_args()
    result = verify(args.archive)
    print("Verified self-contained release, pinned client, licence texts and privacy boundary:", result["runtime"], result["version"])
