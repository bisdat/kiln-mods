#!/usr/bin/env python3
"""Verify the ordinary upstream ZIP against an official release checksum.

Supports legacy ZIP.sha256 files and newer multi-file SHA256SUMS manifests.
Produces a canonical single-file checksum so the existing package.py can
continue applying its original strict approved-pin and checksum checks.
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import re
import sys

ZIP = re.compile(r"EnshroudedBuildingTools-\d+\.\d+\.\d+\.zip\Z")
SUM = re.compile(r"([0-9a-fA-F]{64}) [ *](\S+)\Z")
HEX = re.compile(r"[0-9a-fA-F]{64}\Z")


def official_hash(text: str, checksum_name: str, archive_name: str) -> str:
    if not ZIP.fullmatch(archive_name):
        raise ValueError("Not an official versioned Building Tools archive")
    if checksum_name not in (archive_name + ".sha256", "SHA256SUMS"):
        raise ValueError("Unexpected official checksum asset name")
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    if not lines:
        raise ValueError("Empty official checksum file")
    if checksum_name.endswith(".zip.sha256"):
        if len(lines) != 1:
            raise ValueError("Expected one SHA256 in the legacy checksum")
        if HEX.fullmatch(lines[0]):
            return lines[0].lower()
    found = []
    for line in lines:
        match = SUM.fullmatch(line)
        if not match:
            raise ValueError("Malformed official SHA256 checksum entry")
        name = match.group(2)
        if name in (archive_name, "dist/" + archive_name, "./" + archive_name):
            found.append(match.group(1).lower())
    if len(found) != 1:
        raise ValueError("Expected exactly one checksum for the ordinary upstream ZIP")
    return found[0]


def verify(archive: Path, published: Path, normalized: Path, expected: str | None = None) -> str:
    official = official_hash(published.read_text(encoding="ascii"), published.name, archive.name)
    actual = hashlib.sha256(archive.read_bytes()).hexdigest()
    if actual != official:
        raise ValueError("Upstream ZIP differs from its official published SHA256")
    if expected is not None:
        if not HEX.fullmatch(expected) or actual != expected.lower():
            raise ValueError("Upstream ZIP differs from the reviewed SHA256 pin")
    if normalized.resolve() in (archive.resolve(), published.resolve()):
        raise ValueError("Cannot overwrite the source ZIP or official checksum")
    normalized.write_text(f"{actual}  {archive.name}\n", encoding="ascii")
    return actual


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path, required=True)
    parser.add_argument("--checksum", type=Path, required=True)
    parser.add_argument("--normalized", type=Path, required=True)
    parser.add_argument("--expected-sha256", default=None)
    args = parser.parse_args()
    try:
        digest = verify(args.archive, args.checksum, args.normalized, args.expected_sha256)
    except (OSError, UnicodeError, ValueError) as error:
        parser.error(str(error))
    print(f"sha256={digest}")
    print(f"PASS: verified {args.archive.name} against {args.checksum.name}", file=sys.stderr)


if __name__ == "__main__":
    main()
