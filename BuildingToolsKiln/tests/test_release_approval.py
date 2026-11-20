#!/usr/bin/env python3
"""Regression contracts for automatic CI-verified Building Tools prereleases.

Game testing remains a separate acceptance signal; CI never claims it.
"""
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
DIR = ROOT / "BuildingToolsKiln"
PROFILE = DIR / "release.json"
WATCHER = ROOT / ".github/workflows/buildingtools-upstream-watch.yml"
PACKAGE_WORKFLOW = ROOT / ".github/workflows/buildingtools-kiln-native-adapter.yml"


class AutomaticReleaseTests(unittest.TestCase):
    def test_profile_retains_real_gameplay_acceptance_not_owner_gate(self):
        pin = json.loads(PROFILE.read_text())
        self.assertRegex(pin["upstream_version"], r"^\d+\.\d+\.\d+$")
        self.assertRegex(pin["adapter_version"], r"^\d+\.\d+\.\d+$")
        self.assertRegex(pin["gameplay_verified_upstream_version"], r"^\d+\.\d+\.\d+$")
        self.assertIn(pin["gameplay_verified_upstream_version"], pin["gameplay_acceptance_note"])
        self.assertNotIn("release_approved_upstream_version", pin)
        self.assertNotIn("release_approval_note", pin)

    def test_candidate_advances_only_version_pin_and_preserves_gameplay(self):
        spec = importlib.util.spec_from_file_location(
            "building_tools_propose", DIR / "propose_update.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        pin = json.loads(PROFILE.read_text())
        major, minor, patch = map(int, pin["upstream_version"].split("."))
        candidate = module.propose(pin, f"{major}.{minor}.{patch+1}", "f"*64)
        self.assertIsNotNone(candidate)
        self.assertEqual(candidate["gameplay_verified_upstream_version"],
                         pin["gameplay_verified_upstream_version"])
        self.assertEqual(candidate["gameplay_acceptance_note"],
                         pin["gameplay_acceptance_note"])
        self.assertEqual(candidate["tested_client_sha256"], pin["tested_client_sha256"])
        self.assertNotEqual(candidate["upstream_version"],
                            candidate["gameplay_verified_upstream_version"])

    def test_watcher_performs_full_build_and_prerelease_in_same_run(self):
        w = WATCHER.read_text()
        self.assertIn("concurrency:", w)
        self.assertIn("cancel-in-progress: false", w)
        self.assertIn("contents: write", w)
        self.assertIn("BuildingToolsKiln/official_checksum.py", w)
        self.assertIn("BuildingToolsKiln/propose_update.py", w)
        self.assertIn("python3 -m unittest discover -s BuildingToolsKiln/tests", w)
        self.assertIn("BuildingToolsKiln/package.py", w)
        self.assertIn("cmp \"dist/building-tools-kiln-$ADAPTER.zip\"", w)
        self.assertIn('provenance["gameplay_verified"] is False', w)
        self.assertIn("gh release create", w)
        self.assertIn("--verify-tag --prerelease --latest=false", w)
        self.assertIn("git push origin HEAD:refs/heads/automation/building-tools-release-pins", w)
        self.assertIn('git merge --no-edit refs/remotes/origin/main', w)
        self.assertNotIn("git push origin HEAD:refs/heads/main", w)
        self.assertIn('test "$REMOTE_SHA" = "$GITHUB_SHA"', w)
        self.assertNotIn("gh pr create", w)
        self.assertNotIn("gh issue create", w)
        self.assertNotIn("release_approved_upstream_version", w)

    def test_missing_release_is_recovered_even_with_existing_pin(self):
        w = WATCHER.read_text()
        self.assertIn("if gh release view \"$TAG\"", w)
        self.assertIn('echo "publish=true" >> "$GITHUB_OUTPUT"', w)
        self.assertIn("if git diff --quiet -- BuildingToolsKiln/release.json", w)
        self.assertIn("Existing release $TAG retained without changes", w)
        self.assertIn('refs/tags/$TAG^{commit}', w)

    def test_untested_release_never_looks_stable_or_installed(self):
        w = WATCHER.read_text()
        self.assertIn("AUTOMATED CI-VERIFIED PRERELEASE — GAMEPLAY UNTESTED", w)
        self.assertIn("This release has NOT been verified in Enshrouded", w)
        self.assertIn("Back up saves", w)
        self.assertNotIn("gameplay_verified_upstream_version", w)
        self.assertNotIn("--latest=true", w)

    def test_generic_package_workflow_only_validates(self):
        p = PACKAGE_WORKFLOW.read_text()
        self.assertIn("Packaging and upstream-update regression tests", p)
        self.assertIn("Verify", p)
        self.assertNotIn("release_permitted", p)
        self.assertNotIn("gh release create", p)
        self.assertNotIn("\n  release:\n", p)


if __name__ == "__main__":
    unittest.main()
