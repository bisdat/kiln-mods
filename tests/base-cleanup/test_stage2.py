"""Stage 2 policy and journal regression checks. These are static/offline,
not a substitute for an in-game backup, removal and restart test."""
import gzip
import io
import re
import struct
import unittest
from pathlib import Path

BASE = Path(__file__).resolve().parents[2] / "BaseCleanup"
CORE = (BASE / "CleanupCore.cs").read_text()
CODE = (BASE / "CleanupStage2.cs").read_text()
SCAN = (BASE / "BaseCleanupMod.cs").read_text()
SCANNER = (BASE / "BaseScanCore.cs").read_text()

MAGIC = 0x32434C42

def packed(chunks):
    stream=io.BytesIO()
    for chunk in chunks:
        x,y,z,sx,sy,sz,cells=chunk
        stream.write(struct.pack("<7i",x,y,z,sx,sy,sz,len(cells)))
        for index,raw in cells:
            stream.write(struct.pack("<iH",index,raw))
    return gzip.compress(struct.pack("<ii",MAGIC,len(chunks))+stream.getvalue())

def unpack(data):
    s=io.BytesIO(gzip.decompress(data))
    magic, n=struct.unpack("<ii",s.read(8))
    if magic!=MAGIC or n<0 or n>250000:
        raise ValueError("invalid header")
    result=[]
    for _ in range(n):
        x,y,z,sx,sy,sz,count=struct.unpack("<7i",s.read(28))
        if sx<=0 or sy<=0 or sz<=0 or sx*sy*sz>8192 or count<0 or count>sx*sy*sz:
            raise ValueError("invalid chunk")
        cells=[]
        used=set()
        for _ in range(count):
            index,raw=struct.unpack("<iH",s.read(6))
            if index in used or index<0 or index>=sx*sy*sz or raw>>8==0:
                raise ValueError("invalid voxel record")
            used.add(index)
            cells.append((index,raw))
        result.append((x,y,z,sx,sy,sz,cells))
    if s.read(): raise ValueError("trailing bytes")
    return result

