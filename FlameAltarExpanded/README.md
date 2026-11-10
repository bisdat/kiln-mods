# Flame Altar Expanded — 0.1.2 (level-4 range experiment)

> **EXPERIMENTAL / NOT GAMEPLAY-ACCEPTED.** Version **0.1.2** retains the original **`flame-altar-expanded`** EML mod identity and tests a *different approach* from versions 0.1.0–0.1.1. Use only Kiln `v0.2.0-dev.21` or later and a disposable or backed-up world. The previous separately identified `flame-altar-level4-range-test` package caused a startup problem and was withdrawn. Do not enable that retired test package.

## What 0.1.2 actually changes

**At an existing level-4 Flame Altar only:** the BalancingTable building volume changes from **160 × 160 × 160 m** (80 m half-width) to **300 × 300 × 300 m** (150 m half-width). All lower levels remain unchanged. The sixth entry (altar level 5) remains the vanilla 160 m. The maximum altar level is not modified.

**No new upgrade button, recipe, ingredient cost, or fifth tier.** Upgrade Altar may remain disabled on a level-4 altar. This release is designed to find out whether game UI, construction permissions and save/persistence respond to altering the *existing level-4 size*, before deciding whether to resume the level-5 recipe investigation in [issue #12](https://github.com/bisdat/kiln-mods/issues/12).

The Lua code edits **only** `keen::BalancingTable.buildzoneSizesPerAltarLevel[5]` (Lua's 1-based indexing, level 4), after verifying exactly one balancing table, six entries, vanilla tier values 1–5 and `altarLevelMax=5`. It writes no `RecipeRegistryResource`. Unknown layouts and another mod changing these values cause an explicit failure rather than a partial patch.

## How to test safely

1. Close Enshrouded and Kiln. **Disable/remove both previous Flame Altar packages** from the active Kiln profile, including the retired `flame-altar-level4-range-test`. Do not run two versions of the mod.
2. If any EML patch was previously active, launch with **no EML mods enabled**, reach the game main menu, then quit. Kiln should restore the vanilla KFC from its managed backup. Do **not** delete KFC data, its backup, `.dat` files or the EML cache by hand.
3. Import the **single installable `flame-altar-expanded-0.1.2.zip`** into Kiln Library by dragging it onto the launcher. Confirm its ID/name is **Flame Altar Expanded** and its version is **0.1.2** in the active profile. It must replace the older active version, not coexist.
4. Launch Enshrouded and reach the main menu. If the **launcher or game fails to start, stop here**, disable the mod and capture `kiln/logs/proxy.log`, `kiln/logs/kiln.log`, `kiln/logs/eml.log`, `kiln/state/boot.json`, `kiln/state/eml.json` and (if applicable) the launcher log before another run. Successful EML patching on an uncached run should log the exact 0.1.2 level-4 message with `patched=1`, `phase=done`, `error=null`. Cached subsequent runs may have `patched=0` and `cached=true`.
5. Only if startup succeeds, load a **disposable/copied world containing a level-4 altar**. Inspect its building-area text: expected **300 × 300 × 300 m** instead of 160 × 160 × 160 m. The upgrade option need not be enabled.
6. If the menu shows 300 m, test in that disposable world whether a temporary block can be placed around **100 m horizontally from the altar centre** (outside the vanilla 80 m half-width, within the proposed 150 m), avoiding other altar zones and no-build areas. If it works, optionally test saving, quitting, and reopening with the mod *still enabled* before testing vertical limits.
7. Report a screenshot of the level-4 altar UI, whether building works outside the vanilla radius, and `kiln/state/eml.json` and `kiln/logs/eml.log`. Distinguish patch success, displayed area, actual build permission, and persistence.

## Rollback and save boundaries

Disable 0.1.2 in Kiln and launch with no EML mods to allow the official Kiln EML backup restore. **Do not re-open a world containing construction/terraforming outside vanilla bounds without the mod.** Restore/discard your test world instead. Do not touch important saves, KFC backup files or the EML cache manually.

For multiplayer, both the authoritative host/server and the clients must have matching patches; this has **not** been tested. Changes in KFC values do not establish how the game actually persists expanded building volumes.

## Development

- Source: `src/mod.lua`; manifest: `mod.json`; offline userdata-backed Lua 5.4 regressions: `tests/test_patch.lua`.
- Draft [PR #13](https://github.com/bisdat/kiln-mods/pull/13) keeps this experiment separate from stable releases. Offline tests and CI packaging do not establish in-game acceptance.
- [Previous level-5 engine investigation](ENGINE-INVESTIGATION.md) is retained as research, not part of the 0.1.2 implementation.

MIT-licensed patch code; Kiln's bundled GPL-licensed EML runtime is maintained upstream and is not redistributed.
