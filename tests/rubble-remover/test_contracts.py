"""Source-level safety and packaging contracts for Rubble Remover.

These tests are not a substitute for game/SDK-native integration acceptance.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / "RubbleRemover" / "RubbleRemoverMod.cs").read_text()
PROJECT = (ROOT / "RubbleRemover" / "RubbleRemover.csproj").read_text()
WORKFLOW = (ROOT / ".github" / "workflows" / "rubble-remover.yml").read_text()

class RubbleRemoverContracts(unittest.TestCase):
    def test_removal_allowlist_is_terrain_only(self):
        match = re.search(r"RemovableTerrain\s*=\s*\[(.*?)\];", SOURCE, re.S)
        self.assertIsNotNone(match)
        cells = [int(x) for x in re.findall(r"\d+", match.group(1))]
        self.assertEqual(len(cells), len(set(cells)))
        self.assertTrue(all(1 <= cell <= 127 for cell in cells))
        self.assertEqual(len(cells), 20)
        self.assertTrue({8, 64, 66, 69, 74, 81, 87, 102, 107}.issubset(cells))
        self.assertTrue({23, 36, 43, 44, 61, 62, 92}.issubset(cells))
        self.assertTrue({14, 33, 65, 72}.issubset(cells))
        self.assertTrue({0, 32, 48, 91, 128, 149}.isdisjoint(cells))

    def test_both_actions_are_qualified_remove_only(self):
        self.assertEqual(SOURCE.count("Mode = TerrainToolMode.Remove"), 2)
        self.assertEqual(SOURCE.count("RemoveMaterials = RemovableTerrain"), 2)
        self.assertEqual(SOURCE.count("TerrainOnly = true"), 2)
        self.assertNotIn("TerrainToolMode.Add", SOURCE)
        self.assertNotIn("Build.Dig(", SOURCE.split("public override void OnLoad()")[1])
        self.assertNotIn("ToolMaterialPoolBlock", SOURCE)

    def test_carpenter_recipe_and_brushes(self):
        for name in ("small", "medium", "large"):
            self.assertIn(f'Blocks.Define("{name}"', SOURCE)
        for radius in ("0.5", "1.5", "2.5"):
            self.assertIn(f"Radius = {radius}", SOURCE)
        self.assertIn("Station = StationKind.Carpenter.Workshop", SOURCE)
        self.assertIn("MenuGroup = CraftingGroups.Carpenter.Essentials", SOURCE)
        for item, count in (("Build_TerrainMaterial_T1_Rubble",20),
                            ("Material_T1_FogTerrain_Mycelium",20),
                            ("Material_T1_Wood_Logs",1)):
            self.assertIn(f'["{item}"] = {count}', SOURCE)
        self.assertIn("Output = tool", SOURCE)
        self.assertIn("TrackCrafted = true", SOURCE)

    def test_client_parity_and_pinned_sdk(self):
        self.assertIn('Kiln.Sdk/0.2.0-dev.30.6', PROJECT)
        self.assertIn("<KilnSide>both</KilnSide>", PROJECT)
        self.assertIn("<KilnClients>mod</KilnClients>", PROJECT)
        self.assertIn("<KilnServices>Blocks;Tools</KilnServices>", PROJECT)

    def test_ci_does_not_publish_unaccepted_release(self):
        self.assertIn("RubbleRemover/fetch-sdk.py", WORKFLOW)
        self.assertIn("KilnPack", WORKFLOW)
        self.assertIn("actions/upload-artifact@v4", WORKFLOW)
        self.assertNotIn("action-gh-release", WORKFLOW)
        self.assertNotIn("refs/tags/", WORKFLOW)

if __name__ == "__main__":
    unittest.main()
