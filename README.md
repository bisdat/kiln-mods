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

An independent Kiln-native storage refill mod. Version 0.2.1 uses the **complete 195-group original INI defaults**, aliases and disabled item/group policies. Bind an empty chest with `echest bind Starter Blocks` or any original group name, using Kiln's console folder in offline play. `echest bind Empty` previews a **one-time, irreversible clear** that requires `echest empty confirm` within 60 seconds. **Read-only `echest nameprobe` now reports whether Kiln can see the `CustomPlayerString` component, but does not decode the name; automatic recognition through Enshrouded's chest Rename UI is still unavailable** in the published Kiln SDK; this port does not claim feature parity or load the original ShroudForge DLL.

