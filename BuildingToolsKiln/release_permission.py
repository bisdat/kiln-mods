#!/usr/bin/env python3
"""Read-only release permission check for Building Tools packaging.

An unverified upstream update is not a packaging failure. Publication remains
opt-in, specific to the exact upstream version, and never inferred from the
adapter version or an approval for an earlier upstream version.
"""
import json
from pathlib import Path
import sys


def release_eligibility(profile):
    upstream = profile["upstream_version"]
    if upstream == profile["gameplay_verified_upstream_version"]:
        return True, "gameplay_verified"

    manual = profile.get("release_approved_upstream_version") == upstream
    note = profile.get("release_approval_note")
    if manual and isinstance(note, str) and note.strip():
        return True, "owner_approved_unverified"

    return False, "awaiting_gameplay_verification_or_version_specific_approval"


if __name__ == "__main__":
    profile = json.loads(Path(__file__).with_name("release.json").read_text(encoding="utf-8"))
    permitted, reason = release_eligibility(profile)
    # GitHub Actions caller redirects stdout to GITHUB_OUTPUT.
    print("permitted=" + ("true" if permitted else "false"))
    print(f"Building Tools upstream v{profile['upstream_version']}: {reason}; " +
          ("release allowed" if permitted else "release skipped; packaging stays valid"),
          file=sys.stderr)
