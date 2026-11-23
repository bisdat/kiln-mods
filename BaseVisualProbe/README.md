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
- Cancel: stop a current read-only operation. Snapshots automatically expire
  on world switch, shutdown or mod unload.

The probe never writes world state or stores the snapshot in save files.

## Test procedure

1. Install the PR's test artifact named base-visual-probe-0.1.0-readonly.
   Extract the inner base-visual-probe-0.1.0.zip and install via Kiln Launcher.
   It has a separate mod ID and can run alongside Base Cleanup v0.2.0.
2. Stand in an existing Flame Altar containing weathered or ivy-covered walls.
   Press **Ctrl+Shift+F11** (remappable via Insert > Keys).
3. Select **Census full Flame Altar**. Its full material/flag histogram goes
   to kiln.log. Missing tiles abort the census instead of reporting zero.
4. Stand close to a wall where you will manually remove ivy.
   Select **Capture nearby ~12 m cube**.
5. Use the game's **Overgrowth Tool** on that wall, then choose
   **Compare same cube**. The cube stays fixed at the capture location.
6. Capture again before using the **Decay Tool** on a wall; compare again.
7. Ideally repeat on a roof, since the roof pathway is not confirmed.
8. Upload a Kiln support report ZIP, and state whether each comparison was
   Overgrowth or Decay and whether you used a wall or roof.

### Admin chat / console commands

- /vprobe census
- /vprobe capture
- /vprobe compare
- /vprobe report
- /vprobe cancel

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
