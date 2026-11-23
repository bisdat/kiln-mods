"""Staged UI and per-operation Undo journal safety regression tests.

Offline models cover the UI state sequence and journal generation/rollback
invariants. Only an in-game test can verify the actual UI and backup restore.
"""
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
MOD=(ROOT/"BaseCleanup"/"BaseCleanupMod.cs").read_text()
CODE=(ROOT/"BaseCleanup"/"CleanupStage2.cs").read_text()
CORE=(ROOT/"BaseCleanup"/"CleanupCore.cs").read_text()
FLOW=(ROOT/".github"/"workflows"/"base-cleanup-stage1.yml").read_text()

class OperationHistory:
    """Model the persisted operation index, newest-first Undo and old ledgers."""
    def __init__(self):
        self.next_seq=1
        self.history={}
        self.legacy={"v2":dict(phase="complete",planned=88057,removed=88057,restored=0,conflicted=0),
                     "v3":dict(phase="undone",planned=199135,removed=199135,restored=199135,conflicted=0)}
        self.scanned=False
        self.prepared=False

    def scan(self):
        self.scanned=True
        self.prepared=False

    def prepare(self):
        if not self.scanned: raise ValueError("scan required")
        self.prepared=True

    def remove(self,edited):
        if not self.prepared: raise ValueError("prepare required")
        seq=self.next_seq;self.next_seq+=1
        self.history[seq]=dict(phase="complete",planned=edited,
            removed=edited,restored=0,conflicted=0)
        self.scanned=False;self.prepared=False
        return seq

    def undo(self):
        for seq in reversed(list(self.history)):
            x=self.history[seq]
            if x["phase"]!="undone":
                x["phase"]="undone";x["restored"]=x["removed"]
                return seq
        return None

