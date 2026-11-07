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

An independent Kiln-native storage refill mod. Version **0.3.0** uses the complete original **195-group INI catalogue** and automatically recognizes exact chest names from Enshrouded's in-game **Rename** interface! This works by matching the game's `CustomPlayerString` hash to the FNV-1a64 fingerprint of every original name; two in-game probes proved the algorithm. **Rename an unbound chest to `Starter Blocks`, `Tools`, or another original group and it refills automatically**; existing manual `echest bind` assignments retain precedence. `echest bind Empty` previews irreversible deletion but **still requires** `echest empty confirm` within 60 seconds; renaming to `Empty` never auto-deletes. Use `echest nameprobe` to inspect the game's recognized name hash. The mod is a standalone Kiln SDK implementation, does not load the ShroudForge DLL, and remains experimental pending an in-game automatic refill test.

