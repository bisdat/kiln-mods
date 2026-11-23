"""Material-37 removal experiment: target precision, restore safety and old-ledger protection.

These are deterministic offline/source contract checks, NOT in-game proof
that excavating all material 37 is desirable.
"""
import re
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
SCAN=(ROOT/"BaseCleanup"/"BaseScanCore.cs").read_text()
CORE=(ROOT/"BaseCleanup"/"CleanupCore.cs").read_text()
STAGE=(ROOT/"BaseCleanup"/"CleanupStage2.cs").read_text()
MOD=(ROOT/"BaseCleanup"/"BaseCleanupMod.cs").read_text()
PROJ=(ROOT/"BaseCleanup"/"BaseCleanup.csproj").read_text()
FLOW=(ROOT/".github"/"workflows"/"base-cleanup-stage1.yml").read_text()

RUBBLE={8,64,66,69,74,81,87,102,107,23,36,43,44,61,62,92}
MYCELIUM={14,33,65,72}
MUD=37
INCLUDE_MUD=1
INCLUDE_MYCO=2
INCLUDE_DECAY=4
INCLUDE_RUBBLE=8

def target(raw,options):
    m=raw & 255
    fill=raw>>8
    if m>=128:
        return bool(options & INCLUDE_DECAY) and bool(raw & 0x8000)
    return bool(fill) and ((m in RUBBLE and bool(options & INCLUDE_RUBBLE)) or
                          m==MUD and bool(options & INCLUDE_MUD) or
                          m in MYCELIUM and bool(options & INCLUDE_MYCO))

