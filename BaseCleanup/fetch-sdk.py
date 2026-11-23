#!/usr/bin/env python3
"""Fetch checksum-verified Kiln 0.2.0-dev.30.6 SDK packages for this candidate."""
from __future__ import annotations

import hashlib
import re
from pathlib import Path
from urllib.request import Request, urlopen

VERSION = "0.2.0-dev.30.6"
TAG = "v" + VERSION
ROOT = Path(__file__).resolve().parents[1]
FEED = ROOT / ".kiln-mdk" / "nuget"
BASE = f"https://github.com/frostdev-ops/kiln/releases/download/{TAG}/"


def download(filename: str) -> bytes:
    req = Request(BASE + filename, headers={"User-Agent": "bisdat-base-cleanup-stage1"})
    with urlopen(req, timeout=90) as response:
        data = response.read()
    if not data:
        raise RuntimeError("empty upstream asset: " + filename)
    return data


def main() -> None:
    FEED.mkdir(parents=True, exist_ok=True)
    manifest = download("SHA256SUMS").decode("utf-8-sig")
    sums = {}
    for line in manifest.splitlines():
        parts = line.split()
        if len(parts) == 2 and re.fullmatch(r"[0-9a-fA-F]{64}", parts[0]):
            sums[parts[1].lstrip("*")] = parts[0].lower()
    for name in (f"Kiln.Sdk.{VERSION}.nupkg", f"Kiln.Api.{VERSION}.nupkg"):
        expected = sums.get(name)
        if expected is None:
            raise RuntimeError(f"upstream SHA256SUMS is missing {name}")
        data = download(name)
        actual = hashlib.sha256(data).hexdigest()
        if actual != expected:
            raise RuntimeError(f"SHA-256 mismatch for {name}: {actual} != {expected}")
        (FEED / name).write_bytes(data)
        print(f"Verified {name}: {actual}")


if __name__ == "__main__":
    main()
