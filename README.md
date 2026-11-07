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

An independent Kiln-native storage refill mod. Version **0.4.0** uses the original **195-group catalogue** and recognizes exact names from Enshrouded's in-game **Rename** interface. The user confirmed automatic refilling for `Tools`, `Starter Blocks` and `Animals` in v0.3.0. **New destructive behaviour requested for v0.4.0:** a detected game-UI rename **permanently clears the chest's existing contents** *before* refilling the new group; renaming to **`Empty`** immediately clears everything and keeps clearing, **with no confirmation**. Rename to an unknown name also clears and leaves it unfilled. A chest observed for the first time after startup is not wiped just because it has a group name; `Empty` is the exception and clears on startup. Automatic renaming also removes any old manual binding for that chest. **Back up your world** and test on disposable items. The optional console `echest empty` path still uses preview/confirm, but renaming in-game does not. This remains an experimental Kiln-only port (no original ShroudForge DLL), pending a live v0.4.0 deletion test.

