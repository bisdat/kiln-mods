# Survival QoL (Kiln SDK mod)

A configurable, server-authoritative C# mod for Enshrouded built with **Kiln.Sdk 0.2** and .NET 10. It runs on a dedicated Kiln server or a Kiln-enabled client hosting an offline/co-op world. It does not need the mod on every connecting client.

## Features

| Requested effect | Implementation | Status |
| --- | --- | --- |
| Infinite Stamina | `Rules.Apply` with `MinStamina = 1` | Implemented |
| Infinite Mana | `Rules.Apply` with `MinMana = 1` | Implemented |
| Infinite Underwater Breath | `playerDivingTimeFactor = 2.0` (maximum supported) | **Only 2× breath, NOT infinite** |
| Infinite Frost Timer | `playerBodyHeatFactor = 2.0` (maximum supported) | **Only 2× body heat, NOT infinite** |
| Fast Health Regen | `Combat.Heal` on the tick thread, 25 points/s by default | Implemented |
| Fast Mana Regen | `Abilities.Attributes.Add` on the tick thread, 35 points/s by default | Implemented |
| No Fall Damage | Cancel `EntityFallEvent` for players | Implemented |
| Infinite Weapon Durability | `Settings.Set("enableDurability", false)` | Implemented; **all equipment** durability is disabled |

The SDK currently has **no supported oxygen refill / depletion-cancellation API** and **no supported frost-depletion hook**. The two 2× options are explicit, temporary approximations rather than fake 'infinite' implementations. In particular, **Frost is not the Shroud timer**: `FreezeShroudTimer` does not grant frost protection.

The durability, diving and body-heat options change **global world settings**, so they affect all players. For each setting, the mod records the prior value, changes it only if necessary, and restores it on disabling/unloading only if no one has changed it in the meantime. It does **not** call `Settings.Save()`.

## Build from source

Requirements: `git`, the **.NET 10 SDK**, internet access (GitHub / NuGet), Linux or WSL.

From the repository root:

```bash
bash scripts/build.sh
```

This clones upstream Kiln into the ignored `.kiln-upstream/`, checks out commit `9b9adada835c5f0a68b36567f939dc645cc1ef94`, packages its SDK and services into the ignored `.kiln-mdk/nuget/`, builds this project and invokes `KilnPack`. The SDK package version is pinned to `0.2.0-dev.local.20261107.62100`.

Alternatively download the **survival-qol** artifact from the GitHub Actions build; it contains the ZIP produced by KilnPack.

## Install and configure

1. Install/update Kiln from [upstream](https://github.com/frostdev-ops/kiln) on your game/host or dedicated server. A newer Kiln runtime may be needed if it predates the pinned SDK's APIs.
2. Extract `survival-qol-0.1.0.zip` into the game/server's `kiln/mods/` folder. The archive contains a `survival-qol/` directory with the generated `kiln.mod.json` and compiled DLL.
3. Launch your game/server with Kiln enabled. Check `kiln/logs/kiln.log` for the mod load message.
4. Edit `kiln/config/survival-qol.json` to enable/disable individual effects and set regeneration rates. The config is created at first load and changes are hot-reloaded.

Defaults turn on the six supported effects and the two *2× extensions*. Disable either extension if you would rather preserve the game's normal oxygen/body-heat limits.

## Validation checklist

Build success does **not** prove functionality in Enshrouded. Test in a backup or separate world before using an existing save:

- Sprint, dodge and repeatedly cast spells; confirm stamina and mana refill and remain usable. Note that the client HUD may briefly predict a spend before server correction.
- Take non-lethal damage and observe quick health recovery; spend mana with the infinite-mana toggle off to check its separate regeneration.
- Fall far enough to normally take damage; confirm no health loss or hard-landing stagger.
- Use a weapon repeatedly; check that durability is no longer spent (the setting also applies to other equipment).
- Dive and enter a freezing environment: verify **approximately doubled** limits, not infinity.
- Edit the configuration live, then unload the mod; verify the world's normal durability/diving/heat settings return.

No real-client or dedicated-server validation has been performed by this repository yet.
