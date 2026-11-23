# Base Cleanup v0.3.4 — staged workflow and independent Undo histories

**Experimental test build — do not merge or tag before in-game acceptance.**
Released v0.3.2 remains unchanged. This candidate lives on draft PR #74,
has the same mod ID `base-cleanup`, and displays **Base Cleanup** in Kiln.

## Accepted v0.3.3 material-37 finding (23 November 2026)

The user's Flame Altar 1026 test on Kiln dev.30.6 succeeded:
- The complete survey read 32,768,000 voxels with zero unreadable.
- Preparation selected **199,135** voxels, including **48,183**
  cosmetic-decay repairs and material-37 creek/ditch mud.
- The complete world backup was verified before any change.
- REMOVE edited all 199,135 cells, zero conflicts.
- UNDO restored all 199,135 cells, zero conflicts.

The user then tried REMOVE immediately after UNDO; the consumed
prepared plan correctly refused that direct action. A fresh plan also
failed because v0.3.3 could never reuse its single fixed journal key,
even after the previous Undo. The v0.3.4 test fixes both workflow
problems, rather than merely keeping an unsafe REMOVE button enabled.

## Staged in-game workflow

Open the **Base Cleanup** panel with **Shift+ScrollLock** (remappable under
Insert > Keys). Only the controls relevant to the current step appear:

1. **Scan current Flame Altar**. This is a read-only 3-D census and must
   finish with zero unreadable cells to unlock the next step. Cancel
   the scan at any time; no voxel changes need undoing.
2. **Choose options**: rubble always included; optional **mud terrain
   material 37**, optional mineable mycelium, and optional cosmetic
   weathering repair are selected independently. All three optional
   settings default ON. The test warns that material 37 may be several
   metres thick: selecting it excavates the entire material volume.
   Back returns to a new read-only scan.
3. **Prepare selected options**. The server re-reads the entire Flame
   Altar for precise target counts and original 16-bit raw values.
   This is read-only and cancellable. Changed options invalidate any
   previous plan. Plans expire after 10 minutes.
4. **Review counts and REMOVE**. The UI only shows REMOVE if the server
   has a valid prepared plan. Type **REMOVE** exactly. The mod saves a
   new, uniquely keyed, complete original-voxel journal and obtains a
   verified full-world backup **before any write**. All selected voxels
   are guarded with `Build.WriteIfUnchanged`. A failed backup means
   zero edits. Stage 1's scan must be repeated for the next operation.
5. **UNDO this operation**. Type **UNDO** exactly. It restores only
   cells still in the expected post-edit state, skipping external edits
   rather than overwriting them. Successful Undo returns to **Step 1:
   Scan**, never to another unprepared REMOVE. For a second operation,
   scan → select → prepare → confirm again.

From Step 5 you may instead select **Start another operation (fresh
scan)**, retaining the preceding operation and its separately recoverable
journal. On game reload, an outstanding recovery is accessible from
Step 1. Multiple operations can be undone **individually, newest first**
(LIFO) to protect dependencies. After one Undo, the next previous
operation becomes available. No two REMOVE operations share a journal.
The mod stores at most **32 operations per altar**; at that bound it
refuses to create more, rather than silently destroy an earlier
recovery record. This cap is intentionally conservative for testing.

### Existing journals remain readable

- `cleanup-rollback-v1-...` — Base Cleanup v0.2.0
- `cleanup-rollback-v2-...` — v0.3.1/v0.3.2
- `cleanup-rollback-v3-...` — the accepted v0.3.3 test
- `cleanup-rollback-v4-index-...` — new transaction index per altar
- `cleanup-rollback-v4-...-000001`, `...-000002`, etc. — **individual**
  backup / Undo journals for v0.3.4 operations.

Earlier records are never overwritten or removed. A prior fully
completed cleanup, or a completely undone cleanup, does not block a
new operation. Incomplete, interrupted, partly undone or conflicted
records **do** block new work until restored/reconciled.
The index is checked on every operation. Missing, corrupt or
non-monotonic history refuses new writes. An interrupted Undo is safe
to retry: it recognises already-restored cells as no-ops, not conflicts.

## Material and geometry safety

Stage 2 still removes rubble material IDs 8, 64, 66, 69, 74, 81, 87,
102, 107, 23, 36, 43, 44, 61, 62, 92. The mud option now targets
**terrain cell 37 instead of 91**. Mycelium selects 14, 33, 65, 72.
All these terrain targets are removed to AIR inside the entire 3-D
Flame Altar — not a surface decal removal or geometry-preserving
substitution. The original puddle sample showed thick material-37
terrain layers. **The mud option can excavate a creek, ditch or floor.**

The separate cosmetic decay toggle clears only bit `0x8000` from
building material 128–255 voxels. It preserves material, geometry,
the low paint bits and overgrowth flags. Ivy, props and repairs of
missing buildings remain outside this mod.

## In-game v0.3.4 acceptance test (copied world required)

1. Install the GitHub Actions `base-cleanup-staged-0.3.4-test` artifact's
   inner `base-cleanup-0.3.4.zip`, replacing v0.3.3. Never load two
   versions with the same Kiln mod ID.
2. Press **Shift+ScrollLock**. Only **Scan** should initially appear,
   with a clearly labelled option to recover an older operation if
   it is still undoable. Scan altar 1026. Check that only options
   appear when the 32,768,000-voxel read is complete.
3. Toggle off cosmetic decay/mycelium if you want a mud-centred
   experiment. **Prepare**; check that REMOVE is hidden while planning.
   Confirm prepared counts (especially material 37) before REMOVE.
4. Type **REMOVE**; verify a **new** full-world backup and operation
   number are reported, and the targeted voxels change as expected.
5. Type **UNDO**; verify all edited cells are restored with zero
   conflicts, and the panel returns to **Scan**, not REMOVE.
6. Repeat **Scan → options → Prepare → REMOVE → UNDO** in the SAME
   altar and save. This is the key regression: the new operation must
   have a DIFFERENT backup and journal number.
7. Optional: complete two operations without undoing the first,
   then undo them newest first. Verify each is independently restored.
   Upload Kiln support ZIP and relevant screenshots.

**Manual world-backup restore** on hosted Enshrouded still requires
closing the game and restoring matching world and Kiln mod-data files.
The in-game selective Undo is safe only where voxels have not been
modified by another writer.

## Build

Pinned Kiln dev.30.6 SDK, .NET 10:
`bash build-stage1.sh`.
Artifact: `bin/Release/base-cleanup-0.3.4.zip`.

PR workflow tests scanner, safety, mud37, staged UI, journal history
and Undo logic; builds with the official pinned SDK and verifies
the installable KilnPack archive. **No PR build publishes a release.**
The PR stays draft until in-game acceptance.
