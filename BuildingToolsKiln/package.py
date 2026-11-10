#!/usr/bin/env python3
"""Repackage an exact SHA256-pinned upstream Building Tools release as a
supported Kiln native drop-in, without editing the DLL or any installed game
files. Version pins are in BuildingToolsKiln/release.json.

Usage:
  python3 BuildingToolsKiln/package.py UPSTREAM.zip \
    --checksum UPSTREAM.zip.sha256 \
    --output dist/building-tools-kiln-1.0.0.zip

This is a *packaging* tool. Kiln remains the game's dinput8.dll loader.
"""
from __future__ import annotations
import argparse
import hashlib
import io
import json
import re
from pathlib import Path, PurePosixPath
import stat
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
README = """Enshrouded Building Tools — Kiln native adapter.
Original software (c) 2026 Aleksander Wojtal, MIT.
Upstream: https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools

This adapter repackages the UNMODIFIED original dinput8.dll bytes as
kiln/native/building-tools/BuildingTools.dll and adds native.json.
It NEVER replaces the existing game-folder dinput8.dll used by Kiln.

Old Cross-base Storage native experiment code has been archived.
Do not install its experimental kiln-rules/kiln-items DLL replacements.
Cross-base Storage 0.1.9 is diagnostic-only and not required by this mod.

This adapter has been confirmed to load and enable cross-base Magic Chests
under Kiln in a single-player Proton game. Ingredient conservation,
multiplayer, future game builds and all Building Tools features are not
separately certified. Back up saves before mod changes.
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
You can also use Home -> Options -> Magic chests of all bases.
This is OFF by default. Do not run overlapping native crafting hooks.
The upstream mod keeps its own game-signature checks; after game updates,
verify both its menu and actual crafting results.

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
            rawname = entry.filename
            member = PurePosixPath(rawname)
            mode = (entry.external_attr >> 16) & 0xFFFF
            if "\\" in rawname or stat.S_IFMT(mode) not in (0, stat.S_IFREG):
                raise ValueError("Unsafe archive entry type: " + rawname)
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
    if peoff < 64 or peoff + 24 > len(dll) or dll[peoff:peoff + 4] != b"PE\0\0":
        raise ValueError("Invalid PE executable header")
    if dll[peoff + 4:peoff + 6] != b"\x64\x86":
        raise ValueError("Upstream DLL is not x86-64")
    return match.group(1), content, digest


def add(zipped: zipfile.ZipFile, name: str, data: bytes) -> None:
    """Deterministic zip members; never preserve arbitrary upstream metadata."""
    info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
    info.compress_type = zipfile.ZIP_DEFLATED
    info.create_system = 3
    info.external_attr = 0o100644 << 16
    zipped.writestr(info, data)


def load_profile(path: Path) -> dict:
    cfg = json.loads(path.read_text(encoding="utf-8"))
    if (not re.fullmatch(r"\d+\.\d+\.\d+", cfg["adapter_version"]) or
        not re.fullmatch(r"\d+\.\d+\.\d+", cfg["upstream_version"]) or
        not re.fullmatch(r"[0-9a-f]{64}", cfg["upstream_sha256"]) or
        not re.fullmatch(r"[0-9a-f]{64}", cfg["tested_client_sha256"])):
        raise ValueError("Invalid fixed release profile")
    return cfg


def pack(src: Path, checksum: Path, out: Path, release: dict) -> dict:
    if src.resolve() == out.resolve() or checksum.resolve() == out.resolve():
        raise ValueError("Output cannot replace input or official checksum")
    expected = checksum_value(checksum)
    version, content, sha = source_files(src)
    if expected != sha:
        raise ValueError("Official release archive SHA256 does not match checksum")
    if version != release["upstream_version"] or sha != release["upstream_sha256"]:
        raise ValueError("Upstream release differs from the approved pinned version/hash")
    if out.exists():
        raise ValueError("Output exists; refusing to overwrite: " + str(out))
    manifest = {
        "format": 1,
        "id": "building-tools",
        "version": release["adapter_version"],
        "name": "Enshrouded Building Tools (Kiln adapter)",
        "side": "client",
        "dlls": ["BuildingTools.dll"],
        "phase": "boot",
        "init": None,
        "enabled": True,
        "kiln": ">=0.2.0-dev",
        "builds": [release["tested_client_sha256"]],
        "license": "MIT",
        "authors": ["Aleksander Wojtal"],
        "url": UPSTREAM,
    }
    meta = {
        "adapter": "native.json drop-in; original DLL bytes unchanged",
        "upstream_release": f"v{version}",
        "adapter_version": release["adapter_version"],
        "tested_build": "Enshrouded client 26430625; hosted single-player under Proton",
        "in_game_report": "Magic chests from all bases accessible; user confirmed 2026-11-09",
        "upstream_archive_sha256": sha,
        "original_dll_sha256": hashlib.sha256(content["dinput8.dll"]).hexdigest(),
        "original_licence": "MIT / Aleksander Wojtal",
        "gameplay_verified": False,
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out, "x", compression=zipfile.ZIP_DEFLATED) as zipped:
        add(zipped, ROOT + "native.json", (json.dumps(manifest, indent=2) + "\n").encode())
        add(zipped, ROOT + "BuildingTools.dll", content["dinput8.dll"])
        add(zipped, ROOT + "BuildingTools-LICENSE.txt",
            content["BuildingTools-LICENSE.txt"])
        add(zipped, ROOT + "BuildingTools-README.txt",
            content["BuildingTools-README.txt"])
        add(zipped, ROOT + "ADAPTER-README.txt", README.encode())
        add(zipped, ROOT + "provenance.json",
            (json.dumps(meta, indent=2) + "\n").encode())
        for name in sorted(content):
            if name.startswith("BuildingTools-Languages/"):
                # Upstream uses game-folder translations; never place these
                # in a native DLL folder where its game-root lookup fails.
                add(zipped, name, content[name])
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
    parser.add_argument("--profile", type=Path,
                        default=Path(__file__).with_name("release.json"))
    args = parser.parse_args()
    try:
        info = pack(args.archive, args.checksum, args.output,
                    load_profile(args.profile))
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
        parser.error(str(exc))
    print(f"PASS packaged official Building Tools {info['upstream_release']}")
    print(f"Original DLL bytes preserved; original archive SHA256: "
          f"{info['upstream_archive_sha256']}")
    print(f"Kiln native drop-in: {args.output} (adapter v{info['adapter_version']})")
    print("No original game DLL replaced; remote payment conservation not yet formally verified.")
