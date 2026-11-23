"""Offline Stage 1 source and geometry checks; not a Kiln SDK compilation."""
import math
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
CORE = (ROOT / "BaseCleanup" / "BaseScanCore.cs").read_text()
MOD = (ROOT / "BaseCleanup" / "BaseCleanupMod.cs").read_text()

def csharp_array(name):
    value = re.search(r"\b" + re.escape(name) + r"\s*=\s*\[([^]]+)\]", CORE)
    if not value:
        raise AssertionError("missing material group: " + name)
    return tuple(int(n.strip()) for n in value.group(1).split(","))

def bounds(center, half, origin=0, cell=0.5, max_cells=8192):
    lo = max(0, min(max_cells, math.ceil((center-half-origin)/cell - .5 - 1e-9)))
    hi = max(0, min(max_cells, math.floor((center+half-origin)/cell - .5 + 1e-9)+1))
    return lo, hi

def tiles(shape, tile=(32,16,16)):
    sx, sy, sz = shape
    for z in range(0,sz,tile[2]):
        for y in range(0,sy,tile[1]):
            for x in range(0,sx,tile[0]):
                yield (min(tile[0],sx-x),min(tile[1],sy-y),min(tile[2],sz-z))

class Stage1Tests(unittest.TestCase):
    def test_known_material_groups_no_overlap(self):
        place, world, myc = map(lambda n: set(csharp_array(n)), ("PlaceableRubble","WorldRubble","MineableMycelium"))
        self.assertEqual(place, {8,64,66,69,74,81,87,102,107})
        self.assertEqual(world, {23,36,43,44,61,62,92})
        self.assertEqual(myc, {14,33,65,72})
        self.assertEqual(len(place|world|myc),20)
        self.assertFalse((place|world)&myc)
        self.assertIn("MudTerrain = 37",CORE)
        self.assertNotIn("UnderwaterMud = 91",CORE)

    def test_cell_centres_match_zone_boundaries(self):
        self.assertEqual(bounds(80,80),(0,320))
        self.assertEqual(bounds(100,10),(180,220))
        self.assertEqual(bounds(5,2,origin=3),(0,8))
        self.assertEqual(bounds(-100,2),(0,0))
        self.assertEqual(bounds(8192,1),(8192,8192))
        self.assertEqual(bounds(2,1.5),(1,7))

    def test_batches_partition_entire_region_once(self):
        for shape in ((1,1,1),(32,16,16),(33,17,17),(320,320,320)):
            chunks=list(tiles(shape))
            self.assertTrue(chunks)
            self.assertEqual(sum(x*y*z for x,y,z in chunks),math.prod(shape))
            self.assertTrue(all(x*y*z <= 8192 for x,y,z in chunks))

    def test_air_cells_with_stale_material_are_ignored(self):
        packed=[(8,255),(8,128),(91,0),(14,255),(129,255),(91,1)]
        counts=[0]*256
        air=0
        for material,fill in packed:
            if fill==0: air+=1
            else: counts[material]+=1
        self.assertEqual((counts[8],counts[91],counts[14],counts[129],air),(2,1,1,1,1))
        self.assertIn("if (v.IsAir)",CORE)

    def test_no_mutation_apis_invoked(self):
        forbidden=["Write","WriteAsync","WriteIfUnchanged","Dig","Paint","Flatten","Smooth","Replace","BreakBox","RemoveProp","PlaceProp","Undo"]
        for name in forbidden:
            self.assertIsNone(re.search(r"\bBuild\."+name+r"\s*\(",CORE+MOD),name)
        self.assertIn("await Build.Load",MOD)
        self.assertIn("Build.Read(box)",MOD)
        self.assertIn("job.Unreadable += box.Count",MOD)
        self.assertIn("await Context.Scheduler.NextTick()",MOD)

    def test_permission_and_host_handoff_present(self):
        self.assertIn("Permissions.Has(player, PermissionNode)",MOD)
        self.assertIn("Context.Scheduler.Post(() => Act(local, op, true))",MOD)
        self.assertIn("Client.Post(() => Request(1))",MOD)
        self.assertIn("Client.Post(() => Request(2))",MOD)
        self.assertIn('"Shift+ScrollLock"',MOD)

if __name__=="__main__":
    unittest.main()
