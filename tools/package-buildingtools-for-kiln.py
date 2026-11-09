#!/usr/bin/env python3
"""Package the official MIT-licensed Enshrouded Building Tools ZIP as a
Kiln native *drop-in*, without modifying its executable bytes or writing
to an installed game. The original game-folder dinput8.dll is never touched.

Requires: official release ZIP and its separately published SHA256 checksum.
Usage:
  python3 tools/package-buildingtools-for-kiln.py \
    EnshroudedBuildingTools-0.31.2.zip \
    --checksum EnshroudedBuildingTools-0.31.2.zip.sha256 \
    --output BuildingTools-Kiln-native-0.31.2-EXPERIMENTAL.zip

This is an interoperability adapter, NOT evidence of runtime compatibility.
"""
from __future__ import annotations
import argparse
import hashlib
import io
import json
import re
from pathlib import Path, PurePosixPath
import zipfile

NAME = re.compile(r"EnshroudedBuildingTools-(\d+\.\d+\.\d+)\.zip\Z")
MAX_BYTES = 100 * 1024 * 1024
ROOT = "kiln/native/building-tools/"
UPSTREAM = "https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools"
KNOWN = {
    "dinput8.dll",
    "BuildingTools-LICENSE.txt",
    "BuildingTools-README.txt",
}
README = """Experimental Kiln native drop-in adapter for Enshrouded Building Tools.
Original software (c) 2026 Aleksander Wojtal, MIT.
Upstream: https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools

This adapter repackages the UNMODIFIED original dinput8.dll bytes as
kiln/native/building-tools/BuildingTools.dll and adds native.json.
It NEVER replaces the existing game-folder dinput8.dll used by Kiln.

Do not install alongside the experimental Cross-base Storage native test
DLLs: both projects detour the original stock/payer path.
The original read-only Cross-base Storage 0.1.9 may remain installed.

Before use, back up your saves and test in a disposable single-player world.
Extract this adapter ZIP into the Enshrouded game root (with Kiln already
installed); ensure the Kiln launcher profile allows native drop-ins, if
using a profile. Native mod loader status:
  kiln/state/native.json (look for id "building-tools", state "loaded")
Building Tools runtime status:
  BuildingTools.log in the game directory (look for "ready")
The latter, not merely "loaded", proves its own executable signatures passed.

Building Tools creates BuildingTools.ini in the game directory:
  [Chests]
  GlobalMagicChests=1
This is OFF by default. Do not enable creative building or run overlapping
native crafting hooks at the same time.
On Proton, a native DLL may load without all of its hooks functioning:
verify normal recipe availability AND exactly-once source consumption.

To remove: exit the game, delete kiln/native/building-tools, and delete
only BuildingTools-Languages installed by this package if no other mod
uses it. Do not delete the game-folder dinput8.dll (that is Kiln).
"""


def checksum_value(path: Path) -> str:
    text = path.read_text(encoding="ascii").strip()
    m = re.fullmatch(r"([0-9a-fA-F]{64})(?:\s+\*?[^\r\n]+)?", text)
    if not m:
        raise ValueError("Invalid official SHA256 checksum file")
    return m.group(1).lower()


def source_files(path: Path) -> tuple[str, dict[str, bytes], str]:
    match = NAME.fullmatch(path.name)
    if not match:
        raise ValueError("Expected official EnshroudedBuildingTools-X.Y.Z.zip filename")
    if not path.is_file() or path.stat().st_size > MAX_BYTES:
        raise ValueError("Missing or unreasonably large upstream archive")
    data = path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    content: dict[str, bytes] = {}
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        total = 0
        for entry in archive.infolist():
            rawname = entry.filename.replace("\\", "/")
            member = PurePosixPath(rawname)
            if (member.is_absolute() or ".." in member.parts or
                    not rawname or ":" in rawname or rawname.startswith("/")):
                raise ValueError("Unsafe archive member: " + rawname)
            if entry.is_dir():
                continue
            total += entry.file_size
            if total > MAX_BYTES:
                raise ValueError("Archive is too large when extracted")
            if rawname in content:
                raise ValueError("Duplicate archive member: " + rawname)
            if not (rawname in KNOWN or
                    (rawname.startswith("BuildingTools-Languages/") and
                     rawname.endswith(".txt") and len(member.parts) == 2)):
                raise ValueError("Unexpected upstream archive member: " + rawname)
            content[rawname] = archive.read(entry)
    if not KNOWN.issubset(content):
        raise ValueError("Official archive is missing DLL, README, or MIT licence")
    if b"MIT License" not in content["BuildingTools-LICENSE.txt"]:
        raise ValueError("Expected upstream MIT licence was not preserved")
    dll = content["dinput8.dll"]
    if len(dll) < 1024 or dll[:2] != b"MZ":
        raise ValueError("Expected an original native Windows x64 DLL")
    peoff = int.from_bytes(dll[0x3c:0x40], "little")
    if peoff + 6 > len(dll) or dll[peoff:peoff + 4] != b"PE\0\0":
        raise ValueError("Invalid PE executable header")
    if dll[peoff + 4:peoff + 6] != b"\x64\x86":
        raise ValueError("Upstream DLL is not x86-64")
    return match.group(1), content, digest


