#!/usr/bin/env python3
"""Propose a newer upstream release. No install, no automatic release.

The original C++ code belongs to the Building Tools author and the
Kiln adapter repackages official release DLLs byte-for-byte.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re

VERSION = re.compile(r"^(?:v)?(\d+)\.(\d+)\.(\d+)$")
HASH = re.compile(r"^[0-9a-f]{64}$")


def version(s: str) -> tuple[int, int, int]:
    m = VERSION.fullmatch(s)
    if not m:
        raise ValueError("Version must be a strict X.Y.Z release")
    return tuple(map(int, m.groups()))


def propose(pin: dict, upstream_version: str, sha256: str) -> dict | None:
    incoming = version(upstream_version)
    current = version(pin["upstream_version"])
    digest = sha256.lower()
    if not HASH.fullmatch(digest):
        raise ValueError("Upstream SHA256 must contain exactly 64 hex digits")
    if incoming == current:
        if digest != pin["upstream_sha256"]:
            raise ValueError("CRITICAL: same upstream tag has different archive bytes")
        return None
    if incoming < current:
        return None
    adapter = version(pin["adapter_version"])
    if adapter[2] >= 9999:
        raise ValueError("Adapter patch version limit; pick a new version manually")
    candidate = dict(pin)
    candidate["adapter_version"] = f"{adapter[0]}.{adapter[1]}.{adapter[2]+1}"
    candidate["upstream_version"] = ".".join(map(str, incoming))
    candidate["upstream_sha256"] = digest
    # Gameplay verification is deliberately NOT forwarded to a new binary.
    # The release workflow must block until in-game testing is confirmed.
    return candidate


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--upstream-version", required=True)
    p.add_argument("--sha256", required=True)
    p.add_argument("--profile", type=Path,
                   default=Path(__file__).with_name("release.json"))
    args = p.parse_args()
    try:
        original = json.loads(args.profile.read_text(encoding="utf-8"))
        candidate = propose(original, args.upstream_version, args.sha256)
    except (OSError, ValueError, KeyError) as exc:
        p.error(str(exc))
    if candidate is None:
        print("CURRENT: no newer upstream version")
        return
    args.profile.write_text(json.dumps(candidate, indent=2) + "\n", encoding="utf-8")
    print("DRAFT: upstream " + candidate["upstream_version"] +
          ", Kiln adapter " + candidate["adapter_version"] +
          "; original gameplay verification retained, not transferred.")


if __name__ == "__main__":
    main()
