# Base Visual Metadata Probe v0.1.0 — experimental, READ ONLY

Standalone Kiln mod in bisdat/kiln-mods. It does not modify the released Base Cleanup v0.2.0.

**THIS MOD NEVER CLEANS, CHANGES, WRITES OR REMOVES VOXELS OR PROPS.**
Only read-only survey, before/after comparison and logging are implemented.

## Metadata hypothesis

Kiln Voxel.Raw is a 16-bit value: low 8 bits = material, high 8 bits = Fill.
For BUILDING materials (IDs 128–255), Fill represents paint metadata rather
than terrain fill. The upstream EMF block API describes the high byte as:
- Bit 7, hexadecimal 0x80: weathering/decay.
- Bits 4–6, hexadecimal 0x70: overgrowth (decoded as levels 0–7).
- Bits 0–3, hexadecimal 0x0F: other paint information; preserve it.

**A building voxel with Fill=0 is still occupied.** The existing terrain scanner's
Voxel.IsAir shortcut can undercount these building blocks. This experiment
classifies all material IDs 128–255 as buildings, even with zero paint.

The source establishes a representation, not necessarily the exact result of
using the game's Overgrowth and Decay Tools in all cases. This experiment is
designed to verify those changes before any cleanup implementation.

## Operations

- Full Flame Altar census: read the entire rectangular 3-D altar zone using
  8,192-cell batches. Count building voxels, material IDs, zero paint, overgrowth,
  weathering, combinations, low paint/tint and overgrowth levels. Log exact
  histograms and show headline counts in the UI.
- Capture: read a fixed approximately 12 m cube centred on the character and
  clipped inside the altar; retain every original 16-bit voxel value in RAM.
- Compare: re-read the SAME fixed cube after the user has operated the game's
  own building tools. Log flags added/removed, material differences and up to
  24 exact sample cell coordinates with raw before/after values.
- Track the game's own **BuildEvent** for Overgrowth / Decay Tool operations:
  logs operation kind (4–5 overgrowth, 6–7 decay), Done/Refused outcome,
  impact point, and whether it falls inside the captured cube.
  The compare summary includes counts since baseline and the state of
  the gameplay build hook. An accepted tool action is distinct from a
  verified change in voxel metadata.
- **Targeted clean-versus-overgrown wall profiling:** two new buttons
  collect separate **~5 metre cubes** around the player's current location.
  Reference A is chosen by the player on a visually CLEAN wall; reference B
  is chosen on a visibly IVY-COVERED wall. These are separate positions,
  NOT an automated ivy detector. Read-only results include building
  material/paint values, zero-paint buildings, the 0..7 growth levels,
  the most common exact 16-bit values, and *air-adjacent* building blocks.
  A block is only classified air-adjacent if all six neighbour voxels
  were read within the sample and at least one is air; other cells are
  counted separately as enclosed or boundary-unknown. Being air-adjacent
  does NOT prove that ivy is rendered. A and B results are logged and
  compared only when they belong to the same world and Flame Altar.
  **No full-altar rescan is needed for these samples.**
- Cancel: stop a current read-only operation. Snapshots automatically expire
  on world switch, shutdown or mod unload.

The probe never writes world state or stores the snapshot in save files.

## Test procedure

1. Install the PR's test artifact named base-visual-probe-0.1.0-readonly.
   Extract the inner base-visual-probe-0.1.0.zip and install via Kiln Launcher.
   It has a separate mod ID and can run alongside Base Cleanup v0.2.0.
2. Stand in an existing Flame Altar containing weathered or ivy-covered walls.
   Press **Ctrl+Shift+F11** (remappable via Insert > Keys).
3. **NEW: To investigate the unexpectedly large growth-level-7 count,
   skip the full-altar census.** Stand right next to a visually clean
   building wall (within ~1 metre), click **A. Sample CLEAN wall**.
   Then stand right next to an ivy-covered wall, click
   **B. Sample OVERGROWN wall**. Compare the two summaries in the UI,
   and inspect the exact raw samples and surface counts in kiln.log.
   Ideally choose walls made from the SAME building material and similar
   structure geometry. Take one screenshot of each so we know what
   each sample represents.
4. The older **Census full Flame Altar** remains optional. Its full
   material/flag histogram goes
   to kiln.log. Missing tiles abort the census instead of reporting zero.
5. Stand close to a wall where you will manually remove ivy.
   Select **Capture nearby ~12 m cube**.
6. Use the game's **Overgrowth Tool** on that wall, then choose
   **Compare same cube**. The cube stays fixed at the capture location.
7. Capture again before using the **Decay Tool** on a wall; compare again.
8. Ideally repeat on a roof, since the roof pathway is not confirmed.
9. If zero voxels change, check the new TOOL-SUMMARY in kiln.log for
   accepted/denied tool actions and whether their impact hit the cube.
   No tool events can be caused by a not-yet-active event hook.
10. Upload a Kiln support report ZIP, and state whether each comparison was
   Overgrowth or Decay and whether you used a wall or roof.

### Admin chat / console commands

- /vprobe census
- /vprobe capture
- /vprobe compare
- /vprobe report
- /vprobe cancel

**Local samples use the UI buttons** (Ctrl+Shift+F11): A. Sample CLEAN
wall and B. Sample OVERGROWN wall. You may also use the read-only
commands /vprobe spotclean and /vprobe spotivy where player chat is
available. These samples don't overwrite the separate 12m baseline for
before/after tool actions.

In-world UI and joined clients use the authoritative host, requiring admin
permission. Exact per-cell examples are in kiln.log rather than on a
separate downloadable file.

## Interpretation and next steps

If overgrowth or weathering flags clear with the building MATERIAL UNCHANGED,
a selective metadata editor may work. If props or material IDs change, a
different mechanism is needed. Ivy that remains visually present even when
the flag count is zero could be an entity, texture, or another representation.
Missing physical structure requires a separate reconstruction strategy.

A later cleanup implementation can reuse the backed-up, per-altar undo
workflow already proven in Base Cleanup: read-only preview, mandatory backup,
explicit confirmation, guarded compare-and-write and selective undo.

## Build and CI

Requires .NET 10 and the SHA256-verified official Kiln dev.30.6 SDK.
From the repo root:

    bash build-visual-probe.sh

The separate GitHub Actions PR workflow packages
bin/Release/base-visual-probe-0.1.0.zip as a TEMPORARY TEST ARTIFACT.
No release or push-to-main job, no merge, and no upstream changes.
