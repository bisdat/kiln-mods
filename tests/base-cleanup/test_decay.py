"""Optional cosmetic decay v0.3.4 policy and journal compatibility tests.

Pure/offline tests model before/after raw values and require safety
invariants in source. They are not a substitute for in-game confirmation.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = (ROOT / "BaseCleanup" / "CleanupCore.cs").read_text()
CODE = (ROOT / "BaseCleanup" / "CleanupStage2.cs").read_text()
MOD = (ROOT / "BaseCleanup" / "BaseCleanupMod.cs").read_text()
PROJECT = (ROOT / "BaseCleanup" / "BaseCleanup.csproj").read_text()

INCLUDE_MUD = 1
INCLUDE_MYCELIUM = 2
INCLUDE_DECAY = 4
ALL_OPTIONS = INCLUDE_MUD | INCLUDE_MYCELIUM | INCLUDE_DECAY

def is_decay_target(raw, options):
    return bool(options & INCLUDE_DECAY) and (raw & 0xff) >= 128 and bool(raw & 0x8000)

def after(raw):
    # Terrain cleanup still removes to AIR, regardless of selected options;
    # function called only for a cell explicitly selected by the plan.
    return (raw & 0x7fff) if raw & 0xff >= 128 else 0

def undo(current, original):
    # Only restore original if no subsequent player/world edit changed
    # our exact post-edit raw state.
    return original if current == after(original) else current

class DecayTests(unittest.TestCase):
    def test_default_on_and_console_opt_out_stays_available(self):
        self.assertIn("IncludeDecay = 4", CORE)
        self.assertIn("AllowedOptions = IncludeMud | IncludeMycelium | IncludeDecay", CORE)
        self.assertIn("_includeDecay = true", CODE)
        self.assertIn('ui.Checkbox("Repair cosmetic decay (ON by default)", ref _includeDecay)', CODE)
        self.assertIn("(_includeDecay ? CleanupCore.IncludeDecay : 0)", CODE)
        self.assertIn("BeginPreparation(c.Source.Player, CleanupCore.IncludeMud | CleanupCore.IncludeMycelium)", CODE)
        self.assertIn('Command("baseclean preparedecay"', CODE)
        self.assertIn('Command("baseclean preparenodecay"', CODE)
        self.assertIn("BeginPreparation(c.Source.Player, CleanupCore.AllowedOptions)", CODE)

    def test_observed_decay_tool_voxels_repair_only_weathering(self):
        # Raw values from real Kiln read-only probe, building material 138.
        for original, cleaned in (
            (0x978a, 0x178a), (0xf78a, 0x778a),
            (0x808a, 0x008a), (0xff8a, 0x7f8a),
        ):
            self.assertTrue(is_decay_target(original, ALL_OPTIONS))
            self.assertFalse(is_decay_target(original, INCLUDE_MUD | INCLUDE_MYCELIUM))
            self.assertEqual(after(original), cleaned)
            self.assertEqual(original & 0xff, cleaned & 0xff)
            self.assertEqual(original & 0x7fff, cleaned)
            self.assertEqual(undo(cleaned, original), original)
        # Completely clean non-weathered buildings and any terrain excluded.
        for raw in (0x778a, 0x178a, 0x078a, 0x008a, 0xff17, 0x8023):
            self.assertFalse(is_decay_target(raw, ALL_OPTIONS))

    def test_growth_and_low_paint_unchanged_even_when_growth_is_level_7(self):
        for level in range(8):
            for low in range(16):
                for material in (128, 138, 255):
                    original = ((0x80 | (level << 4) | low) << 8) | material
                    new = after(original)
                    self.assertTrue(is_decay_target(original, ALL_OPTIONS))
                    self.assertEqual(new & 0xff, material)
                    self.assertEqual((new >> 8) & 0x70, level << 4)
                    self.assertEqual((new >> 8) & 0x0f, low)
                    self.assertEqual(new & 0x8000, 0)
                    self.assertEqual(undo(new, original), original)
        self.assertIn("WeatherMask = 0x8000", CORE)
        self.assertIn("voxel.Material >= 128", CORE)
        self.assertIn("new Voxel((ushort)(originalRaw & ~WeatherMask))", CORE)

    def test_undo_does_not_overwrite_later_changes(self):
        original = 0xf78a
        cleaned = after(original)
        self.assertEqual(cleaned, 0x778a)
        self.assertEqual(undo(cleaned, original), original)
        for later_edit in (0x778b, 0x678a, 0x008a, 0x0000, 0xF78A):
            self.assertEqual(undo(later_edit, original), later_edit)
        # Original Stage 2 terrain journal still rolls back only air.
        self.assertEqual(after(0xff17), 0)
        self.assertEqual(undo(0, 0xff17), 0xff17)
        self.assertEqual(undo(0x008a, 0xff17), 0x008a)

    def test_backup_and_journal_always_precede_any_write(self):
        self.assertLess(CODE.index("WorldData.Set(journalKey, journal);"),
                        CODE.index("await Saves.Backup(name)"))
        self.assertLess(CODE.index("backupCompleted = true"),
                        CODE.index("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)"))
        self.assertIn("CleanupCore.EditedValue(cell.Raw)", CODE)
        self.assertIn("expected[cell.Index].Raw != CleanupCore.EditedValue(cell.Raw).Raw", CODE)
        self.assertIn("desired[cell.Index] = new Voxel(cell.Raw)", CODE)
        self.assertIn("BuildFlags.InBases | BuildFlags.RespectZones", CODE)
        self.assertIn("result.Refused || !result.Verified || result.CellsChanged != toWrite", CODE)
        self.assertIn("if (result.Refusal == BuildRefusal.Changed)", CODE)
        self.assertIn("if (!backupCompleted)", CODE)
        self.assertIn("WorldData.Remove(journalKey)", CODE)
        self.assertIn('ConfirmRemove = "REMOVE"', CODE)
        self.assertIn('ConfirmUndo = "UNDO"', CODE)

    def test_older_v02_undo_journals_not_overwritten_and_still_readable(self):
        self.assertIn('LedgerPrefix = "cleanup-rollback-v1-"', CODE)
        self.assertIn('PreviousLedgerPrefix = "cleanup-rollback-v2-"', CODE)
        self.assertIn('CurrentLedgerPrefix = "cleanup-rollback-v3-"', CODE)
        self.assertIn("CurrentLedgerPrefix + LedgerKeyFor(altar)[LedgerPrefix.Length..]", CODE)
        self.assertIn("PreviousLedgerPrefix + LedgerKeyFor(altar)[LedgerPrefix.Length..]", CODE)
        self.assertIn("WorldData.TryGet<CleanupLedger>(PreviousLedgerKeyFor(altar)", CODE)
        self.assertIn("FullyUndone(CleanupLedger old)", CODE)
        self.assertIn('old.Phase == "undone" && old.Removed > 0', CODE)
        self.assertIn("old.Restored >= old.Removed && old.Conflicted == 0", CODE)
        self.assertIn("!SettledPreviousJournal(legacy)", CODE)
        self.assertIn("TryGetJournal(altar, out var journal)", CODE)
        self.assertIn("WorldData.TryGet<CleanupLedger>(LedgerKeyFor(altar), out var legacy)", CODE)
        self.assertIn("CleanupCore.Decode(journal.Packed)", CODE)
        self.assertIn("DecayPlanned", CORE)
        self.assertIn("Decode(string encoded)", CORE)

    def test_toggle_changes_after_prepare_must_force_new_plan(self):
        self.assertIn("selectedOptions != p.Options", CODE)
        self.assertIn("The cleanup options changed after planning.", CODE)
        self.assertIn("case 2: BeginCleanup(player, options)", CODE)
        self.assertIn("CleanupRequestLocal(2, flags)", CODE)
        self.assertIn("p.PreparedAtUtc.AddMinutes(PreviewMinutes) < DateTime.UtcNow", CODE)

    def test_full_altar_scope_but_no_overgrowth_edits(self):
        self.assertIn("BaseScanCore.ZoneCells(altar, grid)", CODE)
        self.assertIn("BaseScanCore.Batches(bounds)", CODE)
        self.assertIn("if (CleanupCore.DecayTarget(v, plan.Options))", CODE)
        self.assertIn("plan.DecayHits++", CODE)
        self.assertIn("Decay = p.DecayHits", CODE)
        self.assertIn("Decay {status.Decay:N0}", CODE)
        self.assertNotIn("OvergrowthMask", CORE)
        self.assertNotIn("GrowthTarget", CORE)
        self.assertIn("Ivy/overgrowth cleanup is excluded", CODE)

    def test_v03_package_and_network(self):
        self.assertIn("<Version>0.3.4</Version>", PROJECT)
        self.assertIn("<KilnModName>Base Cleanup</KilnModName>", PROJECT)
        self.assertNotIn("Base Cleanup - Confirmed Cleanup and Optional Decay", PROJECT)
        self.assertIn('Network.Channel(Channel, "3"', MOD)
        self.assertIn('KeyBindings.Register(Binding, "Shift+ScrollLock"', MOD)
        self.assertIn(".String(Backup).Int64(Decay)", CODE)
        self.assertIn("return status with { Decay = r.Int64() };", CODE)
        self.assertIn("p.Player != player", CODE)
        self.assertIn("Permissions.Has(player, CleanupPermission)", CODE)

if __name__ == "__main__":
    unittest.main()
