#!/usr/bin/env python3
"""Guard manual release approval from being mistaken for gameplay acceptance."""
import json
from pathlib import Path
import unittest

PROFILE = Path(__file__).resolve().parents[1] / "release.json"
WORKFLOW = Path(__file__).resolve().parents[2] / ".github/workflows/buildingtools-kiln-native-adapter.yml"


class ReleaseApprovalTests(unittest.TestCase):
    def test_version_specific_owner_signoff_is_separate_from_gameplay(self):
        pin = json.loads(PROFILE.read_text())
        self.assertEqual(pin["upstream_version"], "0.33.2")
        self.assertEqual(pin["release_approved_upstream_version"], "0.33.2")
        self.assertEqual(pin["gameplay_verified_upstream_version"], "0.31.2")
        self.assertTrue(pin["release_approval_note"])

    def test_unverified_release_requires_prerelease_warning(self):
        content = WORKFLOW.read_text()
        self.assertIn('p.get("release_approved_upstream_version")==p["upstream_version"]', content)
        self.assertIn('print("prerelease="+("false" if verified else "true"))', content)
        self.assertIn('release_flags+=(--prerelease)', content)
        self.assertIn("GAMEPLAY VERIFICATION PENDING", content)


if __name__ == "__main__":
    unittest.main()
