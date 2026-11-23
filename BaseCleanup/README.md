# Base Cleanup v0.3.6 — pre-REMOVE world backups, ten-copy retention, selective Undo

**Draft experimental test candidate only. Not merged or released.**
The stable GitHub release remains v0.3.2. This version builds on
the user's successful in-game v0.3.4 staged Scan → Options → Prepare
→ REMOVE → UNDO workflow and material-37 mud removal.

Display name: **Base Cleanup**. Mod ID: `base-cleanup`. Hotkey:
**Shift+ScrollLock** (remappable in Insert → Keys).

## Why the journals survive uninstall

Kiln stores Base Cleanup's journals through `WorldData` (JSON values
scoped to the `base-cleanup` mod inside a versioned per-world Kiln
sidecar), not inside the mod ZIP or directory:

`kiln/data/worlds/<world id>/`

Kiln intentionally keeps this mod data through a mod's disable,
reload, and launcher uninstall. `Mod.OnUnload` is also called on
ordinary game shutdown/reload/disable and therefore **must NOT
delete journals**: doing that would make logout destroy Undo.

**Supported cleanup before uninstall:** On EVERY world where this mod
was used, launch the game with Base Cleanup installed, enter that
world and run the two chat commands below; allow the game's next
normal autosave to persist the deletion, then uninstall the package
via Kiln Launcher.

A mod cannot guarantee deletion of historical generations already
stored in Kiln's shared world-data bundles or old **whole-world**
backups created by earlier versions. These may still exist after
purging the current keys, and neither the mod's uninstall nor
`WorldData.Remove` deletes those files. Do NOT manually delete
`kiln/data/worlds/` or `kiln/backups/` wholesale: they may
contain data for other mods/worlds/players. A true uninstall-time
purge of all retained generations requires Kiln launcher support.

## Automatic backup BEFORE each REMOVE

**Restored in v0.3.6:** After a complete Scan → Options → Prepare,
typing **REMOVE** first calls `Saves.Backup("bisdat-base-cleanup")`
on the authoritative host. Kiln saves the **entire world** now and
copies the matching world file and Kiln mod-data generation to a
timestamped folder. The operation **waits for a verified successful
backup** before changing even one voxel. If saving, verification,
retention or current-world validation fails, **NO voxels are edited**.
A backup is not created by Scan, Prepare, or UNDO.

The original per-operation WorldData Undo journal and its index are
registered **before** the backup, so the backup contains that
pre-REMOVE journal and the world's pre-cleanup terrain. A completed
REMOVE still retains its selective compare-and-write UNDO journal.
The mod does not request a second save after editing: the game's
normal subsequent saves persist the changed terrain and journal.

**Backup location (Kiln-controlled, not a mod ZIP or a game-save
slot):**

```text
<Enshrouded game directory>/kiln/backups/
  YYYYMMDD-HHmmss-bisdat-base-cleanup/
    backup.json                 # includes per-file SHA256
    savegame/world<N>.ksd        # complete world save
    worlds/<world GUID>/         # matching Kiln mod-data manifest and blobs
```

On the usual Bazzite Steam install, the root is:
`~/.local/share/Steam/steamapps/common/Enshrouded/kiln/backups/`.
The authoritative Kiln API path is `Saves.BackupDirectory`, so
dedicated-server or alternative game installs may differ. Each
backup's full timestamped folder name is reported in `kiln.log`
and the cleanup status.

**Retention:** At each successful pre-REMOVE backup, Base Cleanup
retains the **10 newest** backup folders with the exact
`bisdat-base-cleanup` backup label **for that same world GUID**.
Only its older matching folders are recursively deleted, after
checking each is a real directory directly below Kiln's backup root
(not a link), with the expected timestamped filename and valid
Kiln backup metadata. Backups for other worlds/mods, older legacy
`journal-op...` archives, manually made snapshots, and Kiln
restore files are **never pruned**. If rotation cannot be performed
safely, REMOVE refuses to edit terrain; a completed fresh backup
may still remain for inspection.

**No restore UI:** The mod provides no full-world restore button or
automatic restore action. The existing **UNDO** remains the
recommended selective rollback. Full-world restoration requires a
separate, deliberate manual recovery with the game stopped and
matching Steam/local world-save plus Kiln sidecar data; Kiln's
`Saves.RestoreOnNextStart` is **not supported on client-hosted
worlds**. Do not copy files over an active world or assume replacing
only the `.ksd` file restores mod data. Preserve a separate backup
outside Kiln's retention policy for irreplaceable saves.

**Impact:** `Saves.Backup` forces an actual world save of all
players' bases and may temporarily stall the authoritative host.
That is intentional for this requested pre-commit safety gate.

## Every cleanup category is selectable

Four independent checkboxes, initially ON to retain prior defaults:

1. **Rubble** — the former always-included category, material IDs
   8, 64, 66, 69, 74, 81, 87, 102, 107, 23, 36, 43, 44, 61, 62, 92.
