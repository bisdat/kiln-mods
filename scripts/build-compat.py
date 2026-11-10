#!/usr/bin/env python3
"""Build active mods using checksum-verified official Kiln release SDK assets.

Does not alter pinned stable-mod SDK references in Git: the temporary project
rewrite is reverted in a finally clause. Archive content is never built.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
MOD_IDS = {"SurvivalQoL": "survival-qol", "EndlessChests": "endless-chests-kiln"}
SHA = re.compile(r"[0-9a-f]{40}")
DIGEST = re.compile(r"[0-9a-fA-F]{64}")
TAG = re.compile(r"v\d+\.\d+\.\d+-dev\.\d+")


def run(*args: str, cwd: Path = ROOT) -> str:
    print("+", " ".join(args), flush=True)
    return subprocess.check_output(args, cwd=cwd, text=True).strip()


def download(url: str, destination: Path) -> bytes:
    destination.parent.mkdir(parents=True, exist_ok=True)
    request = Request(url, headers={"User-Agent": "bisdat-kiln-mods-ci"})
    for attempt in range(3):
        try:
            with urlopen(request, timeout=90) as stream:
                data = stream.read()
            if not data:
                raise ValueError(f"Empty official release asset: {url}")
            destination.write_bytes(data)
            return data
        except (OSError, ValueError):
            if attempt == 2:
                raise
            time.sleep(2 * (attempt + 1))
    raise AssertionError("unreachable")


def expected_hash(sums: bytes, filename: str) -> str:
    for line in sums.decode("utf-8-sig").splitlines():
        parts = line.strip().split()
        if len(parts) == 2 and parts[1].lstrip("*") == filename:
            value = parts[0]
            if not DIGEST.fullmatch(value):
                break
            return value.lower()
    raise ValueError(f"Official SHA256SUMS has no verified entry for {filename}")


def build(mod: str) -> None:
    if mod not in MOD_IDS:
        raise ValueError(f"Not an active mod: {mod}")
    project = ROOT / mod / (mod + ".csproj")
    if not project.is_file():
        raise FileNotFoundError(project)

    # Resolve/pin a real published release in this independent CI job.
    run(sys.executable, str(ROOT / "scripts/resolve-kiln-release.py"), "--root", str(ROOT))
    release = json.loads((ROOT / ".kiln-release.json").read_text("utf-8"))
    version, tag, target = (release[k] for k in ("version", "tag", "release_target"))
    if not TAG.fullmatch(tag) or not SHA.fullmatch(target):
        raise ValueError("Release tag or target SHA not validated")
    if os.environ.get("KILN_SDK_VERSION") not in (None, "", version):
        raise ValueError(f"Expected {os.environ['KILN_SDK_VERSION']} but release is {version}")

    # Official tag commit is pinned and verified; never use moving main.
    upstream = ROOT / ".kiln-upstream"
    if not (upstream / ".git").exists():
        upstream.mkdir(parents=True, exist_ok=True)
        run("git", "init", "-q", cwd=upstream)
        run("git", "remote", "add", "origin", "https://github.com/frostdev-ops/kiln.git", cwd=upstream)
    run("git", "fetch", "--depth=1", "origin", f"refs/tags/{tag}", cwd=upstream)
    commit = run("git", "rev-parse", "FETCH_HEAD^{commit}", cwd=upstream)
    if commit != target:
        raise ValueError(f"Published Kiln commit mismatch: tag={commit}, release={target}")
    run("git", "-c", "advice.detachedHead=false", "checkout", "-q", "--detach", commit, cwd=upstream)

    # Load official published NuGet assets, checking each against SHA256SUMS.
    feed = ROOT / ".kiln-mdk" / "nuget"
    url = f"https://github.com/frostdev-ops/kiln/releases/download/{tag}"
    sums = download(url + "/SHA256SUMS", feed / f"{tag}-SHA256SUMS")
    hashes = {}
    for filename in (f"Kiln.Sdk.{version}.nupkg", f"Kiln.Api.{version}.nupkg"):
        data = download(url + "/" + filename, feed / filename)
        actual = hashlib.sha256(data).hexdigest()
        expected = expected_hash(sums, filename)
        if actual != expected:
            raise ValueError(f"Official release checksum mismatch: {filename}")
        hashes[filename] = actual

    # Stable release project files retain their original older SDK pins.
    # Revert temporary SDK version selection on success or failure.
    original = project.read_bytes()
    try:
        text = original.decode("utf-8")
        edited, matches = re.subn(
            r'<Project Sdk="Kiln\.Sdk/[0-9A-Za-z.+-]+">',
            f'<Project Sdk="Kiln.Sdk/{version}">',
            text,
            count=1,
        )
        if matches != 1:
            raise ValueError(f"Missing exact Kiln.Sdk reference in {project}")
        project.write_text(edited, encoding="utf-8")
        run("dotnet", "restore", str(project), "--configfile", str(ROOT / "NuGet.Config"))
        run("dotnet", "build", str(project), "-c", "Release", "--no-restore")
        run("dotnet", "build", str(project), "-c", "Release", "--no-restore", "-t:KilnPack")
    finally:
        project.write_bytes(original)

    source_sha = run("git", "rev-parse", "HEAD")
    mod_version = re.search(r"<Version>([^<]+)</Version>", original.decode("utf-8"))
    if not mod_version:
        raise ValueError(f"Missing mod version in {project}")
    package = ROOT / "bin" / "Release" / f"{MOD_IDS[mod]}-{mod_version.group(1)}.zip"
    if not package.is_file() or package.stat().st_size <= 0:
        raise ValueError(f"Missing packaged mod ZIP: {package}")

    release.update({
        "checked_out_sha": commit,
        "mod_source_sha": source_sha,
        "mod": mod,
        "official_assets_sha256": hashes,
    })
    output = ROOT / "bin" / "Release" / "kiln-sdk-provenance.json"
    output.write_text(json.dumps(release, indent=2) + "\n", encoding="utf-8")
    print(f"SUCCESS: {mod} built against official {tag}@{commit}: {package}")


def main() -> int:
    try:
        if len(sys.argv) != 2:
            raise ValueError("usage: build-compat.py SurvivalQoL|EndlessChests")
        build(sys.argv[1])
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        print(f"Kiln compatibility build FAILED: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
