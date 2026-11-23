"""Read-only floor diagnostic: synthetic voxel cases plus source safety guards.

No attempt is made to identify a displayed puddle from its pixels:
the user labels two sites, and the mod compares the voxels below each.
"""
from pathlib import Path
import re
import unittest
from collections import Counter

ROOT = Path(__file__).resolve().parents[2]
FLOOR = (ROOT / "BaseVisualProbe" / "FloorMaterialCore.cs").read_text()
HOST = (ROOT / "BaseVisualProbe" / "FloorMaterialProbe.cs").read_text()
MOD = (ROOT / "BaseVisualProbe" / "BaseVisualProbeMod.cs").read_text()
PROJ = (ROOT / "BaseVisualProbe" / "BaseVisualProbe.csproj").read_text()
WORKFLOW = (ROOT / ".github" / "workflows" / "base-visual-probe.yml").read_text()

def occupied(raw):
    material = raw & 255
    paint_or_fill = raw >> 8
    return material >= 128 or (0 < material < 128 and paint_or_fill != 0)

def top_by_column(cells, center):
    cx, cy, cz = center
    counts = Counter()
    center_floor = None
    for z in range(cz - 3, cz + 4):
        for x in range(cx - 3, cx + 4):
            values = [(y, cells[(x, y, z)]) for y in range(cy - 11, cy + 1)
                      if occupied(cells[(x, y, z)])]
            if values:
                y, raw = max(values)
                counts[raw] += 1
                if x == cx and z == cz:
                    center_floor = (y - cy, raw)
            else:
                counts["no-floor"] += 1
    return counts, center_floor

def sample(center, base=0xff0a, special=None):
    cx, cy, cz = center
    cells = {(x, y, z): 0
             for x in range(cx - 3, cx + 4)
             for y in range(cy - 11, cy + 3)
             for z in range(cz - 3, cz + 4)}
    for x in range(cx - 3, cx + 4):
        for z in range(cz - 3, cz + 4):
            cells[(x, cy - 1, z)] = base
    for pos, raw in (special or {}).items():
        cells[pos] = raw
    return cells

class FloorMaterialTests(unittest.TestCase):
    def test_floor_material_91_and_other_terrain_are_distinguished(self):
        center=(100,200,300)
        dry=sample(center)
        wet=sample(center, special={(100,199,300):0xc85b}) # mat=91, fill=200
        d,dc=top_by_column(dry,center)
        w,wc=top_by_column(wet,center)
        self.assertEqual(dc,(-1,0xff0a))
        self.assertEqual(wc,(-1,0xc85b))
        self.assertEqual(w[0xc85b],1)
        self.assertEqual(w[0xff0a],48)
        self.assertEqual(d[0xff0a],49)

    def test_no_false_floor_from_empty_terrain_or_stale_material(self):
        center=(10,20,30)
        cells=sample(center,special={
            (10,19,30):0x000a,  # stale material 10, zero fill -> air
            (11,19,30):0x0000,  # air
        })
        counts, floor=top_by_column(cells,center)
        self.assertEqual(counts["no-floor"],2)
        self.assertIsNone(floor)
        self.assertFalse(occupied(0x000a))
        self.assertFalse(occupied(0xff00))
        self.assertFalse(occupied(0x0000))

    def test_zero_paint_building_is_occupied_not_air(self):
        center=(10,20,30)
        cells=sample(center,special={(10,19,30):0x008a})
        counts, floor=top_by_column(cells,center)
        self.assertTrue(occupied(0x008a))
        self.assertEqual(floor,(-1,0x008a))
        self.assertEqual(counts[0x008a],1)
        self.assertIn("v.Material >= 128",FLOOR)
        self.assertIn("v.Material is > 0 and < 128 && v.Fill != 0",FLOOR)

    def test_roof_over_head_not_mistaken_for_underfoot(self):
        center=(10,20,30)
        cells=sample(center,special={(10,22,30):0xff8a})
        counts,floor=top_by_column(cells,center)
        self.assertEqual(floor,(-1,0xff0a))
        self.assertIn("Math.Min(center.Y, box.End.Y - 1)",FLOOR)

    def test_bounded_no_world_changes_and_exact_raw_logging(self):
        self.assertIn("new CellBox(feet.X - 3, feet.Y - 11, feet.Z - 3, 7, 14, 7)",HOST)
        self.assertIn("box.Count > 1024",HOST)
        self.assertIn("VisualBits.Intersect(zone, nominal)",HOST)
        self.assertIn("await Build.Load(box, TimeSpan.FromSeconds(10))",HOST)
        self.assertIn("Build.Read(box)",HOST)
        self.assertIn("FloorMaterialProfile.Analyse",HOST)
        self.assertIn("SameSession(altar, world, epoch)",HOST)
        self.assertIn("TOP-MATERIAL",HOST)
        self.assertIn("TOP-RAW",HOST)
        self.assertIn("CENTER-Y",HOST)
        self.assertIn("CENTER-3x3",HOST)
        self.assertIn("FLOOR-PAIR",HOST)
        self.assertIn("noFloor=",HOST)
        self.assertIn("p.CenterFloorDy",HOST)
        self.assertIn('label == "PUDDLE"',HOST) if 'label == "PUDDLE"' in HOST else self.assertIn('mode == 8 ? "PUDDLE"',HOST)
        for s in (FLOOR,HOST):
            for forbidden in ("WriteIfUnchanged(","Build.Write(","Build.Dig(","Build.Paint(",
                              "WorldData.Set(","WorldData.Remove(","Saves.Backup(","Spawn.Destroy("):
                self.assertNotIn(forbidden,s)

    def test_host_samples_only_current_player_and_altar(self):
        self.assertIn("TryArea(player, out var altar",HOST)
        self.assertIn("Build.CellOf(here)",HOST)
        self.assertIn("No usable floor sample inside this altar.",HOST)
        self.assertIn("context.Player",MOD)
        self.assertIn("Permissions.Has(player, Permission)",MOD)
        self.assertIn("case 8:",MOD)
        self.assertIn("case 9:",MOD)
        self.assertIn("StartFloorSample(player, operation)",MOD)
        self.assertIn("ResetFloorProbe();",MOD)
        self.assertIn("FloorWetCommand",HOST)
        self.assertIn("FloorDryCommand",HOST)
        self.assertIn("DrawFloorProbe(ui);",MOD)
        self.assertIn("Network.Channel(Channel, \"3\"",MOD)
        self.assertIn("Register<FloorMaterialReport>",MOD)
        self.assertIn('"Shift+F11"',MOD)
        self.assertIn("0.1.1",PROJ)

    def test_sample_uses_no_destructive_correction_guess(self):
        self.assertIn("identical material does NOT prove a decal",HOST)
        self.assertIn("different material is not automatically safe to remove",HOST)
        self.assertIn("NOT a render/decal",FLOOR)
        self.assertNotIn("IncludeMud",HOST)
        self.assertNotIn("Voxel.Air",HOST)
        self.assertNotIn("  push:",WORKFLOW)
        self.assertNotIn("contents: write",WORKFLOW)
        self.assertIn("base-visual-probe-0.1.1.zip",WORKFLOW)

if __name__=="__main__":
    unittest.main()
