#!/usr/bin/env python3
"""Assemble a version-locked, NON-INSTALLING lab archive from two CI artifacts.

Only the *same* immutable upstream Kiln dev release is acceptable. Nothing
is written to the game directory, user mod library or GitHub Releases.
"""
from __future__ import annotations

import argparse
from hashlib import sha256
import json
import os
from pathlib import Path
import re
import subprocess
from zipfile import ZipFile, ZIP_DEFLATED

TAG_RE = re.compile(r"^v\d+\.\d+\.\d+-dev\.\d+$")
SHA_RE = re.compile(r"^[0-9a-f]{40}$")


def check_binary(path: Path, kind: str) -> bytes:
    binary = path.read_bytes()
    if len(binary) < 4096 or binary[:2] != b"MZ":
        raise ValueError(f"{kind} is not a plausible compiled Windows DLL: {path}")
    return binary


def assemble(native: Path, managed: Path, observer: Path,
             info_path: Path, output: Path, mod_commit: str,
             expected_tag: str, expected_sha: str) -> dict:
    metadata = json.loads(info_path.read_text(encoding="utf-8"))
    if not TAG_RE.fullmatch(expected_tag) or not SHA_RE.fullmatch(expected_sha):
        raise ValueError("Expected immutable published Kiln tag + full SHA")
    if metadata.get("tag") != expected_tag or metadata.get("version") != expected_tag.removeprefix("v"):
        raise ValueError("Observer Kiln SDK release mismatch")
    if metadata.get("release_target") != expected_sha or metadata.get("checked_out_sha") != expected_sha:
        raise ValueError("SDK checkout does not match immutable upstream native service source")
    if not SHA_RE.fullmatch(mod_commit):
        raise ValueError("Invalid mod source revision")
    blobs = {
        # Deliberately under STAGING-ONLY, not install-ready paths:
        "STAGING-ONLY/kiln/services/kiln-rules.dll": check_binary(native, "native"),
        "STAGING-ONLY/kiln/host/Kiln.Rules.dll": check_binary(managed, "managed"),
        "observer/craft-payment-trace-0.1.1.zip": observer.read_bytes(),
    }
    with ZipFile(observer) as mod:
        names = mod.namelist()
        if not any(n.endswith("/CraftPaymentTrace.dll") for n in names):
            raise ValueError("Observer mod ZIP missing CraftPaymentTrace.dll")
        if any(n.lower().endswith(("/kiln-rules.dll", "/kiln.rules.dll",
                                   "/kiln-core.dll", "/dinput8.dll")) for n in names):
            raise ValueError("Observer mod ZIP contains native/runtime service DLL")
    assets = [
        {"path": name, "size": len(payload), "sha256": sha256(payload).hexdigest()}
        for name, payload in sorted(blobs.items())
    ]
    manifest = {
        "kind": "EXPERIMENTAL-NOT-INSTALLABLE-LAB-ONLY",
        "kiln_tag": expected_tag,
        "kiln_release_sha": expected_sha,
        "mod_source_sha": mod_commit,
        "runtime_patched": False,
        "direct_game_install": False,
        "assets": assets,
    }
    note = f"""EXPERIMENTAL CRAFT PAYMENT TRACE — LAB MATERIALS ONLY
======================================================
Kiln release: {expected_tag}
Upstream source commit: {expected_sha}
Observer mod source commit: {mod_commit}

DO NOT extract this archive directly over your Enshrouded/Kiln installation.
Its modified native and managed Rules services have only passed source-level
and compilation checks; they have NOT been tested in a running game. The
original payer methods are called unmodified, but merely reading invalid
native memory or installing a mismatched service could still crash the game.
The zip intentionally stores DLLs under STAGING-ONLY/, not kiln/.

The optional managed observer mod is in observer/. It is harmless on standard
Kiln: cbtrace status reports UNAVAILABLE until the exact paired native AND
managed experimental services are loaded. This bundle does not install them.

If you later decide to try the service pair, use ONLY a disposable test
installation, a backed-up world/save, the EXACT Kiln release above, full
backups of kiln/services/kiln-rules.dll and kiln/host/Kiln.Rules.dll, a
stopped game, and a verified rollback plan. Never let Kiln update the
services mid-session. No installation is performed by CI.

Even a working trace reports only RAW, UNVERIFIED recipe cost rows and STAGED
journal operations; it does not validate category inputs, remote source
permissions or final transaction commits. No remote crafting is enabled.
"""
    output.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(output, "w", compression=ZIP_DEFLATED) as archive:
        for name, body in blobs.items():
            archive.writestr(name, body)
        archive.writestr("MANIFEST.json", json.dumps(manifest, indent=2) + "\n")
        archive.writestr("README-LAB-ONLY.txt", note)
    with ZipFile(output) as archive:
        for asset in assets:
            if sha256(archive.read(asset["path"])).hexdigest() != asset["sha256"]:
                raise ValueError("Archive integrity verification failed")
    print(f"PASS: lab archive {output}: {len(assets)} version-locked files; no game installation.")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native", type=Path, required=True)
    parser.add_argument("--managed", type=Path, required=True)
    parser.add_argument("--observer", type=Path, required=True)
    parser.add_argument("--release", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    mod_sha = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    assemble(args.native, args.managed, args.observer, args.release, args.out,
             mod_sha, os.environ["KILN_RELEASE_TAG"], os.environ["KILN_UPSTREAM_SHA"])


if __name__ == "__main__":
    main()
