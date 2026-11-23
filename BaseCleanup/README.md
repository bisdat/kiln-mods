# Base Cleanup v0.3.3 — material-37 mud experiment

**Experimental PR test candidate only. NOT merged or released.**
The stable release is [Base Cleanup v0.3.2](https://github.com/bisdat/kiln-mods/releases/tag/base-cleanup-v0.3.2).
This experimental package has the same Kiln mod ID (`base-cleanup`) and
display name **Base Cleanup**. Install it INSTEAD of the released package.

## What changed

The **Include creek/ditch mud (material 37) — TEST** checkbox replaces
the previous **Include underwater mud (material 91)** option in Stage 2.
The same `IncludeMud` switch, default ON, now selects only **terrain material
37** for mud removal. Material 91 is excluded. The Stage 1 inventory and
Stage 2 prepared-plan mud counts use the new ID 37.

Read-only floor probes in the user's existing Flame Altar found:
- Visibly muddy/puddled ground: terrain material **37**, including a
  partially filled top and several fully filled subsurface voxel layers.
- Road: material **21**; grass: material **1**.
- Underwater Mud, material **91**, absent from the prior complete altar scan.

**IMPORTANT:** Stage 2 replaces all selected material-37 terrain voxels
throughout the **entire 3-D Flame Altar** with AIR, including buried voxels.
It does NOT just remove a puddle texture or preserve floor geometry.
This can **excavate a creek bed, ditch, or floor**, potentially creating
large holes or changing water-related visuals. The aim is to test the
user's hypothesis that physical digging is required to remove all mud
from water-feature sites, not to assume the outcome.

## Unchanged safety and controls

- Stage 1: non-destructive census of the complete 3-D Flame Altar.
- Stage 2: rubble always included (materials 8, 64, 66, 69, 74,
  81, 87, 102, 107, 23, 36, 43, 44, 61, 62, 92), with optional
  material-37 mud, optional mycelium (14, 33, 65, 72), and cosmetic
  weathering repair. All three options are ON by default and may be
  independently disabled.
- Cosmetic decay repair touches **only weathering bit 0x8000** on building
  voxels; material and other paint metadata stay unchanged.
- Existing UI hotkey **Shift+ScrollLock** and Kiln display name
  **Base Cleanup** remain unchanged.
- **Read-only Prepare** provides per-category counts. Changing options
  after preparing invalidates confirmation; prepare a new plan instead.
- **REMOVE** confirmation is required. The mod saves a complete,
  compressed original-voxel Undo journal and verifies a full-world
  backup before ANY write. If backup fails, NO voxels are edited.
- `Build.WriteIfUnchanged` compare-writes only exact preplanned values,
  under the player/base authorization and protection checks.
- **UNDO** restores original material/fill only where each edited
  voxel is STILL in the mod's expected post-cleanup state (air for terrain
  removal, or cleared weather bit for cosmetic decay); conflicting
  player edits are left untouched.
- Overgrowth/ivy removal, props, and custom surface material substitution
  remain excluded.

## Journal compatibility — essential for the existing world

Previously published journal keys are NEVER overwritten:
- `cleanup-rollback-v1-...`: Base Cleanup v0.2.
- `cleanup-rollback-v2-...`: v0.3.1/v0.3.2.
- `cleanup-rollback-v3-...`: **this v0.3.3 test only**.

The same stable Flame Altar geometry hash is used across all generations.
The test permits another cleanup in the same altar after a prior journal
has either been **fully undone** (restored >= removed, no conflicts), OR
is a **fully completed and verified cleanup** (phase complete, planned
equals removed, restored zero, no conflicts). A completed cleanup does
not need to be undone: the experiment will save a separate v3 journal
without overwriting or reverting accepted v0.3.2 rubble/decay work.

This was specifically verified against the user's real report on 23 Nov:
prior v0.3.2 journal **88,057 edits**, phase **complete**, **0 restored**,
**0 conflicts**. The previous v0.3.3 candidate incorrectly refused the
read-only preparation; this revision corrects the journal gate.

A prior partial, interrupted, conflicted, or partly undone cleanup
**still blocks** new preparation, preserving the ability to recover it
first. Undo prefers an outstanding journal in newest-to-oldest order;
older recovery records remain available.

A completed v0.3.3 test journal is never overwritten by another test
in the same altar, even if fully undone. The backups and old recovery
data remain available.

## In-game test — use a copied save

1. Download the `base-cleanup-mud37-0.3.3-test` Actions artifact from
   the experimental PR's GREEN build; extract the installable
   `base-cleanup-0.3.3.zip` and install it via Kiln. This replaces
   0.3.2; don't install two versions of the same mod ID.
2. Start inside the same Flame Altar where the probe observed the puddle.
   Open Base Cleanup with **Shift+ScrollLock**.
3. Verify the checkbox reads **Include creek/ditch mud (material 37) — TEST**.
   Untick **mineable mycelium** and **cosmetic decay** if you want to
   minimise unrelated work. Rubble remains included by design.
4. Click **Prepare removal plan (read-only)** and inspect all planned
   **rubble, mud, mycelium, decay** counts BEFORE committing. The mud
   number now means **all material-37 voxels**, not visible puddles;
   do not commit if it is unexpectedly large.
5. If counts are acceptable on your **copied save**, enter **REMOVE**
   and confirm. Check the backup succeeded; check what happened to the
   visually muddy area (including any new holes or lost ground), and
   whether other material-37 areas were affected.
6. Enter **UNDO** and verify all changed terrain and appearance bits
   are restored (zero unexpected conflicts). Do not dig, build, or
   change the tested area between cleanup and Undo.
7. Upload the Kiln support report and, ideally, before/after screenshots.

The same result can be tested through the existing admin commands:
`/baseclean prepare`, `/baseclean confirm REMOVE`,
`/baseclean undo UNDO`, `/baseclean status`,
`/baseclean cancel`. `/baseclean preparenodecay` can opt out of
cosmetic decay, but still includes rubble, mud 37, and mycelium.

On a HOSTED Enshrouded world, a **full-world backup** cannot be
automatically restored by the mod; manual replacement of the matching
game and Kiln mod-data generations with the game closed is required.
Selective Undo is in-game, but can only restore still-unchanged cells.
Keep an independent save copy before this destructive test.

## Build and PR policy

The project targets the checksum-pinned Kiln dev.30.6 SDK with .NET 10.
From the repository root: `bash build-stage1.sh`.
Output: `bin/Release/base-cleanup-0.3.3.zip`.

The GitHub PR workflow validates scanner/cleanup/decay/mud37 regression
suites, builds the mod, and packages an installable ZIP. **PR runs do
not publish releases.** This mod must not be merged or released until
the user reports an in-game PASS.