def pack(src: Path, checksum: Path, out: Path) -> dict:
    if src.resolve() == out.resolve() or checksum.resolve() == out.resolve():
        raise ValueError("Output cannot replace input or official checksum")
    expected = checksum_value(checksum)
    version, content, sha = source_files(src)
    if expected != sha:
        raise ValueError("Official release archive SHA256 does not match checksum")
    if out.exists():
        raise ValueError("Output exists; refusing to overwrite: " + str(out))
    manifest = {
        "format": 1,
        "id": "building-tools",
        "version": version,
        "name": "Enshrouded Building Tools (Kiln adapter)",
        "side": "client",
        "dlls": ["BuildingTools.dll"],
        "phase": "boot",
        "init": None,
        "enabled": True,
        "kiln": ">=0.2.0-dev",
        "license": "MIT",
        "authors": ["Aleksander Wojtal"],
        "url": UPSTREAM,
    }
    meta = {
        "adapter": "native.json drop-in; original DLL bytes unchanged",
        "upstream_release": f"v{version}",
        "upstream_archive_sha256": sha,
        "original_dll_sha256": hashlib.sha256(content["dinput8.dll"]).hexdigest(),
        "original_licence": "MIT / Aleksander Wojtal",
        "gameplay_verified": False,
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out, "x", compression=zipfile.ZIP_DEFLATED) as zipped:
        zipped.writestr(ROOT + "native.json", json.dumps(manifest, indent=2) + "\n")
        zipped.writestr(ROOT + "BuildingTools.dll", content["dinput8.dll"])
        zipped.writestr(ROOT + "BuildingTools-LICENSE.txt",
                        content["BuildingTools-LICENSE.txt"])
        zipped.writestr(ROOT + "BuildingTools-README.txt",
                        content["BuildingTools-README.txt"])
        zipped.writestr(ROOT + "ADAPTER-README.txt", README)
        zipped.writestr(ROOT + "provenance.json", json.dumps(meta, indent=2) + "\n")
        for name in sorted(content):
            if name.startswith("BuildingTools-Languages/"):
                # Upstream uses game-folder translations; never place these
                # in a native DLL folder where its game-root lookup fails.
                zipped.writestr(name, content[name])
    with zipfile.ZipFile(out) as check:
        assert check.read(ROOT + "BuildingTools.dll") == content["dinput8.dll"]
        assert check.read(ROOT + "BuildingTools-LICENSE.txt") == content[
            "BuildingTools-LICENSE.txt"]
        assert "dinput8.dll" not in check.namelist()
        assert json.loads(check.read(ROOT + "native.json"))["dlls"] == [
            "BuildingTools.dll"]
    return meta


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--checksum", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        info = pack(args.archive, args.checksum, args.output)
    except (OSError, ValueError, zipfile.BadZipFile) as exc:
        parser.error(str(exc))
    print(f"PASS packaged official Building Tools {info['upstream_release']}")
    print(f"Original DLL bytes preserved; original archive SHA256: "
          f"{info['upstream_archive_sha256']}")
    print(f"Experimental Kiln native drop-in: {args.output}")
    print("No original game DLL replaced; runtime compatibility NOT asserted.")
