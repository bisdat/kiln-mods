#!/usr/bin/env python3
"""Check that the latest *published* upstream release is ready for repackaging.

GitHub can publish a Release before uploading its binary assets. An assetless
release is pending, not a broken Kiln build, and must not create an upgrade PR.
Accepts either the legacy ZIP.sha256 or the new SHA256SUMS manifest.
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
    archive = f"EnshroudedBuildingTools-{version}.zip"
    legacy = archive + ".sha256"

    def uploaded(name: str) -> bool:
        asset = by_name.get(name)
        return (isinstance(asset, dict) and
                isinstance(asset.get("size"), int) and
                not isinstance(asset["size"], bool) and asset["size"] > 0
                and asset.get("state") == "uploaded")

    # The upstream releases now use SHA256SUMS instead of a ZIP.sha256.
    pending = []
    if not uploaded(archive):
        pending.append(archive)
    if not (uploaded(legacy) or uploaded("SHA256SUMS")):
        pending.append(f"{legacy} or SHA256SUMS")
    return version, not pending, pending

def checksum_asset(info: dict) -> str | None:
    """Return the official uploaded checksum asset for the ordinary ZIP."""
    version, ready, _ = inspect_release(info)
    if not ready:
        return None
    archive = f"EnshroudedBuildingTools-{version}.zip"
    for name in (archive + ".sha256", "SHA256SUMS"):
        for asset in info["assets"]:
            if (isinstance(asset, dict) and asset.get("name") == name
                    and isinstance(asset.get("size"), int)
                    and not isinstance(asset["size"], bool)
                    and asset["size"] > 0
                    and asset.get("state") == "uploaded"):
                return name
    raise AssertionError("ready release has no uploaded checksum")


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
        asset = checksum_asset(data)
        print(f"checksum_asset={asset}")
        print(f"Official Building Tools v{version}: ZIP and {asset} uploaded",
              file=sys.stderr)
    else:
        print("::warning title=Building Tools upstream release incomplete::"
              f"v{version} is published but its required release assets are "
              f"not yet uploaded: {', '.join(missing)}. "
              "No update PR will be created; the daily check will retry.",
              file=sys.stderr)


if __name__ == "__main__":
    main()
