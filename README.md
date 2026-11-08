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

An independent Kiln-native storage refill mod. Version **0.4.2** uses the complete original **195-group catalogue**, and the user confirmed the automatic rename/clear functionality works in-game. **New in v0.4.1:** names are **case-insensitive for every original group name** (including arbitrary mixed ASCII capitals, e.g. `sTaRtEr bLoCkS`); renaming to an **unrecognised** name now **preserves all existing items and stops refilling**, instead of deleting them. Renaming to a **recognised** group/alias/item still clears previous contents before the new refill. Renaming to **`Empty`** (any capitalization) still immediately clears everything without confirmation and keeps clearing while named Empty. Some very long individual item names have limited mixed-case matching; see the [complete documentation](EndlessChests/README.md). Back up your world before installing, because recognised renames and Empty are destructive. The optional console `echest empty` path continues to use preview/confirm, but in-game renaming does not. Experimental Kiln-only implementation; no ShroudForge DLL is loaded.

The original [`endless-chests.defaults.ini`](EndlessChests/defaults/endless-chests.defaults.ini) has **195 group names sorted alphabetically** within `[Groups]`, with the other INI sections sorted by their keys too. Each group's item list and all original settings are unchanged. Starting with the **`endless-chests-v0.4.2` GitHub Release**, download the **standalone `endless-chests.defaults.ini`** directly from the release's Assets alongside the installable `endless-chests-kiln-0.4.2.zip`. The INI is a readable reference/customization template, not a Kiln-installable mod; upgrading does not overwrite an existing user-modified `endless-chests.ini`.

## Cross-base Storage (Kiln) — discovery prototype, not yet a crafting mod

[Cross-base Storage technical guide](CrossBaseStorage/README.md) | [CI workflow](.github/workflows/cross-base-storage.yml)

**Goal:** let Magic Chests in different Enshrouded bases supply the game's usual building and crafting interfaces, with actual ingredients consumed only once. **Not implemented yet:** the pinned Kiln SDK can read loaded chest inventories but does not expose the engine's vanilla crafting/building material-source resolver or a supported cross-base consumer for remote Magic Chests. The `0.1.5` package offers safe, **read-only** administrator diagnostics (`cbstore scan`, `cbstore near`, `cbstore stock Wood Logs`, `cbstore templates`, `cbstore lastcraft`, `cbstore plan last`, `cbstore lastbuild`, `cbstore quote 3 String`) to identify loaded magic storage, inspect native recipe requirements, and observe vanilla crafting/building events. In-game scans have verified remote stock: 50 Magic Chests across two bases, with 70,500 Yellow Luminescent Blocks located in two chests at the distant base. **No items are moved or duplicated, and no gameplay effect is claimed.** The new native craft/build probes reveal where server events arrive but cannot enable the disabled client crafting button. Completion requires an upstream client/server material-source integration API and live end-to-end tests. **Versioning is numeric:** `0.1.3`, `0.1.4`, `0.1.5`, etc. A live craft/build hook test showed the services available, but no native request or building outcome was observed; the game may have rejected missing materials on the client side. `cbstore plan last` therefore no longer fails when no request exists, and `cbstore recipes` plus `cbstore plan <recipeID>` works without crafting. See the [integration design](CrossBaseStorage/ENGINE-BRIDGE.md). A live Workbench `plan` test located 716 String in two Magic Chests while the backpack had zero, but the second native recipe input is an unresolved category/placeholder with raw count zero. Version 0.1.4 explicitly reports that the true category ingredient cost is unknown instead of implying it is free. Version **0.1.5** adds a deterministic, **non-reserving read-only source-allocation quote** across player inventory and candidate Magic Chests with unit tests for shortages, duplicates and stable exact amounts. It does **not** make vanilla crafting/building consume remote materials.
