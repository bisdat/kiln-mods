# Flame Altar Expanded

**Experimental / not yet validated in-game.** A separate [Kiln EML](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/eml.md) data-patch mod for *Enshrouded*.

## Intended behaviour

At a **level-4 Flame Altar** the normal altar menu should offer one further expansion to **level 5**, consuming **10 Shroud Nexus**, and raising that altar's building volume from **160 × 160 × 160 m** to **300 × 300 × 300 m**. Earlier tiers stay unchanged. This does not increase the world Flame strength or the number of altars.

The vanilla game's balancing table currently already allows altar level 5, but its fifth size is still 160 m and the native upgrade recipe for level 5 is absent. The mod patches **both** the balancing data and the output-less altar recipe at game startup. It clones the vanilla level-4 upgrade, preserving the original ingredient item and engine recipe fields, and assigns a permanently fixed GUID to the new level-5 recipe.

## Requirements and installation

This uses **Kiln's `kiln-eml` pregame plugin**, *not* the older `Kiln.Content` C# mod API. A Kiln installation must include `kiln/plugins/kiln-eml.dll`. Kiln's currently pinned SDK tag `v0.2.0-dev.15` does **not** include this plugin; it is documented in newer upstream Kiln development. Do not expect this mod to operate with the pinned SDK alone.

Install the package so the game contains one of:

- `<Enshrouded>/kiln/eml/mods/flame-altar-expanded/mod.json` and `src/mod.lua` beneath that folder; **or**
- `<Enshrouded>/mods/flame-altar-expanded/mod.json` and `src/mod.lua` beneath that folder.

An active Kiln profile must permit EML drop-ins, or explicitly register the mod. For multiplayer, install the same mod and compatible Kiln EML runtime on **both clients and the authoritative server**. Completely restart the game and server after changing it.

At startup inspect `kiln/logs/eml.log` and `kiln/state/eml.json` for `flame-altar-expanded`, and ensure the patch was applied rather than skipped or disabled.

## Verification before use on a real save

1. **Back up your world** or create a disposable test world; close the game/server before installing.
2. Start the client and server with the EML plugin and confirm that both loaded the mod successfully; verify the logs show the level-5 size patch and no game-data assertion failures.
3. At an altar upgraded through vanilla levels 1–4, check that a fifth upgrade **appears at level 4 only**; the expected cost is **10 Shroud Nexus**. Test that exactly 10 are consumed and altar level becomes 5.
4. Place blocks and terraform just beyond the old **80 m half-width**, up to a **150 m half-width**, and test analogous vertical limits. Check client cursor, server permission, and saved terrain after a full restart.
5. Check co-op and dedicated servers, including a reconnect. Confirm level-1 to level-4 altars and existing saves behave unchanged.

**Important:** The plugin currently has only structural/mock coverage, not a real Enshrouded gameplay pass. A copied altar upgrade may need additional engine-specific unlock/UI plumbing; do **not** interpret a successful CI run as proof that the fifth upgrade appears or works. Report gameplay results on [issue #12](https://github.com/bisdat/kiln-mods/issues/12).

**Uninstallation / save safety:** Do not remove this mod and then load/save a world containing builds or terraforming outside the vanilla level-4 area; those changes may fall outside the game's persistence boundary and be lost. Restore a world backup if reverting. The script refuses unexpected vanilla dimensions, upgrade ingredients, missing registries, and duplicate recipe IDs instead of guessing.

## Development

Source: `src/mod.lua`; manifest: `mod.json`. Run `lua5.4 tests/test_patch.lua` and `luac5.4 -p src/mod.lua` from this directory. The repository's `Package Flame Altar Expanded` GitHub Actions workflow tests and creates an installable ZIP, but does **not** mark this untested mod as a stable release.

License: MIT (original patch code). Kiln's bundled EML engine is separately GPL-licensed, maintained upstream and not redistributed in this mod.
