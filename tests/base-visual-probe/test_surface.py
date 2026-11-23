"""Read-only local appearance experiment and six-neighbour source checks.

Offline checks model interpretation, not what a real client renders.
The actual clean-versus-ivy comparison requires an in-game visual choice.
"""
from pathlib import Path
import re
import unittest
from collections import Counter

ROOT=Path(__file__).resolve().parents[2]
CORE=(ROOT/"BaseVisualProbe"/"VisualSurfaceCore.cs").read_text()
PROBE=(ROOT/"BaseVisualProbe"/"VisualSurfaceProbe.cs").read_text()
MOD=(ROOT/"BaseVisualProbe"/"BaseVisualProbeMod.cs").read_text()
SCANNER=(ROOT/"BaseVisualProbe"/"VisualCore.cs").read_text()

def building(raw):
    return raw&0xff >= 128

def air(raw):
    material=raw&0xff
    fill=raw>>8
    return material<128 and fill==0

def model(cells):
    offsets=[(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)]
    out=Counter()
    for (x,y,z),raw in cells.items():
        if not building(raw):
            continue
        out["buildings"]+=1
        growth=((raw>>8)&0x70)>>4
        out[f"all{growth}"]+=1
        neighbors=[(x+dx,y+dy,z+dz) for dx,dy,dz in offsets]
        if not all(q in cells for q in neighbors):
            out["unknown"]+=1
        elif any(air(cells[q]) for q in neighbors):
            out["exposed"]+=1
            out[f"exposed{growth}"]+=1
        else:
            out["enclosed"]+=1
    return out

class LocalSamples(unittest.TestCase):
    def test_air_adjacent_vs_interior_vs_unknown(self):
        cells={(x,y,z):0 for x in range(5) for y in range(5) for z in range(5)}
        # A full 3x3x3 block in the middle: only center is fully enclosed.
        for x in range(1,4):
            for y in range(1,4):
                for z in range(1,4):
                    cells[(x,y,z)]=(0x77<<8)|138
        data=model(cells)
        self.assertEqual(data["buildings"],27)
        self.assertEqual(data["enclosed"],1)
        self.assertEqual(data["exposed"],26)
        self.assertEqual(data["unknown"],0)
        self.assertEqual(data["exposed7"],26)
        self.assertEqual(data["all7"],27)

    def test_untouched_wall_and_actual_growth_tool_changes(self):
        # Both level 1 and level 7 are seen in the user's tool-tested wall.
        # Clearing 0x70 does not change material 138 or low paint nibble 7.
        for paint in (0x17,0x77,0x97,0xf7):
            original=paint<<8|138
            cleaned=(paint&~0x70)<<8|138
            self.assertEqual(original&0xff,cleaned&0xff)
            self.assertEqual((original>>8)&0x0f,(cleaned>>8)&0x0f)
            self.assertEqual(((cleaned>>8)&0x70),0)
        # This does NOT establish that every level 7 is a visible ivy cell.

    def test_around_zero_paint_building_is_not_air(self):
        self.assertTrue(building(0x008a))
        self.assertFalse(air(0x008a))
        self.assertTrue(air(0x0000))
        self.assertFalse(air(0x0080))  # building material 128, paint zero
        self.assertTrue(air(0x0017))   # terrain material 23, fill zero

    def test_edge_cells_are_unclassified(self):
        cells={(x,y,z):0 for x in range(3) for y in range(3) for z in range(3)}
        cells[(0,0,0)]=(0x77<<8)|138
        cells[(1,1,1)]=(0x17<<8)|138
        c=model(cells)
        self.assertEqual(c["buildings"],2)
        self.assertEqual(c["unknown"],1)
        self.assertEqual(c["exposed"],1)
        self.assertEqual(c["exposed1"],1)

    def test_bounded_local_only_read_and_honest_exposure_label(self):
        self.assertIn("Build.BoxAround(here, 2.5, 2.5, 2.5)",PROBE)
        self.assertIn("box.Count > 4096",PROBE)
        self.assertIn("await Build.Load(box, TimeSpan.FromSeconds(10))",PROBE)
        self.assertIn("VisualBits.Enumerate(box, read)",PROBE)
        self.assertIn("VisualSurfaceProfile.Analyse(",PROBE)
        self.assertIn("!VisualBits.IsBuilding(neighbour) && neighbour.Fill == 0",CORE)
        self.assertIn("hasAllNeighbours",CORE)
        self.assertIn("BorderUnknown",CORE)
        self.assertIn("Enclosed",CORE)
        self.assertIn("Exposed",CORE)
        self.assertIn("NOT a guarantee",PROBE)
        self.assertIn("Level7Exposed",PROBE) if "Level7Exposed" in PROBE else self.assertIn("Growth7Exposed",PROBE)
        self.assertIn('case 6:',MOD)
        self.assertIn('case 7:',MOD)
        self.assertIn('Network.Channel(Channel, "3"',MOD)
        self.assertIn(".Register<VisualSurfaceReport>",MOD)
        self.assertIn("DrawSurfaceSpot(ui);",MOD)
        self.assertIn("ResetSurfaceSpot();",MOD)

    def test_surface_paths_have_no_mutating_apis(self):
        all_sources=CORE+PROBE+MOD+SCANNER
        for name in ["Write","WriteAsync","WriteIfUnchanged","Dig","Paint","Flatten",
                     "Smooth","Replace","BreakBox","RemoveProp","PlaceProp","Undo"]:
            self.assertIsNone(re.search(r"\bBuild\."+name+r"\s*\(", all_sources),name)
        for term in ["WorldData.Set(","WorldData.Remove(","Saves.Backup(","Spawn.Destroy("]:
            self.assertNotIn(term,all_sources)
        self.assertIn("Mode == 6",PROBE)
        self.assertIn("Mode == 7",PROBE)

if __name__=="__main__":
    unittest.main()