class MultiStepSafety(unittest.TestCase):
    def test_model_scan_options_prepare_remove_undo_then_repeat(self):
        h=OperationHistory()
        with self.assertRaises(ValueError):h.prepare()
        with self.assertRaises(ValueError):h.remove(10)
        h.scan()
        h.prepare()
        self.assertEqual(h.remove(199135),1)
        with self.assertRaises(ValueError):h.remove(199135)
        self.assertEqual(h.undo(),1)
        self.assertEqual(h.undo(),None)
        h.scan();h.prepare()
        self.assertEqual(h.remove(111073),2)
        self.assertEqual(h.undo(),2)
        self.assertEqual(h.history[1]["restored"],199135)
        self.assertEqual(h.history[2]["restored"],111073)
        self.assertEqual(h.legacy["v2"]["removed"],88057)
        self.assertEqual(h.legacy["v3"]["restored"],199135)

    def test_model_incremental_ops_are_undoable_latest_first(self):
        h=OperationHistory()
        for qty in (100,200,300):
            h.scan();h.prepare();h.remove(qty)
        self.assertEqual([h.undo(),h.undo(),h.undo(),h.undo()],
                         [3,2,1,None])
        self.assertEqual(set(h.history.keys()),{1,2,3})
        self.assertTrue(all(x["removed"]==x["restored"] for x in h.history.values()))

    def test_scan_is_server_side_prerequisite_for_new_prepare(self):
        self.assertIn("_lastSurveyWorldId = Saves.WorldId;",MOD)
        self.assertIn("_lastSurveyAltar = job.Zone;",MOD)
        self.assertIn("job.Unreadable == 0",MOD)
        self.assertIn("_lastSurveyWorldId != Saves.WorldId",CODE)
        self.assertIn("_lastSurveyAltar is not { } surveyed",CODE)
        self.assertIn("surveyed.Altar != altar.Altar",CODE)
        self.assertIn("Step 1 required: complete a fresh, readable survey",CODE)
        self.assertIn("_lastSurveyWorldId = null;",CODE)
        self.assertIn("_lastSurveyAltar = null;",CODE)
        self.assertIn("_latest = null;",CODE)
        self.assertIn("No unexpired, nonempty plan",CODE)
        self.assertIn("p.PreparedAtUtc.AddMinutes(PreviewMinutes)",CODE)

    def test_ui_shows_only_next_stage_and_no_unprepared_remove(self):
        self.assertIn("private int _uiStage;",MOD)
        self.assertIn("if (stage == 0)",MOD)
        self.assertIn("if (stage == 1)",MOD)
        self.assertIn("DrawCleanup(ui);",MOD)
        for step in ("if (step == 1)","if (step == 2)","if (step == 3)",
                     "if (step == 4)","if (step == 5)"):
            self.assertIn(step,CODE)
        self.assertIn('ui.Button("1. Scan current Flame Altar")',MOD)
        self.assertIn('ui.Button("2. Prepare selected options (read-only)")',CODE)
        self.assertIn('ui.Button("4. REMOVE this prepared operation")',CODE)
        self.assertIn('ui.Button("5. UNDO latest eligible operation")',CODE)
        self.assertIn('if (status?.Phase != "prepared")',CODE)
        self.assertIn("_cleanupConfirm == ConfirmRemove",CODE)
        self.assertIn("_undoConfirm == ConfirmUndo",CODE)
        self.assertIn('case "undone":',CODE)
        self.assertIn("Volatile.Write(ref _uiStage, 0)",CODE)
        self.assertIn("Volatile.Write(ref _viewReport, null)",CODE)

    def test_each_commit_gets_unique_record_and_backup_before_edit(self):
        self.assertIn("public sealed class CleanupHistory",CORE)
        self.assertIn("List<int> Sequences",CORE)
        self.assertIn("OperationNumber",CORE)
        self.assertIn('HistoryPrefix = "cleanup-rollback-v4-index-"',CODE)
        self.assertIn('OperationPrefix = "cleanup-rollback-v4-"',CODE)
        self.assertIn("ReadHistory(PlayerBase altar)",CODE)
        self.assertIn("OperationKeyFor(PlayerBase altar, int sequence)",CODE)
        self.assertIn('WorldData.Contains(journalKey)',CODE)
        self.assertIn("history.NextSequence++;",CODE)
        self.assertIn("history.Sequences.Add(sequence);",CODE)
        self.assertIn("WorldData.Set(journalKey, journal);",CODE)
        self.assertIn("WorldData.Set(HistoryKeyFor(plan.Altar), history);",CODE)
        self.assertLess(CODE.index("WorldData.Set(journalKey, journal);"),
                        CODE.index("await Saves.Backup(name)"))
        self.assertLess(CODE.index("WorldData.Set(HistoryKeyFor(plan.Altar), history);"),
                        CODE.index("await Saves.Backup(name)"))
        self.assertLess(CODE.index("backupCompleted = true;"),
                        CODE.index("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)"))
        self.assertIn("WorldData.Remove(journalKey)",CODE)
        self.assertIn("history.Sequences.Remove(journal.OperationNumber);",CODE)
        self.assertIn("MaxHistory = 32",CODE)
        self.assertIn("Refusing to proceed",CODE)

    def test_legacy_journals_remain_unmodified_and_reverse_order_undo(self):
        for prefix in ('cleanup-rollback-v1-','cleanup-rollback-v2-','cleanup-rollback-v3-'):
            self.assertIn(prefix,CODE)
        self.assertIn("for (var i = history.Sequences.Count - 1; i >= 0; i--)",CODE)
        self.assertIn("foreach (var item in ReadOperationJournals(altar))",CODE)
        self.assertIn("if (!FullyUndone(item)) { journal = item; return true; }",CODE)
        self.assertIn("!SettledPreviousJournal(item)",CODE)
        self.assertIn("if (!SettledPreviousJournal(legacy))",CODE) if "if (!SettledPreviousJournal(legacy))" in CODE else self.assertIn("!SettledPreviousJournal(legacy)",CODE)
        self.assertNotIn("WorldData.Set(CurrentLedgerKeyFor(plan.Altar)",CODE)
        self.assertNotIn("WorldData.Set(PreviousLedgerKeyFor(plan.Altar)",CODE)
        self.assertNotIn("WorldData.Set(LedgerKeyFor(plan.Altar)",CODE)

    def test_cancelled_undo_can_resume_without_false_conflicts(self):
        self.assertIn("expected[cell.Index].Raw == cell.Raw",CODE)
        self.assertIn("journal.Restored += restored;",CODE)
        self.assertIn("journal.Restored >= journal.Removed && conflicts == 0",CODE)
        self.assertIn("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)",CODE)
        self.assertIn('journal.Phase = "partial"',CODE)

    def test_no_release_on_pr(self):
        self.assertIn("base-cleanup-staged-0.3.4-test",FLOW)
        self.assertIn("github.event_name == 'push'",FLOW)
        self.assertIn("github.ref == 'refs/heads/main'",FLOW)
        self.assertIn("  pull_request:",FLOW)

if __name__=="__main__":
    unittest.main()
