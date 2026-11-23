# Rubble Remover (Kiln) — v0.1.3 test candidate

A craftable, removal-only building tool for Enshrouded. **Requires Kiln
0.2.0-dev.30.6 or newer on the host and any joined client.** Built against
the official Kiln SDK 0.2.0-dev.30.6. This is **not yet accepted in game**.

## What it does

- At the **Workbench**, craft **Rubble Remover** using **20 Rubble + 20 Mycelium + 1 Wood Log**.
- Equip the tool, enter building mode and choose **Small** (0.5 m),
  **Medium** (1.5 m) or **Large** (2.5 m) brush.
- Both primary and secondary building actions **remove** permitted terrain
  instead of placing it; use your usual, possibly remapped, building keys.
- Permitted cells: the nine rubble materials (8, 64, 66, 69, 74, 81, 87,
  102, 107), seven world-authored rubble variants (23, 36, 43, 44, 61,
  62, 92) and mineable mycelium (14, 33, 65, 72).
- No building blocks, non-voxel props, regular terrain, surface overgrowth,
  glow-dirt, underwater mud or bone deposits. Bone removal was not
  confirmed by the reference mod's gameplay tests, so is excluded here.
- Uses Kiln's native, server-authoritative terrain tool policy: normal
  permission/build-area checks, native brush geometry, mining drops and
  the game's building-action undo are retained. There is no background
  scanning, broad terrain dig, pointer patching or EML-R dependency.

v0.1.0 failed to load due to a mismatched Carpenter menu group (corrected to Workbench in v0.1.1). v0.1.1 and v0.1.2 both reported `output item rubble-remover.rubble-remover not found`. v0.1.2 baked the three brushes, but the **Kiln dev.30.6 native world-table loader skips documents whose only section is `toolBrushShapes` or `toolMaterialPools`**. v0.1.3 adds a **harmless empty `blueprints` section** to the generated `blocks.tables.json`; this recognised section makes the loader process the baked brush descriptors. This is a temporary shim for the upstream loader defect. It adds no blueprints or other content. CI verifies the final ZIP contains the shim and brushes.

This intentionally omits the reference mod's rubble **placement** mode.
The starting 0.1.3 implementation focuses only on the requested removal
tool. Until a custom icon is created it inherits Kiln's Overgrowth starter
icon/model, but is a separate named item with its own brush identities.

## Install

1. **Back up a test save** before editing terrain.
2. Install Kiln 0.2.0-dev.30.6 or newer and ensure the same mod is
   available to all clients in a multiplayer session (this is a
   `clients=mod` mod, not a server-only mod).
3. In GitHub Actions, open **Package Rubble Remover**, download the
   `rubble-remover-0.1.3` artifact and extract its inner ZIP.
4. Install that inner ZIP using the Kiln launcher/profile mod installer.
   Do not install the reference EML-R DLL/Lua package or copy its
   `mods/` directory alongside this one.
5. Fully quit and restart the game after replacing the mod. The three brush
   definitions should now be in the archive's `blocks.tables.json` and
   available at the **first clean boot**. Do not rely on repeated restarts
   to compensate for a package missing the table file.

The mod's item and recipe identities are Kiln-derived, **not the legacy
EML-R GUIDs**. Do not expect existing items crafted with the reference
mod to be carried across. This mod does not replace the vanilla Overgrowth
or Decay Tool.

## In-game acceptance (test save only)

- [ ] Mod loads without errors; no EML-R installed or required.
- [ ] Recipe appears at the Workbench; crafting uses exactly 20 Rubble,
      20 Mycelium and 1 Wood Log, yields one Rubble Remover.
- [ ] Inventory item and three separate brushes appear in building mode.
- [ ] With each brush and both actions, permitted rubble disappears while
      adjacent ordinary ground and player-made building voxels remain.
- [ ] Thick mineable mycelium disappears; surface growth is unaffected.
- [ ] Partial voxel fills and mining drops agree with normal game behaviour.
- [ ] Y / remapped building undo restores a valid removal without changing
      unrelated blocks.
- [ ] Outside a Flame Altar / inside protected areas the tool refuses edits.
- [ ] Vanilla building/Overgrowth tools behave identically after switching.
- [ ] Save, leave, reload and retest; also test joined multiplayer where
      the host alone authorises edits.

Kiln's native hook qualification may fail on a game executable build. In
that case the tool **must refuse** rather than fall back to unrestricted
digging. Compilation and static tests alone do not establish in-game
action, UI presentation, crafting or multiplayer compatibility.

## SDK and provenance

Dedicated CI downloads the official Kiln 0.2.0-dev.30.6 SDK packages and
verifies their SHA-256 values against the official release's SHA256SUMS
before compiling and packaging the mod. The project post-processes the
SDK-baked `blocks.tables.json` with `patch-worldtables.py` for Kiln dev.30.6's
native loader. Python 3 is needed when building locally. CI opens the
resulting ZIP and requires three radii plus the loader compatibility section;
a successfully compiled DLL alone is not enough. It only uploads a CI candidate
artifact — it does not publish a stable release or tag on merge.

Reference: the user-supplied **Rubble Tool 1.0** EML-R ZIP (22 Nov 2026)
was inspected for intended ingredients and terrain IDs. This source is a
**new MIT-licensed Kiln implementation**: no GPL-licensed Lua, native C++,
DLL, textures or legacy item identities were copied.
