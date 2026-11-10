# Survival QoL (Kiln SDK mod)

A configurable, server-authoritative C# mod for Enshrouded using Kiln.Sdk 0.2 and .NET 10. It runs on a dedicated Kiln server or a Kiln-enabled client hosting a world; no separate client-side mod is necessary.

**Status: v0.2.0 test candidate.** On 10 November 2026, the player confirmed that the earlier **infinite oxygen** and **infinite frost/body-heat** refills both worked in-game on Kiln dev.21 (issue #2 / PR #22). At the player's request, this branch replaces those always-on infinite switches with **1x–10x duration sliders**. This new proportional-depletion behaviour still requires its own in-game acceptance.

## Features

| Effect | Implementation | Status |
| --- | --- | --- |
| Infinite stamina | `Rules.Apply` with `MinStamina = 1` and `UnlimitedGliding` | Existing; glider protection needs a Kiln client |
| Infinite mana | `Rules.Apply` with `MinMana = 1` | Existing |
| Underwater breath duration | `UnderwaterBreathMultiplier`, 1x–10x integer slider | **New; in-game duration verification pending** |
| Frost resistance duration | `FrostResistanceMultiplier`, 1x–10x integer slider | **New; in-game duration verification pending** |
| Fast health regeneration | `Combat.Heal`, default 25 points/s | Existing |
| Fast mana regeneration | `Abilities.Attributes.Add`, default 35 points/s | Existing |
| No fall damage | Cancel `EntityFallEvent` for players | Existing |
| Infinite equipment durability | `Settings.Set("enableDurability", false)` | Existing; applies to all equipment |

**Frost is not Shroud:** `GameAttribute.BodyHeat` controls frost protection. It is distinct from the Shroud timer.

## How the multipliers work

Two independent Kiln configuration sliders use `[Range(1, 10)]` on integer properties:

- **Underwater Breath Multiplier** — `underwaterBreathMultiplier`, default **10**.
- **Frost Resistance Multiplier** — `frostResistanceMultiplier`, default **10**.

The end points are **1 = normal game depletion, no attribute writes**, and **10 = approximately one tenth of normal depletion (roughly ten times the usual duration)**. All integers between 1 and 10 are supported. The 10x default preserves a strong protective effect after replacing the older infinite option; select 1 to restore the normal game for a resource.

On each authoritative server Pre tick, the mod compares the current oxygen/body-heat attribute with the last **corrected** value. It refunds a fraction `(multiplier - 1) / multiplier` of any loss using the supported `Kiln.Attributes.TrySet` API. Integer remainders are carried across ticks so repeated single-point losses scale correctly, without the rounding error of naive per-tick refills. The game still controls the attribute's maximum, surface/warm-area restoration, equipment and buffs.

This is an **approximation of game duration** calculated from *net observed attribute loss*, not a modification to the engine's native timer or depletion program. Mixed depletion and healing in the same tick, very low tick rates, a maximum change, or unusual status effects may alter the exact resulting duration. The client gauge may briefly show a one-tick correction. Unlike the former infinite implementation, **10x is not infinite**.

The mod keeps separate baseline and remainder state for each player entity and each attribute. It resets state on reconnect, a changed maximum or slider, or whenever natural attribute restoration exceeds consumption. Both sliders run independently of the health/mana regeneration options. Neither changes global diving/body-heat settings.

## Upgrading the configuration

`kiln/config/survival-qol.json` is hot-reloadable through Kiln's config UI. The previous **Infinite Underwater Breath**, **Infinite Frost Protection**, **Double Underwater Breath** and **Double Frost Resistance** boolean settings are all removed from the mod's config schema and should not appear as toggles in Kiln's config screen.

**Kiln deliberately keeps unknown keys in old JSONC config files.** Consequently, when upgrading from v0.1.x or the earlier PR #22 test build, those old values might still be present as **inert unknown keys** in the on-disk JSON file and trigger an "unknown key" warning, even though they no longer appear in the UI or affect gameplay. You can remove those four old properties manually from `survival-qol.json` while preserving any other settings; Kiln will retain/generate the two new sliders. The mod does not rewrite unrelated config content itself. Back up the existing file before editing or resetting it.

The old provisional 2x game-wide `playerDivingTimeFactor` and `playerBodyHeatFactor` overrides have been removed. **Only equipment durability** changes a global world setting; the mod restores its previous value on disabling/unloading when that value is still owned by this mod. Neither oxygen nor frost requires world-setting restoration: setting a slider back to 1x stops attribute writes immediately.

## Build from source

Requires git, .NET 10 SDK and internet access. From the repository root:

```bash
python3 -m unittest discover -s tests/survival-qol -p 'test_*.py' -v
dotnet run --project tests/survival-qol/DepletionMathTests/DepletionMathTests.csproj -c Release
bash scripts/build.sh
```

The normal build pins the Kiln SDK source to commit `346a3ae5de436db1ba64bbbf0a27f4b8d0ed5be5` (dev.21) and locally packages SDK `0.2.0-dev.local.20261110.62101`. The standard [Package Survival QoL workflow](../.github/workflows/build.yml) runs those checks and publishes a ready-to-install `survival-qol-0.2.0.zip` CI artifact.

The [Follow latest Kiln development release workflow](../.github/workflows/kiln-latest.yml) also runs the arithmetic tests and builds against the current officially published, checksum-verified Kiln dev SDK. Compatibility compilation is **not** game acceptance.

Do not publish a stable `v0.2.0` tag until the new slider behaviour has passed the manual checklist.

## Install and validation

1. Use **Kiln 0.2.0-dev.20 or newer** on the host/server (dev.21 recommended). The writable oxygen and body-heat attributes did not exist in older SDK runtimes.
2. Download `survival-qol-0.2.0.zip` from a successful workflow run for **this updated PR revision**. Extract its `survival-qol/` directory to `kiln/mods/` on the host.
3. Open the Kiln Configs screen and confirm only the **two new multiplier sliders** appear (not the old four toggles).
4. In a backed-up test world, measure oxygen depletion and frost/body-heat depletion at **1x**, **2x**, **5x** and **10x**, ideally comparing the time required to lose a fixed quantity of the attribute at the same location/equipment.
5. At **1x**, confirm both resources deplete at vanilla rates and no artificial refill occurs. At **10x**, confirm gradual depletion remains (not infinite) and lasts roughly ten times as long.
6. With both health and mana regeneration disabled, confirm both multipliers still work.
7. Change sliders live and reconnect; verify no stale accumulation, no unexpected instantaneous refill, and both gauge values remain consistent.
8. Check stamina, mana, health regeneration, fall protection and equipment durability for regressions.

The earlier **infinite refill** tests are accepted; they do not prove the new **duration multipliers**. Report the selected multiplier, normal and modified times, Kiln version, and `kiln/logs/kiln.log` if anything behaves unexpectedly.
