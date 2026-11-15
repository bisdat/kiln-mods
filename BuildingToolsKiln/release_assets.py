#!/usr/bin/env python3
"""Check that the latest *published* upstream release is ready for repackaging.

GitHub can publish a Release before uploading its binary assets. An assetless
release is pending, not a broken Kiln build, and must not create an upgrade PR.
Exits successfully but emits a GitHub Actions warning and ready=false; the
daily watcher checks again. Unrecognized release metadata fails closed.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys

VERSION_RE = re.compile(r"^v(\d+\.\d+\.\d+)$")


def inspect_release(info: dict) -> tuple[str, bool, list[str]]:
    if not isinstance(info, dict):
        raise ValueError("Expected the official GitHub Release JSON object")
    tag = info.get("tag_name", "")
    match = VERSION_RE.fullmatch(tag) if isinstance(tag, str) else None
    if not match:
        raise ValueError(f"Unexpected upstream release tag: {tag!r}")
    if info.get("draft") is True or info.get("prerelease") is True:
        raise ValueError("Upstream release must be published and stable")
    version = match.group(1)
    assets = info.get("assets")
    if not isinstance(assets, list):
        raise ValueError("Upstream release assets metadata is unavailable")
    by_name = {
        x["name"]: x for x in assets
        if isinstance(x, dict) and isinstance(x.get("name"), str)
    }
    required = (
        f"EnshroudedBuildingTools-{version}.zip",
        f"EnshroudedBuildingTools-{version}.zip.sha256",
    )
    pending = []
    for name in required:
        asset = by_name.get(name)
        if (asset is None or not isinstance(asset.get("size"), int)
                or isinstance(asset.get("size"), bool)
                or asset["size"] <= 0
                or asset.get("state") != "uploaded"):
            pending.append(name)
    return version, not pending, pending


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("release_json", type=Path)
    args = parser.parse_args()
    try:
        data = json.loads(args.release_json.read_text(encoding="utf-8"))
        version, ready, missing = inspect_release(data)
    except (OSError, ValueError, TypeError) as exc:
        parser.error(str(exc))
    # stdout is appended directly to GITHUB_OUTPUT; all human-readable
    # diagnostics go to stderr instead.
    print(f"version={version}")
    print(f"ready={'true' if ready else 'false'}")
    if ready:
        print(f"Official Building Tools v{version}: both assets uploaded",
              file=sys.stderr)
    else:
        print("::warning title=Building Tools upstream release incomplete::"
              f"v{version} is published but its required release assets are "
              f"not yet uploaded: {', '.join(missing)}. "
              "No update PR will be created; the daily check will retry.",
              file=sys.stderr)


if __name__ == "__main__":
    main()
