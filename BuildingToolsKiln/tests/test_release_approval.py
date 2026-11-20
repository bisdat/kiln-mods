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
        current = pin["upstream_version"]
        approved = pin["release_approved_upstream_version"]
        verified = pin["gameplay_verified_upstream_version"]

        # The watcher deliberately tests the *proposed* release.json, where
        # current is new but the older approval and gameplay signoff must
        # remain unchanged. Never hard-code the current production version.
        for value in (current, approved, verified):
            self.assertRegex(value, r"^\d+\.\d+\.\d+$")
        version = lambda v: tuple(map(int, v.split(".")))
        self.assertLessEqual(version(approved), version(current))
        self.assertLessEqual(version(verified), version(current))

        # Notes describe the versions they actually approved or tested.
        self.assertIn(approved, pin["release_approval_note"])
        self.assertIn(verified, pin["gameplay_acceptance_note"])
        self.assertIn("tested and working in game", pin["gameplay_acceptance_note"])

    def test_new_upstream_proposal_does_not_inherit_release_approval(self):
        import importlib.util

        updater = PROFILE.with_name("propose_update.py")
        spec = importlib.util.spec_from_file_location("building_tools_propose", updater)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        pin = json.loads(PROFILE.read_text())
        major, minor, patch = map(int, pin["upstream_version"].split("."))
        candidate = module.propose(pin, f"{major}.{minor}.{patch + 1}", "f" * 64)
        self.assertIsNotNone(candidate)
        self.assertNotEqual(candidate["upstream_version"], pin["upstream_version"])
        for field in (
            "gameplay_verified_upstream_version",
            "gameplay_acceptance_note",
            "release_approved_upstream_version",
            "release_approval_note",
        ):
            self.assertEqual(candidate[field], pin[field])
        self.assertNotEqual(candidate["upstream_version"], candidate["gameplay_verified_upstream_version"])
        self.assertNotEqual(candidate["upstream_version"], candidate["release_approved_upstream_version"])

    def test_release_permission_cases(self):
        import importlib.util

        helper = PROFILE.with_name("release_permission.py")
        spec = importlib.util.spec_from_file_location("building_tools_release_permission", helper)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        # Future upstream releases must not inherit an earlier owner's approval.
        pin = dict(json.loads(PROFILE.read_text()))
        pin.update(
            upstream_version="99.0.0",
            gameplay_verified_upstream_version="98.0.0",
            release_approved_upstream_version="98.0.0",
            release_approval_note="Owner approved only upstream v98.0.0.",
        )
        self.assertEqual(
            module.release_eligibility(pin),
            (False, "awaiting_gameplay_verification_or_version_specific_approval"),
        )

        # An unverified upstream version requires both an exact-version owner
        # approval AND a meaningful release note; then only prerelease is allowed.
        pin["release_approved_upstream_version"] = "99.0.0"
        pin["release_approval_note"] = "  "
        self.assertFalse(module.release_eligibility(pin)[0])
        pin["release_approval_note"] = "Owner explicitly approved 99.0.0 for prerelease."
        self.assertEqual(
            module.release_eligibility(pin), (True, "owner_approved_unverified")
        )

        # Gameplay verification of exactly this upstream version allows release.
        pin["gameplay_verified_upstream_version"] = "99.0.0"
        pin["release_approved_upstream_version"] = "98.0.0"
        pin["release_approval_note"] = ""
        self.assertEqual(module.release_eligibility(pin), (True, "gameplay_verified"))

    def test_unapproved_release_is_skipped_without_failing_packaging(self):
        content = WORKFLOW.read_text()
        self.assertIn(
            "release_permitted: ${{ steps.release_gate.outputs.permitted }}", content
        )
        self.assertIn(
            "python3 BuildingToolsKiln/release_permission.py >> \"$GITHUB_OUTPUT\"",
            content,
        )
        self.assertIn(
            "needs.package.outputs.release_permitted == 'true'", content
        )
        # An eligible job still independently checks the release approval.
        self.assertIn(
            'assert verified or manual, "New upstream version requires gameplay',
            content,
        )

    def test_unverified_release_requires_prerelease_warning(self):
        content = WORKFLOW.read_text()
        self.assertIn('p.get("release_approved_upstream_version")==p["upstream_version"]', content)
        self.assertIn('print("prerelease="+("false" if verified else "true"))', content)
        self.assertIn('release_flags+=(--prerelease)', content)
        self.assertIn("GAMEPLAY VERIFICATION PENDING", content)
        self.assertIn("gh release edit", content)
        self.assertIn("cmp ", content)


if __name__ == "__main__":
    unittest.main()
