# Base Cleanup - Stage 1 (read-only survey)

Experimental Kiln mod for Enshrouded. Version 0.1.0. This is an
unaccepted test candidate that never edits world blocks or save data.
Built against the official Kiln 0.2.0-dev.30.6 SDK.

## Scope

- Detects the Flame Altar build zone containing the character.
- Uses the actual 3D rectangular altar boundaries, not a spherical guess.
  If altar zones overlap, the closest containing altar is scanned, not
  the union of all overlapping zones.
- Inventories **every filled voxel**, including buried and suspended
  materials. No structural support, surface or exposure filters.
- Separately counts 9 placeable rubble and 7 world-authored rubble
  materials, 4 mineable mycelium materials, and underwater mud (ID 91).
- Reports counts and fill-weighted cubic metres for every material ID.
  Zero-filled cells are air even when the voxel's material byte is set.
- Streams voxel batches of at most 8,192 cells, with progress and cancel.
- Unavailable tiles are counted as unreadable, never assumed empty.
- Surface overgrowth and cosmetic decay are not detected by voxel reads.

## Installation and use

Download the artifact from the Package Base Cleanup Stage 1 workflow on
the draft PR. GitHub wraps an installable Kiln ZIP in a downloadable
artifact archive; extract the **inner base-cleanup-0.1.0.zip** before
installing it with the Kiln Launcher. Enable it in your Kiln profile.

1. Open your hosted or joined world; stand within the altar build zone.
2. Press Ctrl+Shift+F10 (remappable in Insert > Keys).
3. Choose **Scan current Flame Altar**.
4. View survey progress and the exact material inventory. Cancel if desired.
5. Examine kiln/logs/kiln.log for individual material IDs and volumes.

Host single-player uses the authoritative server tick without loopback
network assumptions. Joined clients with the mod may request a scan
from an admin account. No request carries a forged player entity.

## Chat / console commands

- /basescan : start a new survey where the player stands
- /basescan status : see progress
- /basescan cancel : request cancellation
- /basescan report : summary
- /basescan materials [page] : materials per 12-row page

Offline hosted worlds may run commands using Kiln's
kiln/control/console folder, acting as the current local player.

## Building

In a checkout of bisdat/kiln-mods with .NET 10 installed:

    bash build-stage1.sh

The script verifies the material logic/read-only contract, fetches the
official SDK assets (SHA256 verified against upstream's SHA256SUMS),
restores and compiles the mod, and produces:

    bin/Release/base-cleanup-0.1.0.zip

The PR CI runs the same steps and uploads only a test artifact. It does
not publish a release or tag, merge or change upstream Kiln.

## In-game acceptance (use a copied save)

- [ ] Mod loads; Ctrl+Shift+F10 opens preview.
- [ ] Current altar and displayed zone dimensions are correct.
- [ ] Full scan completes or precisely reports unreadable cells.
- [ ] Buried rubble, freely floating construction and underground mud
      are included in counts; air cells are not.
- [ ] Cancel and re-scan both work, with progress in the panel.
- [ ] /basescan report and /basescan materials agree with the panel.
- [ ] Game stays responsive during scanning.
- [ ] Save/restart confirms zero world modifications.
- [ ] Multiplayer permissions and host-side read are validated if used.

This is Stage 1 only. Destructive Stage 2 and rollback require separate
approval. In-game acceptance is not asserted by successful CI.
