"""Guard the official Kiln release follower against stale Endless Chests tests.

The compatibility workflow is a separate YAML from the normal packaging
workflow. Moving from INI groups to a canonical JSON catalogue previously
left this release watcher pointing at deleted scripts.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
FOLLOWER = ROOT / ".github/workflows/kiln-latest.yml"
PACKAGE = ROOT / ".github/workflows/endless-chests.yml"


class CompatibilityWorkflowContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = FOLLOWER.read_text(encoding="utf-8")
        cls.normal = PACKAGE.read_text(encoding="utf-8")

    def test_referenced_python_scripts_exist(self):
        scripts = set(re.findall(r"python3\s+(scripts/[\w./-]+\.py)", self.source))
        self.assertTrue(scripts, "No Python script references found")
        for script in scripts:
            with self.subTest(script=script):
                self.assertTrue((ROOT / script).is_file(), f"Missing CI script {script}")

    def test_follower_uses_current_canonical_catalogue_contract(self):
        s = self.source
        self.assertIn("python3 scripts/validate-endless-settings.py", s)
        self.assertIn("python3 scripts/validate-endless-catalogue.py", s)
        self.assertIn("python3 scripts/test-endless-catalogue-repairs.py", s)
        self.assertIn("python3 scripts/test-endless-final-27.py", s)
        self.assertIn("python3 scripts/test-endless-weapon-groups.py", s)
        self.assertIn("python3 scripts/test-chest-name-hashes.py", s)
        self.assertIn("python3 scripts/test-chest-case.py", s)
        self.assertNotIn("validate-endless-ini.py", s)
        self.assertNotIn("test-endless-item-ids.py", s)
        for name in ("validate-endless-*.py", "test-endless-*.py", "test-chest-*.py"):
            self.assertIn(f"'scripts/{name}'", s)

    def test_four_mod_compatibility_matrix(self):
        s = self.source
        self.assertIn("mod: [SurvivalQoL, EndlessChests, NoClipToggle, BaseStationMagicStorage]", s)
        self.assertIn('"BaseStationMagicStorage": "base-station-magic-storage-*.zip"', s)
        self.assertIn("tests/base-station-magic-storage", s)
        self.assertIn("all four mod builds and contract tests passed", s)

    def test_sdk_watcher_is_not_an_hourly_every_source_rebuild(self):
        # The mod-specific CI workflows already build normal mod changes.
        # This watcher only needs new SDKs, infrastructure PRs and manual force.
        s = self.source
        triggers = s.split("\npermissions:", 1)[0]
        self.assertIn("cron: '17 5 * * *'", triggers)
        self.assertNotIn("cron: '17,47 * * * *'", triggers)
        for project in ("SurvivalQoL", "EndlessChests", "NoClipToggle",
                        "BaseStationMagicStorage"):
            self.assertNotIn(f"'\\{project}/**'", triggers)
        self.assertIn("key: kiln-sdk-verified-${{ steps.release.outputs.version }}", s)
        self.assertIn("key: kiln-sdk-verified-${{ needs.check.outputs.release_version }}", s)
        self.assertNotIn("key: kiln-release-verified-", s)
        self.assertIn("needs.check.outputs.build == 'true'", s)
        self.assertIn("github.event_name != 'pull_request'", s)
        self.assertIn("force", s)

    def test_packaging_and_release_include_canonical_json(self):
        s = self.source
        filename = "endless-chests.catalogue.json"
        self.assertIn(f"cp EndlessChests/defaults/catalogue.json bin/Release/{filename}", s)
        self.assertIn(f"bin/Release/{filename}", s)
        self.assertIn(f"endless_assets = (\"endless-chests.defaults.ini\", \"{filename}\")", s)
        self.assertIn("if len(published) != 10:", s)
        self.assertIn("python3 scripts/validate-endless-catalogue.py", self.normal)
        self.assertIn(f"bin/Release/{filename}", self.normal)


if __name__ == "__main__":
    unittest.main()
