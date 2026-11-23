# Base Cleanup - Stage 2 (experimental v0.2.0)

Kiln mod for Enshrouded, developed only in bisdat/kiln-mods.
This is a TEST CANDIDATE, not a release or an upstream Kiln change.

## Stage 1 (preserved)
Press Ctrl+Shift+F10 in a Flame Altar's zone to survey its full
3D rectangular build volume. Counts filled voxels by material ID,
including buried and floating voxels; unreadable areas are reported
separately. No support or surface checks are used.

## Stage 2 (new)
A deliberately confirmed cleanup of terrain material IDs:

- Rubble: 8, 64, 66, 69, 74, 81, 87, 102, 107,
  23, 36, 43, 44, 61, 62, 92.
- Underwater mud (optional, enabled by default): 91.
- Mineable mycelium (optional, enabled by default): 14, 33, 65, 72.

Building blocks (material 128+), other terrain, vegetation entities,
surface overgrowth, and cosmetic decay are not targeted.

From the successful 23 Nov 2026 report on the 160 x 160 x 160 m
Flame Altar zone (32,768,000 voxels scanned, zero unreadable):
- Rubble material 23: 38,717 cells.
- Rubble material 43: 693 cells.
- Rubble material 66: 469 cells.
- Mud and mineable mycelium: none detected.
- Current candidate count: 39,879 if no intervening world changes.

Stage 2 rescans; it does not trust the historical material counts.

## SAFETY: TEST ON A COPIED WORLD

1. Prepare removal plan (read-only): surveys and records exact original
   voxel values. Unreadable areas abort planning without any change.
2. Review exact counts and warnings; type REMOVE and confirm while
   still in the same altar, within 10 minutes of preparation.
3. Kiln records the entire compressed sparse voxel undo journal in
   per-world WorldData. Mandatory Saves.Backup saves the world AND
   the matching Kiln mod data before the first edit.
   Backup failure means no voxels are modified.
4. Voxel removal writes air in small batches with
   Build.WriteIfUnchanged and explicit InBases + RespectZones,
   plus actor authorization and write verification.
   Other edits made since planning are skipped.
5. To undo, type UNDO. The original material and fill values are
   restored ONLY where the cell is still air. Other non-air
   player changes are not overwritten. The saved journal persists
   across ordinary saves and restarts.
6. The full-world backup remains separately available via Kiln.Saves.
   A full-world restore resets ALL later world changes, including
   unrelated gameplay, and applies only after a restart.

NOTE: air from our cleanup is indistinguishable from an independently
edited voxel also set to air later. Do not build or terraform between
cleanup and undo if you need an exact restoration. Kiln's world and
mod-data generations must match; an out-of-sync journal refuses undo.

Only one active journal per world is allowed in this candidate;
new cleanup plans are refused while an old journal remains, even
after undo. That prevents replacing the only recovery record.
There are NO structural, surface, depth, gravity or support checks.

## Installation and controls

1. On PR #69's successful Stage 2 CI workflow, download artifact
   base-cleanup-stage2-0.2.0. Extract the inner
   base-cleanup-0.2.0.zip and install using the Kiln Launcher.
2. Enable the mod in your profile, enter a copied world, stand
   in your target Flame Altar zone.
3. Press Ctrl+Shift+F10 (remappable via Insert > Keys).
4. Prepare removal plan. Review the candidate rubble/mud counts.
5. Type REMOVE exactly. Confirm backup and removal.
6. Review visuals and log, save and restart.
7. For selective rollback, type UNDO and activate Undo previous cleanup.

The UI includes progress and cancellation, with server-side permission
checks. The server computes all changes; network requests never supply
a forged player identity.

## Chat / console commands (admin)

Stage 1:
- /basescan
- /basescan status | cancel | report
- /basescan materials [page]

Stage 2:
- /baseclean prepare
- /baseclean status
- /baseclean confirm REMOVE
- /baseclean cancel
- /baseclean undo UNDO

Host offline worlds without chat can use kiln/control/console.

## Build and CI

Requires .NET 10 and official Kiln 0.2.0-dev.30.6 SDK. From the
repository root run:

    bash build-stage1.sh

This keeps the original entrypoint on existing PR #69 and now runs
Stage 1 and Stage 2 regression tests, checksum-verifies SDK assets,
compiles and packages bin/Release/base-cleanup-0.2.0.zip.

CI uploads an installable TEST artifact. No release, tag, merge
or upstream changes are made.

## In-game acceptance (copied save)

- [ ] Stage 1 remains correct, with no unreadable cells.
- [ ] Plan reports expected rubble (approximately 39,879 if unchanged).
- [ ] World remains unchanged before explicit REMOVE confirmation.
- [ ] Backup and journal demonstrably precede any removal.
- [ ] Identified rubble disappears, including buried areas.
- [ ] Unrelated building material and natural terrain remain.
- [ ] Cancel leaves persistent undo available for committed cells.
- [ ] Save and restart: cleanup persists and Undo remains available.
- [ ] Undo restores original material/fill values; verify after restart.
- [ ] No unhandled errors, hangs or network desync.

A green CI run is not an in-game PASS. Inspect kiln.log, a follow-up
Stage 1 scan, and before/after screenshots.