2. **Creek/ditch mud** — terrain material 37. Material 91 is no longer
   targeted. The 37 material may extend metres deep: removing it
   excavates terrain, not just a decal. Check counts carefully.
3. **Mineable mycelium** — terrain 14, 33, 65, 72.
4. **Cosmetic decay** — building weathering flag `0x8000` only.
   Building material, tint and overgrowth bits are preserved.

You may use any nonempty combination, but **Undo is per REMOVE
operation**. To remove/restore one category independently, select
that category alone, prepare/commit, and use that operation's UNDO.
If you choose multiple categories together, the journal groups
them for one Undo. Later operations must be undone before earlier
ones (newest first), to avoid overwriting later edits.

A plan with all four categories OFF is refused; the server also
verifies the exact selected options on commit. Read-only planning
still expires after 10 minutes. Every new operation requires a fresh
complete 3-D altar scan with zero unreadable voxels.

### Staged UI

1. **Scan** the current Flame Altar (read-only).
2. Choose the four category checkboxes.
3. **Prepare** the selected options (read-only), inspect counts.
4. Type **REMOVE** to save a full timestamped world backup, then commit (separate persistent journal).
5. Type **UNDO** for selective rollback, or choose a fresh Scan for
   the next operation. Undo success returns to Scan.

Up to 32 individually journalled REMOVE operations per altar are
retained; new operations fail closed when this limit is reached,
rather than silently destroy any recovery history. A completed
operation can coexist with a later operation; interrupted/partial
or conflicted operations block new changes until resolved.
Recovery journals from v0.2, v0.3.2 and v0.3.3 remain readable.

## In-game chat: delete ALL Base Cleanup journals

**WARNING: irreversible.** This deletes all Base Cleanup rollback
keys in the **CURRENT loaded world**, across ALL its Flame Altars,
including legacy v1/v2/v3 and new v4 per-operation journals and
history indexes. It does NOT remove or restore terrain. Afterward
no Base Cleanup Undo will be possible for those operations.

The commands require admin permission and a loaded world whose Kiln
mod data exactly matches the game save. Never run them during
active cleanup, Undo or scanning.

First preview the current-world journal inventory:

```text
/baseclean journals
```

The game will reply with the number of matching keys, world ID and
a warning, and arm a time-limited confirmation for two minutes.
To permanently delete precisely those keys, type exactly:

```text
/baseclean journals delete DELETE_ALL_JOURNALS
```

The server confirms the same player, world, time window and that the
set of journal keys has not changed. An expired/stale/mismatched
preview refuses deletion. The command deletes `WorldData` keys
only; it does not edit any voxels, trigger any game save, request
a backup, remove other mods' records or touch filesystem paths.

**Wait for the game's next ordinary world autosave** before
uninstalling the mod, so the deleted keys are absent from the next
persistent generation. Historical generations / earlier backups
may still contain old records, as described above. If the mod is
already uninstalled, reinstall it in the same world to run this
command; simply deleting the mod ZIP is not a data purge.

## Test v0.3.6 on a COPIED world

1. Install the Actions test package `base-cleanup-0.3.6.zip`
   extracted from artifact `base-cleanup-precommit-backups-0.3.6-test`.
2. Survey a test Flame Altar. Untick Mud, Mycelium and Decay;
   confirm **rubble-only** Prepare/REMOVE edits only rubble.
3. UNDO and verify complete restoration and zero conflicts.
4. Repeat with **mud-only**, if that volume is acceptable;
   verify separate operation and independent Undo.
5. On **REMOVE**, verify a new timestamped
   `kiln/backups/YYYYMMDD-HHmmss-bisdat-base-cleanup/` folder exists
   before any voxel changes. Confirm its `backup.json`, world save and
   matching Kiln sidecar; logout/re-enter and confirm independent Undo.
6. Perform 11 small independent REMOVE operations (UNDO/re-scan
   between them as needed), checking that exactly the newest 10
   `bisdat-base-cleanup` backups remain for this world, and other
   world/legacy backups remain untouched.
7. After *fully undoing* tests and on a dispensable copied world,
   run the **two-step chat purge**. Verify the journal count becomes
   zero, Undo reports no saved journal, and the count stays zero
   after the next natural save/restart.

Do NOT purge the real world's accepted Undo history just to test
the command. The purge is destructive and cannot be rolled back
by Base Cleanup.

## Development

Built with the checksum-pinned official Kiln dev.30.6 SDK and .NET 10.
From the repository root:
`bash build-stage1.sh`.

CI runs the scanner/cleanup and independent-category tests, plus
pre-REMOVE backup ordering, ten-copy retention scoping and two-step
journal purge tests before compilation and KilnPack ZIP verification.

This is a test candidate until in-game acceptance. The PR does not
merge, tag or release the mod automatically.