class Mud37Tests(unittest.TestCase):
    def test_only_37_not_91_is_selected_by_mud_toggle(self):
        for raw in (0xFF25,0xAF25,0x0125):
            self.assertTrue(target(raw,INCLUDE_MUD))
            self.assertFalse(target(raw,0))
        for raw in (0xFF5B,0xFF15,0x2A01,0x0015,0x0025):
            self.assertFalse(target(raw,INCLUDE_MUD))
        self.assertEqual(MUD,37)
        self.assertIn("MudTerrain = 37",SCAN)
        self.assertIn("BaseScanCore.MudTerrain",CORE)
        self.assertNotIn("voxel.Material == 91",CORE)
        self.assertNotIn("UnderwaterMud",CORE+STAGE+MOD)
        self.assertIn("Include creek/ditch mud (material 37) — TEST",STAGE)

    def test_mud_37_is_not_safe_as_surface_only_or_geometry_preserving(self):
        # The observed puddle had multiple full 37-voxels below a
        # partial 37-voxel top. The real cleanup deliberately writes AIR,
        # producing a possible hole when applied to that material.
        for raw in (0xAF25,0xFF25):
            self.assertTrue(target(raw,INCLUDE_MUD))
            self.assertEqual(0, 0 if (raw&255)<128 else raw&0x7fff)
        self.assertIn("new Voxel((ushort)(originalRaw & ~WeatherMask))",CORE)
        self.assertIn(": Voxel.Air;",CORE)
        self.assertIn("desired[cell.Index] = CleanupCore.EditedValue(cell.Raw)",STAGE)
        self.assertIn("BuildFlags.InBases | BuildFlags.RespectZones",STAGE)

    def test_existing_terrain_mycelium_rubble_and_weather_target_unchanged(self):
        self.assertFalse(target(0xff17,0))
        self.assertTrue(target(0xff17,INCLUDE_RUBBLE))
        self.assertTrue(target(0xff0e,INCLUDE_MYCO))
        self.assertFalse(target(0xff0e,INCLUDE_MUD))
        self.assertTrue(target(0xf78a,INCLUDE_DECAY))
        self.assertFalse(target(0x778a,INCLUDE_DECAY))
        self.assertTrue(target(0xff25,INCLUDE_MUD|INCLUDE_MYCO|INCLUDE_DECAY))
        self.assertFalse(target(0xff5b,INCLUDE_MUD|INCLUDE_MYCO|INCLUDE_DECAY))
        self.assertIn("_includeRubble = true",STAGE)
        self.assertIn("_includeMud = true",STAGE)
        self.assertIn("_includeDecay = true",STAGE)
        self.assertIn('KeyBindings.Register(Binding, "Shift+ScrollLock"',MOD)
        self.assertIn("<KilnModName>Base Cleanup</KilnModName>",PROJ)

    def test_new_test_journal_never_overwrites_v02_or_v032(self):
        self.assertIn('LedgerPrefix = "cleanup-rollback-v1-"',STAGE)
        self.assertIn('PreviousLedgerPrefix = "cleanup-rollback-v2-"',STAGE)
        self.assertIn('CurrentLedgerPrefix = "cleanup-rollback-v3-"',STAGE)
        self.assertIn("PreviousLedgerKeyFor(PlayerBase altar)",STAGE)
        self.assertIn("WorldData.Set(journalKey, journal)",STAGE)
        self.assertIn("WorldData.Set(HistoryKeyFor(plan.Altar), history)",STAGE)
        self.assertNotIn("WorldData.Set(PreviousLedgerKeyFor",STAGE)
        self.assertNotIn("WorldData.Remove(PreviousLedgerKeyFor",STAGE)
        self.assertNotIn("WorldData.Set(LedgerKeyFor(",STAGE)
        self.assertIn("!FullyUndone(v3)",STAGE)
        self.assertIn("!FullyUndone(v1)",STAGE)
        self.assertIn("if (!FullyUndone(item)) { journal = item; return true; }",STAGE)
        self.assertIn("!FullyUndone(v1)",STAGE)
        self.assertIn('old.Phase == "undone"',STAGE)
        self.assertIn("old.Restored >= old.Removed;",STAGE)
        self.assertIn("TryGetJournal(altar, out journal)",STAGE)

    def test_completed_prior_cleanup_is_not_a_blocker_or_overwritten(self):
        # Real report from the user's Flame Altar 1026:
        # v0.3.2 journal: 88,057 edited, 0 restored, 0 conflicts,
        # phase complete. A v3 test journal is a distinct recovery record.
        def settled(phase, planned, removed, restored, conflicts):
            fully_undone = (phase=="undone" and removed>0 and
                           restored>=removed)
            fully_committed = (phase=="complete" and planned>0 and
                               removed==planned and restored==0 and
                               conflicts==0)
            return fully_undone or fully_committed
        self.assertTrue(settled("complete",88057,88057,0,0))
        self.assertTrue(settled("undone",88057,88057,88057,0))
        # v0.3.3 can record a full second-Undo conflict tally even though
        # the first Undo already restored every committed voxel.
        self.assertTrue(settled("undone",199135,199135,199135,199135))
        for previous in (
            ("partial",88057,88057,0,0),
            ("complete",88057,88056,0,0),
            ("complete",88057,88057,0,1),
            ("complete",88057,88057,1,0),
            ("undone",88057,88057,88056,0),
            ("undone",88057,88057,88056,2),
            ("complete",0,0,0,0),
        ):
            self.assertFalse(settled(*previous),previous)
        self.assertIn("SettledPreviousJournal(CleanupLedger previous)",STAGE)
        self.assertIn("previous.Phase == \"complete\"",STAGE)
        self.assertIn("previous.Planned > 0 && previous.Removed == previous.Planned",STAGE)
        self.assertIn("previous.Restored == 0 && previous.Conflicted == 0",STAGE)
        self.assertIn("!SettledPreviousJournal(item)",STAGE)
        self.assertIn("!SettledPreviousJournal(legacy)",STAGE)
        self.assertIn("WorldData.Set(journalKey, journal)",STAGE)
        self.assertNotIn("WorldData.Set(PreviousLedgerKeyFor",STAGE)

    def test_journal_before_write_no_world_backup_and_undo_only_on_air(self):
        self.assertLess(STAGE.index("WorldData.Set(journalKey, journal)"),
                        STAGE.index("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)"))
        self.assertLess(STAGE.index("WorldData.Set(HistoryKeyFor(plan.Altar), history)"),
                        STAGE.index("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)"))
        self.assertNotIn("await Saves.Backup(",STAGE)
        self.assertNotIn("Saves.Request(",STAGE)
        self.assertIn("CleanupCore.EditedValue(cell.Raw)",STAGE)
        self.assertIn("expected[cell.Index].Raw != CleanupCore.EditedValue(cell.Raw).Raw",STAGE)
        self.assertIn("desired[cell.Index] = new Voxel(cell.Raw)",STAGE)
        self.assertIn('ConfirmRemove = "REMOVE"',STAGE)
        self.assertIn('ConfirmUndo = "UNDO"',STAGE)

    def test_test_package_is_separate_version_not_release(self):
        self.assertIn("<Version>0.3.5</Version>",PROJ)
        self.assertIn("base-cleanup-0.3.5.zip",FLOW)
        self.assertIn("base-cleanup-selective-0.3.5-test",FLOW)
        self.assertIn("github.event_name == 'push'",FLOW)
        self.assertIn("github.ref == 'refs/heads/main'",FLOW)
        self.assertIn("  pull_request:",FLOW)

if __name__=="__main__":
    unittest.main()
