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

    def test_packaging_and_release_include_canonical_json(self):
        s = self.source
        filename = "endless-chests.catalogue.json"
        self.assertIn(f"cp EndlessChests/defaults/catalogue.json bin/Release/{filename}", s)
        self.assertIn(f"bin/Release/{filename}", s)
        self.assertIn(f"endless_assets = (\"endless-chests.defaults.ini\", \"{filename}\")", s)
        self.assertIn("if len(published) != 8:", s)
        self.assertIn("python3 scripts/validate-endless-catalogue.py", self.normal)
        self.assertIn(f"bin/Release/{filename}", self.normal)


if __name__ == "__main__":
    unittest.main()
