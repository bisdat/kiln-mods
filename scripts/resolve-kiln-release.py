#!/usr/bin/env python3
"""Resolve only a published, complete Kiln development release.

No fallback to an old version and no moving-branch SDK selection.
The build independently verifies the tag commit and official asset checksums.
"""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import re
import sys
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

URL = "https://api.github.com/repos/frostdev-ops/kiln/releases?per_page=100"
TAG = re.compile(r"^v(?P<version>\d+\.\d+\.\d+-dev\.\d+)$", re.ASCII)
SHA = re.compile(r"^[0-9a-f]{40}$", re.ASCII)


def select_release(releases: list[dict], requested_tag: str | None = None) -> dict:
    if requested_tag and TAG.fullmatch(requested_tag) is None:
        raise ValueError(f"Invalid pinned Kiln release tag: {requested_tag!r}")
    candidates = [
        r for r in releases
        if r.get("draft") is False
        and r.get("prerelease") is True
        and isinstance(r.get("tag_name"), str)
        and TAG.fullmatch(r["tag_name"])
        and isinstance(r.get("published_at"), str)
        and bool(r["published_at"])
    ]
    if requested_tag:
        candidates = [r for r in candidates if r["tag_name"] == requested_tag]
    if not candidates:
        raise ValueError("No verified published Kiln dev release (no older fallback)")
    r = max(candidates, key=lambda x: (x["published_at"], x["tag_name"]))
    v = TAG.fullmatch(r["tag_name"]).group("version")
    assets = {a.get("name") for a in r.get("assets", []) if isinstance(a, dict)}
    required = {f"Kiln.Sdk.{v}.nupkg", f"Kiln.Api.{v}.nupkg", "SHA256SUMS"}
    missing = required - assets
    if missing:
        raise ValueError(f"Newest Kiln release {r['tag_name']} is missing assets: {sorted(missing)}")
    target = r.get("target_commitish")
    if not isinstance(target, str) or not SHA.fullmatch(target):
        raise ValueError("Release target_commitish is not an immutable 40-digit SHA")
    return {
        "version": v,
        "tag": r["tag_name"],
        "published_at": r["published_at"],
        "release_url": r["html_url"],
        "release_target": target,
    }


def get_releases() -> list[dict]:
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "bisdat-kiln-mods-ci",
        "X-GitHub-Api-Version": "2022-11-28",
    }
    if token := os.getenv("GITHUB_TOKEN"):
        headers["Authorization"] = f"Bearer {token}"
    with urlopen(Request(URL, headers=headers), timeout=30) as response:
        result = json.load(response)
    if not isinstance(result, list):
        raise ValueError("GitHub Releases returned a non-array response")
    return result


def save(root: Path, release: dict) -> None:
    (root / ".kiln-release.json").write_text(
        json.dumps(release, indent=2) + "\n", encoding="utf-8"
    )
    if path := os.getenv("GITHUB_OUTPUT"):
        with open(path, "a", encoding="utf-8") as stream:
            stream.write(f"tag={release['tag']}\n")
            stream.write(f"version={release['version']}\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        release = select_release(get_releases(), os.getenv("KILN_RELEASE_TAG") or None)
        save(args.root.resolve(), release)
    except (ValueError, KeyError, OSError, HTTPError, URLError) as exc:
        print(f"Kiln release verification FAILED (no stale fallback): {exc}", file=sys.stderr)
        return 1
    print(
        f"Kiln official release: {release['tag']} on {release['published_at']} "
        f"target={release['release_target']} url={release['release_url']}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
