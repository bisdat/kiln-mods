"""Read-only Base Visual Probe contracts and modeled paint-bit experiments.

Tests are offline: GitHub CI compiles against Kiln MDK; only real
game tool comparisons can validate the bit/appearance correlation.
"""
import re
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
CORE=(ROOT/"BaseVisualProbe"/"VisualCore.cs").read_text()
MOD=(ROOT/"BaseVisualProbe"/"BaseVisualProbeMod.cs").read_text()
CSPROJ=(ROOT/"BaseVisualProbe"/"BaseVisualProbe.csproj").read_text()
FLOW=(ROOT/".github"/"workflows"/"base-visual-probe.yml").read_text()

def build(raw):
    material=raw&0xff
    paint=raw>>8
    return {
        "material":material,
        "building":material>=128,
        "paint":paint,
        "growth":(paint & 0x70)>>4,
        "weather": bool(paint & 0x80),
        "tint":paint & 0x0f
    }

def diff(old,new):
    a,b=build(old),build(new)
    if a["building"] and b["building"] and a["material"]==b["material"]:
        return {
            "growth_added":a["growth"]==0 and b["growth"]!=0,
            "growth_cleared":a["growth"]!=0 and b["growth"]==0,
            "growth_changed":a["growth"]!=b["growth"] and a["growth"]!=0 and b["growth"]!=0,
            "weather_added":not a["weather"] and b["weather"],
            "weather_cleared":a["weather"] and not b["weather"],
            "tint_changed":a["tint"]!=b["tint"],
            "material_changed":False
        }
    return {"material_changed":True}

class VisualProbeTests(unittest.TestCase):
    def test_zero_paint_is_still_building(self):
        # Original survey's Voxel.IsAir checks Fill==0 and would
        # accidentally skip a genuine unpainted building voxel.
        self.assertTrue(build(0x008f)["building"])
        self.assertEqual(build(0x008f)["growth"],0)
        self.assertFalse(build(0x008f)["weather"])
        self.assertIn("v.Material >= 128",CORE)
        self.assertIn("if (!VisualBits.IsBuilding(v)) continue;",CORE)
        self.assertNotIn("if (v.IsAir)",CORE)

    def test_weather_and_growth_bit_encoding(self):
        for material in [128,143,200,255]:
            for growth in range(8):
                for weather in [False,True]:
                    raw=(int(weather)*0x80 | growth<<4 | 0x05)<<8 | material
                    x=build(raw)
                    self.assertEqual(x["growth"],growth)
                    self.assertEqual(x["weather"],weather)
                    self.assertEqual(x["tint"],5)
        self.assertIn("Weathered = 0x80",CORE)
        self.assertIn("Overgrowth = 0x70",CORE)
        self.assertIn("OtherPaint = 0x0f",CORE)

    def test_game_tool_diff_categories_do_not_conflate_metadata_and_building_changes(self):
        # No real-world property changed; only the paint byte differs.
        old=(0xf5<<8)|143
        after_growth=(0x85<<8)|143
        after_decay=(0x05<<8)|143
        self.assertTrue(diff(old,after_growth)["growth_cleared"])
        self.assertFalse(diff(old,after_growth)["weather_cleared"])
        self.assertTrue(diff(after_growth,after_decay)["weather_cleared"])
        self.assertFalse(diff(after_growth,after_decay)["growth_cleared"])
        self.assertFalse(diff(after_growth,after_decay)["tint_changed"])
        self.assertTrue(diff(old,(0xf5<<8)|144)["material_changed"])
        self.assertTrue(diff(old,(0x55<<8)|91)["material_changed"])
        self.assertIn("previous.Material != current.Material", CORE)

    def test_census_histogram_for_paint_zero_and_flags(self):
        raw=[0x008f,0xff8f,0x708f,0x808f,0x0023,0xff23]
        values=[build(v) for v in raw]
        buildings=[v for v in values if v["building"]]
        self.assertEqual(len(buildings),4)
        self.assertEqual(sum(v["paint"]==0 for v in buildings),1)
        self.assertEqual(sum(v["growth"]>0 for v in buildings),2)
        self.assertEqual(sum(v["weather"] for v in buildings),2)
        self.assertEqual(sum(v["growth"]>0 and v["weather"] for v in buildings),1)
        self.assertIn("new long[128]",CORE)
        self.assertIn("new long[8]",CORE)

    def test_bounded_tile_reads_and_fixed_baseline(self):
        self.assertIn("const int nx = 32, ny = 16, nz = 16",CORE)
        self.assertIn("Build.BoxAround(here, 6, 6, 6)",MOD)
        self.assertIn("VisualBits.Intersect(area, Build.BoxAround",MOD)
        self.assertIn("sample.Count > 30_000",MOD)
        self.assertIn("snap.Cells.Add(cell, value)",MOD)
        self.assertIn("snap.Cells.TryGetValue(cell, out var previous)",MOD)
        self.assertIn("diffs.Compared != snap.Box.Count",MOD)
        self.assertIn("WorldLoadedEvent",MOD)
        self.assertIn("ServerStoppingEvent",MOD)
        self.assertIn("Saves.WorldId != snap.WorldId",MOD)

    def test_read_only_api_contract_and_network_permissions(self):
        forbidden=[
            "Write","WriteAsync","WriteIfUnchanged","Dig","Paint","Flatten",
            "Smooth","Replace","BreakBox","RemoveProp","PlaceProp","Undo"
        ]
        for f in forbidden:
            self.assertIsNone(re.search(r"\bBuild\."+f+r"\s*\(",CORE+MOD), f)
        for expr in ["Spawn.Destroy(","WorldData.Set(","WorldData.Remove(",
                     "Saves.Backup(","Saves.Request("]:
            self.assertNotIn(expr,CORE+MOD)
        self.assertIn("await Build.Load(box",MOD)
        self.assertIn("Build.Read(box)",MOD)
        self.assertIn("Permissions.Has(player, Permission)",MOD)
        self.assertIn("private void ObserveVanillaTool(BuildEvent e)",MOD)
        self.assertIn("e.Kind != BuildKind.Terraform",MOD)
        self.assertIn("e.TerraformType is < 4 or > 7",MOD)
        self.assertIn("e.Outcome == BuildOutcome.Done",MOD)
        self.assertIn("base-visual-probe TOOL-SUMMARY",MOD)
        self.assertIn("snap.Box.Contains(Build.CellOf(e.Position))",MOD)
        self.assertIn("Capabilities.Is(\"gameplay.build_hooks\")",MOD)
        self.assertIn("Gameplay</KilnServices>",CSPROJ)
        self.assertIn("Context.Scheduler.Post(() => Dispatch(p, operation, true))",MOD)
        self.assertIn('"Shift+F11"',MOD)
        self.assertIn("No network request accepts coordinates",MOD)

    def test_packaging_is_separate_without_release(self):
        self.assertIn("<KilnModId>base-visual-probe</KilnModId>",CSPROJ)
        self.assertIn("0.2.0-dev.30.6",CSPROJ)
        self.assertIn("base-visual-probe-0.1.1.zip",FLOW)
        self.assertIn("pull_request:",FLOW)
        self.assertNotIn("  push:",FLOW)
        self.assertNotIn("softprops/action-gh-release",FLOW)
        self.assertNotIn("contents: write",FLOW)
        self.assertIn("BaseCleanup/fetch-sdk.py",FLOW)

if __name__=="__main__":
    unittest.main()
