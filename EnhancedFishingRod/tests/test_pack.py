"""Static contract and packaging tests (no live Enshrouded or Kiln runtime)."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ID = "enhanced-fishing-rod"


class FishingRodPackTests(unittest.TestCase):
    def setUp(self):
        self.manifest = json.loads((ROOT / "kiln.mod.json").read_text(encoding="utf-8"))
        self.pack = json.loads((ROOT / "content" / "rod.json").read_text(encoding="utf-8"))

    def test_manifest_is_data_only(self):
        self.assertEqual(self.manifest["id"], ID)
        self.assertEqual(self.manifest["version"], "0.1.0")
        self.assertEqual(self.manifest["apiVersion"], "0.2")
        self.assertEqual(self.manifest["side"], "both")
        self.assertEqual(self.manifest["clients"], "kiln")
        self.assertNotIn("entryAssembly", self.manifest)
        self.assertNotIn("entryType", self.manifest)

    def test_exact_vanilla_rod_and_fields_only(self):
        self.assertEqual(self.pack["format"], 1)
        self.assertEqual(set(self.pack) - {"comment", "format"}, {"items"})
        self.assertEqual(set(self.pack["items"]), {"Tool_T6_FishingRod_epic"})
        self.assertEqual(self.pack["items"]["Tool_T6_FishingRod_epic"], {
            "impactValues.FishingStrength": 5.0,
            "impactValues.FishingEndurance": 110.0,
        })

    def test_values_are_positive_and_finite(self):
        import math
        values = self.pack["items"]["Tool_T6_FishingRod_epic"]
        self.assertTrue(all(isinstance(v, (int, float)) and math.isfinite(v) and v > 0
                            for v in values.values()))

    def test_zip_matches_kiln_pack_layout(self):
        with tempfile.TemporaryDirectory() as output:
            subprocess.run(
                [sys.executable, str(ROOT / "package.py"), output],
                check=True, capture_output=True, text=True,
            )
            zips = list(Path(output).glob("*.zip"))
            self.assertEqual(len(zips), 1)
            self.assertEqual(zips[0].name, f'{ID}-{self.manifest["version"]}.zip')
            with zipfile.ZipFile(zips[0]) as archive:
                self.assertEqual(set(archive.namelist()), {
                    f"{ID}/kiln.mod.json",
                    f"{ID}/content/rod.json",
                    f"{ID}/README.md",
                })
                self.assertEqual(
                    json.loads(archive.read(f"{ID}/content/rod.json")), self.pack
                )


if __name__ == "__main__":
    unittest.main()
