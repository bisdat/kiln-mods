#!/usr/bin/env python3
"""Select newest *published* Kiln dev prerelease (not moving main).

Run this before packing any of this repository's Kiln mods. Does not
download or install Kiln; build.sh subsequently fetches the immutable tag.
The last cached/known version is NEVER used when release verification fails.
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
SHA = re.compile(r"^[0-9a-fA-F]{40}$")


def select_release(releases: list[dict], requested_tag: str | None = None) -> dict:
    candidates = [
        r for r in releases
        if r.get("draft") is False
        and r.get("prerelease") is True
        and isinstance(r.get("tag_name"), str)
        and TAG.fullmatch(r["tag_name"])
        and isinstance(r.get("published_at"), str)
        and r["published_at"]
    ]
    if requested_tag:
        if not TAG.fullmatch(requested_tag):
            raise ValueError(f"Invalid pinned release tag: {requested_tag!r}")
        candidates = [r for r in candidates if r["tag_name"] == requested_tag]
    if not candidates:
        raise ValueError("No verified published Kiln development release found (refusing older fallback).")
    # Publication timestamp matters: don't build a branch or an unpublished tag.
    selected = max(candidates, key=lambda r: (r["published_at"], r["tag_name"]))
    version = TAG.fullmatch(selected["tag_name"]).group("version")
    assets = {a.get("name") for a in selected.get("assets", []) if isinstance(a, dict)}
    for required in (f"Kiln.Sdk.{version}.nupkg", f"Kiln.Api.{version}.nupkg"):
        if required not in assets:
            raise ValueError(f"Newest published Kiln dev release {selected['tag_name']} is missing {required}; refusing fallback.")
    if not isinstance(selected.get("target_commitish"), str) or not selected["target_commitish"]:
        raise ValueError("Release target_commitish is missing.")
    return {
        "version": version,
        "tag": selected["tag_name"],
        "published_at": selected["published_at"],
        "release_url": selected.get("html_url", ""),
        "release_target": selected["target_commitish"],
    }


def get_releases() -> list[dict]:
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "bisdat-kiln-mods-ci",
        "X-GitHub-Api-Version": "2022-11-28",
    }
    if token := os.environ.get("GITHUB_TOKEN"):
        headers["Authorization"] = f"Bearer {token}"
    with urlopen(Request(URL, headers=headers), timeout=25) as resp:
        releases = json.load(resp)
    if not isinstance(releases, list):
        raise ValueError("GitHub Releases returned a non-array response.")
    return releases


def write_files(root: Path, release: dict) -> None:
    global_path = root / "global.json"
    current = json.loads(global_path.read_text(encoding="utf-8")) if global_path.exists() else {}
    if not isinstance(current, dict):
        raise ValueError("global.json must contain a JSON object.")
    versions = current.setdefault("msbuild-sdks", {})
    if not isinstance(versions, dict):
        raise ValueError("global.json msbuild-sdks must contain a JSON object.")
    versions["Kiln.Sdk"] = release["version"]
    global_path.write_text(json.dumps(current, indent=2) + "\n", encoding="utf-8")
    # The only values we source into Bash are regex-checked tag/version tokens.
    (root / ".kiln-release.env").write_text(
        f"export KILN_UPSTREAM_TAG={release['tag']}\n"
        f"export KILN_SDK_VERSION={release['version']}\n",
        encoding="ascii",
    )
    (root / ".kiln-release.json").write_text(
        json.dumps(release, indent=2) + "\n", encoding="utf-8"
    )
    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        with open(output, "a", encoding="utf-8") as f:
            f.write(f"tag={release['tag']}\nversion={release['version']}\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        release = select_release(get_releases(), os.environ.get("KILN_RELEASE_TAG"))
        write_files(args.root.resolve(), release)
    except (ValueError, KeyError, OSError, HTTPError, URLError) as exc:
        print(f"Kiln release resolution FAILED (no stale fallback): {exc}", file=sys.stderr)
        return 1
    print(
        f"Kiln upstream: {release['tag']} ({release['published_at']}); "
        f"release={release['release_url']}; SDK={release['version']}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
