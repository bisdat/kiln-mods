#!/usr/bin/env python3
"""Download the exact official Kiln SDK release and verify every package."""
from __future__ import annotations

import hashlib
import re
from pathlib import Path
from urllib.request import Request, urlopen

VERSION = "0.2.0-dev.30.6"
TAG = f"v{VERSION}"
BASE = f"https://github.com/frostdev-ops/kiln/releases/download/{TAG}"
DEST = Path(__file__).resolve().parents[1] / ".kiln-mdk" / "nuget"

def download(name: str) -> bytes:
    req = Request(f"{BASE}/{name}", headers={"User-Agent": "bisdat-rubble-remover-ci"})
    with urlopen(req, timeout=90) as response:
        data = response.read()
    if not data:
        raise ValueError(f"Empty release asset: {name}")
    return data

def main() -> None:
    sums = download("SHA256SUMS").decode("utf-8-sig")
    names = [f"Kiln.Sdk.{VERSION}.nupkg", f"Kiln.Api.{VERSION}.nupkg"]
    DEST.mkdir(parents=True, exist_ok=True)
    for name in names:
        match = re.search(rf"^([0-9a-fA-F]{{64}})\\s+\\*?{re.escape(name)}$", sums, re.MULTILINE)
        if not match:
            raise ValueError(f"No official SHA-256 for {name}")
        data = download(name)
        actual = hashlib.sha256(data).hexdigest()
        if actual != match.group(1).lower():
            raise ValueError(f"Official SHA-256 mismatch for {name}")
        (DEST / name).write_bytes(data)
        print(f"Verified official {name}: {actual}")
if __name__ == "__main__":
    main()
