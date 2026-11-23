"""Independent cleanup categories and safe chat journal purge contract.

Read-only/offline source tests; do not attempt live WorldData mutation here.
"""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]
CORE=(ROOT/"BaseCleanup"/"CleanupCore.cs").read_text()
STAGE=(ROOT/"BaseCleanup"/"CleanupStage2.cs").read_text()
MOD=(ROOT/"BaseCleanup"/"BaseCleanupMod.cs").read_text()
RUBBLE={8,64,66,69,74,81,87,102,107,23,36,43,44,61,62,92}
MYCELIUM={14,33,65,72}
MUD=37
R=8; M=1; Y=2; D=4

def target(raw, flags):
    mat=raw&255
    fill=raw>>8
    if mat>=128: return bool(flags&D) and bool(raw&0x8000)
    return bool(fill) and ((mat in RUBBLE and bool(flags&R)) or
        (mat==MUD and bool(flags&M)) or (mat in MYCELIUM and bool(flags&Y)))

class SelectiveTests(unittest.TestCase):
    def test_four_category_flags_are_independent(self):
        rows=((R,0xff17),(M,0xff25),(Y,0xff0e),(D,0xf78a))
        for flags,raw in rows:
            self.assertTrue(target(raw,flags),(flags,raw))
            self.assertFalse(target(raw,0),(flags,raw))
            for other_flag,other_raw in rows:
                if flags!=other_flag:self.assertFalse(target(other_raw,flags))
        for flags,raw in rows:
            self.assertTrue(target(raw,R|M|Y|D))
        self.assertIn("IncludeRubble = 8",CORE)
        self.assertIn("AllowedOptions = IncludeMud | IncludeMycelium | IncludeDecay | IncludeRubble",CORE)
        self.assertIn("BaseScanCore.IsRubble(voxel.Material) && (options & IncludeRubble) != 0",CORE)
        self.assertIn("voxel.Material == BaseScanCore.MudTerrain",CORE)
        self.assertIn("Include rubble (entire 3-D altar)",STAGE)
        self.assertIn("_includeRubble = true",STAGE)
        self.assertIn("(_includeRubble ? CleanupCore.IncludeRubble : 0)",STAGE)
        self.assertIn("options == 0 || (options & ~CleanupCore.AllowedOptions) != 0",STAGE)
        self.assertIn("selectedOptions != p.Options",STAGE)

    def test_journals_persist_with_verified_precommit_save(self):
        self.assertIn("WorldData.Set(journalKey, journal)",STAGE)
        self.assertIn("WorldData.Set(HistoryKeyFor(plan.Altar), history)",STAGE)
        self.assertIn("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)",STAGE)
        self.assertLess(STAGE.index("WorldData.Set(journalKey, journal)"),
                        STAGE.index("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)"))
        self.assertLess(STAGE.index("WorldData.Set(HistoryKeyFor(plan.Altar), history)"),
                        STAGE.index("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)"))
        self.assertIn("await Saves.Backup(CleanupBackups.Label)", STAGE)
        self.assertLess(STAGE.index("CleanupBackups.Prune(backup)"),
                        STAGE.index("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)"))
        for forbidden in ("Saves.Request(", "Saves.RestoreOnNextStart(",
                          "Saves.Restore(", "File.Delete(", "Directory.Delete(", "File.WriteAll"):
            self.assertNotIn(forbidden,STAGE)
        self.assertIn("WorldData.Loaded != DataMatch.Exact",STAGE)
        self.assertIn("Saves.WorldId != journal.WorldId",STAGE)

    def test_chat_purge_is_scoped_and_two_step_only(self):
        self.assertIn('Command("baseclean journals"',STAGE)
        self.assertIn('Command("baseclean journals delete"',STAGE)
        self.assertIn('ConfirmDeleteJournals = "DELETE_ALL_JOURNALS"',STAGE)
        self.assertIn('Permission = CommandPermission.Admin',STAGE)
        self.assertIn('k.StartsWith("cleanup-rollback-", StringComparison.Ordinal)',STAGE)
        self.assertIn("WorldData.Keys",STAGE)
        self.assertIn("_journalPurgeWorldId != Saves.WorldId",STAGE)
        self.assertIn("_journalPurgePlayer != c.Source.Player",STAGE)
        self.assertIn("_journalPurgeAtUtc.AddMinutes(PurgeMinutes) < DateTime.UtcNow",STAGE)
        self.assertIn("!current.SequenceEqual(snapshot, StringComparer.Ordinal)",STAGE)
        self.assertIn("if (_cleanupBusy || _running is not null)",STAGE)
        self.assertIn("WorldData.Loaded != DataMatch.Exact",STAGE)
        self.assertIn("if (WorldData.Remove(key)) removed++",STAGE)
        self.assertIn("_journalPurgeKeys = null;",STAGE)
        self.assertIn("No voxel edits or world save requested",STAGE)
        self.assertNotIn("OnUnload() => WorldData",MOD)
        self.assertNotIn("WorldData.Remove(",MOD)

    def test_old_journal_names_included_without_touching_other_mod_keys(self):
        allkeys=("cleanup-rollback-v1-aaa","cleanup-rollback-v2-aaa",
                 "cleanup-rollback-v3-aaa","cleanup-rollback-v4-index-aaa",
                 "cleanup-rollback-v4-aaa-000001","my-other-mod-data",
                 "config-settings")
        journalkeys=[k for k in allkeys if k.startswith("cleanup-rollback-")]
        self.assertEqual(len(journalkeys),5)
        self.assertNotIn("config-settings",journalkeys)
        self.assertNotIn("my-other-mod-data",journalkeys)

if __name__=="__main__":
    unittest.main()