class SafetyTests(unittest.TestCase):
    def test_compressed_sparse_journal_round_trip(self):
        chunks=[
            (10,20,30,32,16,16,[(0,0xff17),(3,0x8017),(8191,0x2a43)]),
            (42,20,30,3,4,5,[(59,0xff42)]),
        ]
        self.assertEqual(unpack(packed(chunks)),chunks)
        self.assertLess(len(packed(chunks)),300)
        self.assertIn("BinaryWriter(gzip)",CORE)
        self.assertIn("BinaryReader(gzip)",CORE)

    def test_journal_rejects_corrupt_and_duplicate_cells(self):
        with self.assertRaises(ValueError):
            unpack(packed([(0,0,0,1,1,1,[(0,0xff17),(0,0xff17)])]))
        with self.assertRaises(ValueError):
            unpack(packed([(0,0,0,1,1,1,[(1,0xff17)])]))
        with self.assertRaises(ValueError):
            unpack(packed([(0,0,0,1,1,1,[(0,0x0017)])]))
        with self.assertRaises(ValueError):
            unpack(gzip.compress(struct.pack("<ii",0,0)))

    def test_removal_preserves_non_targets_and_fill(self):
        rubble={8,64,66,69,74,81,87,102,107,23,36,43,44,61,62,92}
        mycelium={14,33,65,72}
        for material, fill, include_mud, include_mycelium in (
            (23,255,False,False),(43,110,False,False),(66,255,False,False),
            (37,220,True,False),(14,120,False,True)):
            target=(material in rubble or (material==37 and include_mud) or
                    (material in mycelium and include_mycelium)) and fill!=0
            self.assertTrue(target)
        self.assertNotIn(138,rubble)
        self.assertIn("MudTerrain = 37", SCANNER)
        self.assertIn("voxel.Material == BaseScanCore.MudTerrain",CORE)
        self.assertIn("Include creek/ditch mud (material 37) — TEST",CODE)
        self.assertNotIn("UnderwaterMud",SCANNER+CODE+SCAN)
        self.assertNotIn("material 91",CODE)
        self.assertIn("!voxel.IsAir", CORE)
        self.assertIn("BaseScanCore.IsRubble", CORE)
        self.assertIn("IncludeRubble", CORE)
        self.assertIn("BaseScanCore.IsRubble(voxel.Material) && (options & IncludeRubble) != 0", CORE)
        self.assertIn("IncludeMud", CORE)
        self.assertIn("IncludeMycelium", CORE)
        self.assertNotIn("surface",CORE.lower())
        self.assertNotIn("Build.BlockAt(",CODE)
        self.assertNotIn("Build.FindWall(",CODE)

    def test_world_data_journal_and_world_backup_precede_any_write(self):
        journal=CODE.index("WorldData.Set(journalKey, journal);")
        history=CODE.index("WorldData.Set(HistoryKeyFor(plan.Altar), history);")
        edit=CODE.index("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)")
        self.assertLess(journal,edit)
        self.assertLess(history,edit)
        self.assertIn("WorldData.Contains(journalKey)",CODE)
        self.assertIn("journal.Phase = journal.Removed > 0",CODE)
        self.assertIn("await Saves.Backup(CleanupBackups.Label)", CODE)
        self.assertLess(CODE.index("await Saves.Backup(CleanupBackups.Label)"),edit)
        self.assertLess(CODE.index("CleanupBackups.Prune(backup)"),edit)
        for forbidden in ("Saves.Request(", "Saves.RestoreOnNextStart(", "Saves.Restore("):
            self.assertNotIn(forbidden,CODE)
        self.assertNotIn("backupCompleted",CODE)

    def test_rollback_journal_key_is_individual_to_altar(self):
        self.assertIn("LedgerKeyFor(PlayerBase altar)", CODE)
        self.assertIn("SHA256.HashData", CODE)
        self.assertIn("HasOutstandingLedger(p.Altar, out reason)", CODE)
        self.assertIn("WorldData.TryGet<CleanupLedger>(LedgerKeyFor(altar)", CODE)
        self.assertIn("WorldData.Set(journalKey, journal)", CODE)

    def test_two_step_explicit_confirmation_and_server_authority(self):
        self.assertIn('ConfirmRemove = "REMOVE"',CODE)
        self.assertIn('ConfirmUndo = "UNDO"',CODE)
        self.assertIn("p.Player != player",CODE)
        self.assertIn("p.PreparedAtUtc.AddMinutes(PreviewMinutes) < DateTime.UtcNow",CODE)
        self.assertIn("HasOutstandingLedger(p.Altar, out reason)",CODE)
        self.assertIn("Permissions.Has(player, CleanupPermission)",CODE)
        self.assertIn('Context.Scheduler.Post(() => CleanupAct(player, operation, options, localHost: true))',CODE)
        self.assertIn("BuildFlags.InBases | BuildFlags.RespectZones",CODE)
        self.assertIn("Journal = false",CODE)
        self.assertIn("DataMatch.Exact",CODE)
        self.assertIn("Saves.WorldId != journal.WorldId",CODE)
        self.assertIn("CleanupCore.Decode(journal.Packed)",CODE)
        self.assertIn("Volatile.Write(ref _viewCleanup, status)",CODE)
        self.assertIn('Network.Channel(Channel, "3"',SCAN)

    def test_rollback_never_writes_into_non_air(self):
        self.assertIn("expected[cell.Index].Raw != CleanupCore.EditedValue(cell.Raw).Raw",CODE)
        self.assertIn("desired[cell.Index] = new Voxel(cell.Raw)",CODE)
        self.assertIn("Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask)",CODE)
        original={0:0xff17,1:0x2a43,2:0xff42}
        after={0:0,1:0xff8a,2:0}
        restored=after.copy()
        for i,raw in original.items():
            if after[i]==0:
                restored[i]=raw
        self.assertEqual(restored,{0:0xff17,1:0xff8a,2:0xff42})

    def test_stage1_scan_still_read_only(self):
        self.assertIn("Build.Read(box)",SCAN)
        self.assertIn("DrawCleanup(ui)",SCAN)
        self.assertNotIn("Build.WriteIfUnchanged(",SCAN)
        self.assertNotIn("Build.WriteIfUnchanged(",SCANNER)

if __name__=="__main__":
    unittest.main()
