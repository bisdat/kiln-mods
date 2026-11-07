# Kiln mods

Enshrouded mods using the [Kiln SDK](https://github.com/frostdev-ops/kiln), maintained here as independent projects.

## Survival QoL — first mod

[Survival QoL documentation](SurvivalQoL/README.md) | [Build workflow](.github/workflows/build.yml)

Implements infinite stamina, infinite mana, fast health regeneration, fast mana regeneration, no fall damage and disabled equipment durability loss using Kiln's supported services.

**SDK limitation:** underwater oxygen and frost depletion cannot currently be made truly infinite through the documented API. The mod includes clearly labelled **2× underwater breath** and **2× body heat** options instead.

Build with `bash scripts/build.sh` (requires git, .NET 10 and internet access), or use **Actions → Package Survival QoL** to download the `survival-qol-package` artifact from a successful run. Pushing a matching version tag (e.g. `v0.1.0`) also attaches the ready-to-install ZIP to a GitHub Release. Configuration is hot-reloadable.

See the mod README for limitations, installation, SDK pin and testing instructions.

## Endless Chests (Kiln) — experimental port

[Endless Chests README](EndlessChests/README.md) | [Packaging workflow](.github/workflows/endless-chests.yml)

An independent Kiln-native storage refill mod. Version **0.4.1** uses the complete original **195-group catalogue**, and the user confirmed the automatic rename/clear functionality works in-game. **New in v0.4.1:** names are **case-insensitive for every original group name** (including arbitrary mixed ASCII capitals, e.g. `sTaRtEr bLoCkS`); renaming to an **unrecognised** name now **preserves all existing items and stops refilling**, instead of deleting them. Renaming to a **recognised** group/alias/item still clears previous contents before the new refill. Renaming to **`Empty`** (any capitalization) still immediately clears everything without confirmation and keeps clearing while named Empty. Some very long individual item names have limited mixed-case matching; see the [complete documentation](EndlessChests/README.md). Back up your world before installing, because recognised renames and Empty are destructive. The optional console `echest empty` path continues to use preview/confirm, but in-game renaming does not. Experimental Kiln-only implementation; no ShroudForge DLL is loaded.

