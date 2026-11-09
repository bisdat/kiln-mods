# Kiln mods

Enshrouded mods using the [Kiln SDK](https://github.com/frostdev-ops/kiln), maintained here as independent projects.

## Survival QoL — first mod

[Survival QoL documentation](SurvivalQoL/README.md) | [Build workflow](.github/workflows/build.yml)

Implements infinite stamina, infinite mana, fast health regeneration, fast mana regeneration, no fall damage and disabled equipment durability loss using Kiln's supported services.

**SDK limitation:** underwater oxygen and frost depletion cannot currently be made truly infinite through the documented API. The mod includes clearly labelled **2× underwater breath** and **2× body heat** options instead.

Build with `bash scripts/build.sh` (requires git, .NET 10 and internet access), or use **Actions → Package Survival QoL** to download the `survival-qol-package` artifact from a successful run. Pushing a matching version tag (e.g. `v0.1.0`) also attaches the ready-to-install ZIP to a GitHub Release. Configuration is hot-reloadable.

See the mod README for limitations, installation, SDK pin and testing instructions.

## Endless Chests (Kiln) — experimental port

[Endless Chests user guide](EndlessChests/README.md) | [Sorted original catalogue (INI)](EndlessChests/defaults/endless-chests.defaults.ini) | [GitHub Releases](https://github.com/bisdat/kiln-mods/releases) | [Packaging workflow](.github/workflows/endless-chests.yml)

An independent Kiln-native storage refill mod. Version **0.4.3** fixes ambiguous **Rough Wood Block** identity ([issue #16](https://github.com/bisdat/kiln-mods/issues/16)) and reports other duplicate item names; it retains the complete original **195-group catalogue**, and the user confirmed the automatic rename/clear functionality works in-game. **New in v0.4.1:** names are **case-insensitive for every original group name** (including arbitrary mixed ASCII capitals, e.g. `sTaRtEr bLoCkS`); renaming to an **unrecognised** name now **preserves all existing items and stops refilling**, instead of deleting them. Renaming to a **recognised** group/alias/item still clears previous contents before the new refill. Renaming to **`Empty`** (any capitalization) still immediately clears everything without confirmation and keeps clearing while named Empty. Some very long individual item names have limited mixed-case matching; see the [complete documentation](EndlessChests/README.md). Back up your world before installing, because recognised renames and Empty are destructive. The optional console `echest empty` path continues to use preview/confirm, but in-game renaming does not. Experimental Kiln-only implementation; no ShroudForge DLL is loaded.

The original [`endless-chests.defaults.ini`](EndlessChests/defaults/endless-chests.defaults.ini) has **195 group names sorted alphabetically** within `[Groups]`, with the other INI sections sorted by their keys too. Each group's item list and all original settings are unchanged. Starting with the **`endless-chests-v0.4.2` GitHub Release**, download the **standalone `endless-chests.defaults.ini`** directly from the release's Assets alongside the installable `endless-chests-kiln-0.4.3.zip`. The INI is a readable reference/customization template, not a Kiln-installable mod; upgrading does not overwrite an existing user-modified `endless-chests.ini`.


## Building Tools for Kiln — cross-base Magic Chests

**Kiln adapter v1.0.0 · upstream Building Tools v0.31.2**

[Installation and settings](docs/BUILDINGTOOLS-KILN-ADAPTER.md) |
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
