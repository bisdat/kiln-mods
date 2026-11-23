# Base Cleanup v0.3.0 — experimental candidate, optional cosmetic decay

Standalone Kiln mod for Enshrouded, developed in bisdat/kiln-mods only.
**Draft PR test candidate, not a published release.** Stable v0.2.0 is unchanged.

## What's new

An explicit **Repair cosmetic decay (OFF by default)** checkbox has been
added to the existing Stage 2 Base Cleanup UI. It is an additional target
in the existing **read-only plan → type REMOVE → mandatory world backup →
guarded cleanup → persistent selective UNDO** workflow. It does NOT
automatically edit anything when the mod loads or the toggle is checked.

Cosmetic decay only targets **building voxels (material IDs 128–255)**
whose high paint byte has its weathering flag (bit 7) set. The edit clears
**0x8000** from the exact original 16-bit voxel value; it preserves the
material ID, overgrowth bits 4–6, low paint/tint bits 0–3, and geometry.

The real in-game diagnostic probe demonstrated, for material 138:
- `0x978A → 0x178A` (clear weathering, preserve low paint).
- `0xF78A → 0x778A` (clear weathering, preserve overgrowth level 7).

The probe also found normal-looking walls with growth level 7.
**Do not indiscriminately clear 0x7000.** Overgrowth/ivy removal is NOT
implemented by this candidate. It does not restore missing physical walls,
repair missing roofs, remove vegetation entities or recreate props.

## Existing Base Cleanup features retained

- Stage 1 survey of ALL voxels inside the exact 3-D Flame Altar zone,
  including buried/floating cells, with unreadable tiles reported.
- Stage 2 always targets rubble material IDs
  8, 64, 66, 69, 74, 81, 87, 102, 107, 23, 36, 43, 44, 61, 62, 92.
  This remains a full-volume terrain removal, WITHOUT surface/support
  filtering; existing rubble is always included in the plan.
- Optional underwater mud (material 91), ON by default.
- Optional mineable mycelium (14, 33, 65, 72), ON by default.
- **Optional cosmetic building decay, OFF by default.**
- Client/server checks, progress/cancel, actor authorization, world and
  altar identity checks and a 10-minute plan expiry.
- Exact per-cell original voxel values in an altar-specific compressed
  WorldData journal, saved BEFORE the complete world backup.
- Backup must confirm matching world ID and mod-data bundles before any
  modification. Failure means zero writes.
- `Build.WriteIfUnchanged` uses the exact original raw voxel value as
  the expected state; conflicting cells are skipped. Failed/partial edits
  retain the recovery journal.
- Selective Undo only writes where the present value equals the exact
  expected post-cleanup state: **air for terrain removal**, or the
  original building value with its weathering bit cleared for decay.
  Any other player/building changes are NEVER overwritten.

## Compatibility with existing v0.2.0 journals

The v0.2.0 per-altar recovery journal remains at its original
`cleanup-rollback-v1-...` world-data key. The v0.3.0 candidate writes
a separate `cleanup-rollback-v2-...` key and **never overwrites v0.2
journal content**.

If an old cleanup has been fully undone (`phase=undone`, at least one
edit committed, total restored >= total committed, zero conflicts),
v0.3.0 permits a new plan in the same altar while preserving the old
record. Otherwise the old outstanding journal **blocks new cleanup**
until its recovery is resolved. Undo prefers the new journal if one
exists and otherwise still supports the old format.

v0.3.0 also refuses a second cleanup in an altar while a new journal
exists, even if it has been undone: the first v0.3 journal cannot be
silently discarded or replaced. The backup remains available.

## Install and test on a copied world

1. Download the test artifact from this PR's successful
   **Test Base Cleanup v0.3.0 optional decay** GitHub Actions run:
   `base-cleanup-decay-0.3.0-test`. Extract its inner
   `base-cleanup-0.3.0.zip` and install through Kiln Launcher.
   This **replaces v0.2.0** for the test (the ID is unchanged).
2. Keep a separate copy of the save before the test. Use a Flame Altar
   with visible weathering. The current in-game evidence was captured on
   Kiln dev.30.6; this candidate is compiled against that SDK.
3. Press **Ctrl+Shift+F10** to open the existing Base Cleanup panel.
4. The cosmetic decay checkbox is initially **OFF**. Tick
   **Repair cosmetic decay**. Mud and mycelium retain their own toggles.
5. Click **Prepare removal plan**. The server scans the complete altar
   and reports **cosmetic decay** count separately from rubble, mud and
   mycelium. Planning itself makes NO world edits.
6. Review ALL candidate counts. **Rubble is still included** by design.
   If you change any checkbox after planning, the commit will be
   refused; prepare again to take a new snapshot of selected options.
7. Type **REMOVE** and select Confirm. A full-world backup and complete
   undo journal MUST succeed before the first terrain or building edit.
8. Check that visibly decayed surfaces become clean, that ivy/overgrowth
   and other building colours remain unchanged, and that rubble (if
   present) is removed.
9. Type **UNDO** and select Undo previous cleanup. Confirm that both
   removed terrain and repaired building weathering are restored.
   Save/restart and re-check journal availability if possible.
10. Upload a Kiln support report and ideally before/after screenshots.
    No merge or release until in-game acceptance.

### Admin console commands

- `/basescan`, `/basescan status`, `/basescan cancel`,
  `/basescan report`, `/basescan materials [page]` — Stage 1.
- `/baseclean prepare` — old behavior, rubble+mud+mycelium;
  **decay is NOT enabled by default** for legacy scripts.
- `/baseclean preparedecay` — explicit opt-in to include decay in
  the same plan; also includes rubble, mud and mycelium.
- `/baseclean confirm REMOVE` — backup and commit the exact
  options of the server-side prepared plan.
- `/baseclean undo UNDO` — conflict-safe selective rollback.
- `/baseclean status`, `/baseclean cancel`.

## Backup caveats

On a HOSTED Enshrouded world, Kiln cannot automatically restore an
entire world snapshot with `Saves.RestoreOnNextStart`; that requires
manual replacement of the matching world and Kiln mod-data generations
while the game is closed. The in-game selective Undo is different: it
restores ONLY unchanged cells in the saved journal. Avoid editing the
affected building surfaces between cleanup and Undo for exact recovery.

An air cell created by an unrelated action cannot always be
distinguished from air created by this mod; this is an existing
limitation of the original Stage 2 journal.

## Build

From repo root with .NET 10, run `bash build-stage1.sh`.
The script uses SHA256-verified official Kiln dev.30.6 SDK assets,
runs all offline Python regression suites, builds and packs
`bin/Release/base-cleanup-0.3.0.zip`.

The PR workflow is intentionally test-only: **no automatic publication,
no merge, no upstream Kiln change**. Green CI is not an in-game PASS.
