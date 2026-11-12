# Kiln mods

Enshrouded mods using the [Kiln SDK](https://github.com/frostdev-ops/kiln), maintained here as independent projects.

## Survival QoL — first mod

[Survival QoL documentation](SurvivalQoL/README.md) | [Build workflow](.github/workflows/build.yml)

Implements infinite stamina, infinite mana, fast health regeneration, fast mana regeneration, no fall damage and disabled equipment durability loss using Kiln's supported services.

**Survival QoL v0.2.1 test candidate:** Infinite oxygen and frost refills were **confirmed working in-game** on Kiln dev.21. At the player's request they are now **independent 1×–10× duration sliders** (10× initially selected; 1× is vanilla). The new proportional-depletion behaviour awaits in-game acceptance. The obsolete 2× and infinite toggles have been removed from the UI; existing JSONC files can retain inert unknown keys. See [issue #2](https://github.com/bisdat/kiln-mods/issues/2).

Build with `bash scripts/build.sh` (requires git, .NET 10 and internet access), or use **Actions → Package Survival QoL** to download the `survival-qol-package` artifact from a successful run. Pushing a matching version tag (e.g. `v0.2.1`) also attaches the ready-to-install ZIP to a GitHub Release. Configuration is hot-reloadable.

See the mod README for limitations, installation, SDK pin and testing instructions.

## Endless Chests (Kiln) — experimental port

[Endless Chests user guide](EndlessChests/README.md) | [Sorted original catalogue (INI)](EndlessChests/defaults/endless-chests.defaults.ini) | [GitHub Releases](https://github.com/bisdat/kiln-mods/releases) | [Packaging workflow](.github/workflows/endless-chests.yml)

An independent Kiln-native storage refill mod. Version **0.4.3** fixes ambiguous **Rough Wood Block** identity ([issue #16](https://github.com/bisdat/kiln-mods/issues/16)) and reports other duplicate item names; it retains the complete original **195-group catalogue**, and the user confirmed the automatic rename/clear functionality works in-game. **New in v0.4.1:** names are **case-insensitive for every original group name** (including arbitrary mixed ASCII capitals, e.g. `sTaRtEr bLoCkS`); renaming to an **unrecognised** name now **preserves all existing items and stops refilling**, instead of deleting them. Renaming to a **recognised** group/alias/item still clears previous contents before the new refill. Renaming to **`Empty`** (any capitalization) still immediately clears everything without confirmation and keeps clearing while named Empty. Some very long individual item names have limited mixed-case matching; see the [complete documentation](EndlessChests/README.md). Back up your world before installing, because recognised renames and Empty are destructive. The optional console `echest empty` path continues to use preview/confirm, but in-game renaming does not. Experimental Kiln-only implementation; no ShroudForge DLL is loaded.

The original [`endless-chests.defaults.ini`](EndlessChests/defaults/endless-chests.defaults.ini) has **195 group names sorted alphabetically** within `[Groups]`, with the other INI sections sorted by their keys too. Each group's item list and all original settings are unchanged. Starting with the **`endless-chests-v0.4.2` GitHub Release**, download the **standalone `endless-chests.defaults.ini`** directly from the release's Assets alongside the installable `endless-chests-kiln-0.4.3.zip`. The INI is a readable reference/customization template, not a Kiln-installable mod; upgrading does not overwrite an existing user-modified `endless-chests.ini`.


## Building Tools for Kiln — cross-base Magic Chests

**Kiln adapter v1.0.0 · upstream Building Tools v0.31.2**

[Building Tools for Kiln source and documentation](BuildingToolsKiln/README.md) |
[Packaging workflow](.github/workflows/buildingtools-kiln-native-adapter.yml) |
[Releases](https://github.com/bisdat/kiln-mods/releases)

An **MIT-licensed upstream Building Tools DLL**, packaged unchanged
as a **Kiln-native mod**. Does **not** replace Kiln's dinput8.dll.
The user confirmed that the in-game menu and **Magic Chests from all
Flame Altar bases** work under Kiln on Linux Steam/Proton.

Enable with **Home → Options → Magic chests of all bases → On**.
Alternatively set GlobalMagicChests=1 under [Chests] in the
game-root BuildingTools.ini while the game is closed, then restart.
The upstream binary is preserved byte-for-byte with its original MIT
licence and translations; the production build pins upstream release
0.31.2 to its verified SHA256. Existing user INI configuration
survives upgrades.

Previous custom native Cross-base Storage implementation and all
related research were **archived**, not merged:
[archived research](docs/ARCHIVED-CROSS-BASE-RESEARCH.md).
Do not install its experimental kiln-rules/kiln-items DLLs.
Remote ingredient debit/save-reload conservation still deserves
separate acceptance testing; see the user guide.

## Enhanced Fishing Rod — stronger Excellent Fishing Rod

[Enhanced Fishing Rod instructions](EnhancedFishingRod/README.md) |
[Packaging workflow](.github/workflows/enhanced-fishing-rod.yml)

Standalone data-only Kiln mod v0.1.0: changes `Tool_T6_FishingRod_epic` Fishing Strength from **2.5 to 5.0** and Fishing Endurance from **55 to 110**, without changing its crafting recipe, rarity, other rods or hidden loot multipliers. Requires Kiln dev.22 or newer. This is a **test candidate** awaiting in-game confirmation; package with `python3 EnhancedFishingRod/package.py` or use the GitHub Actions ZIP.

## Debug Tools — build anywhere (test candidate)

[Debug Tools documentation](DebugTools/README.md) | [Packaging workflow](.github/workflows/debug-tools.yml)

An independent, administrator-only, **per-player** `/debugbuild on|off|status` command. Kiln's supported placement rules lift the outside-Flame-Altar, Shroud and dungeon/POI no-build restrictions, with optional persistence and free construction. All debug building starts **off**; other players' base permissions are unchanged. **Flame Altar placement in the Shroud and dungeons requires in-game testing** because independent altar limits are not bypassed. Version 0.1.0 is not accepted or released yet.

## Licence

Original code, scripts and documentation in this repository are available under
the [MIT License](LICENSE), copyright © 2026 bisdat.

Third-party material is **not relicensed** by this notice. In particular,
Building Tools remains © Aleksander Wojtal under its own MIT licence, and the
Endless Chests catalogue incorporates material attributed to HipShot808.
See [Third-party notices](THIRD_PARTY_NOTICES.md) for attribution and scope.
