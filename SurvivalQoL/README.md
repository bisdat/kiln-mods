# Survival QoL (Kiln SDK mod)

A configurable, server-authoritative C# mod for Enshrouded built with **Kiln.Sdk 0.2** and .NET 10. It runs on a dedicated Kiln server or a Kiln-enabled client hosting an offline/co-op world. It does not need the mod on every connecting client.

## Features

| Requested effect | Implementation | Status |
| --- | --- | --- |
| Infinite Stamina | `Rules.Apply` with `MinStamina = 1` and `UnlimitedGliding` | Implemented; glider-specific protection requires a Kiln client |
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

This retrieves Kiln's **latest published development release**, checks out its exact immutable Git tag in the ignored `.kiln-upstream/`, packs the matching SDK into `.kiln-mdk/nuget/`, updates the shared `global.json` SDK mapping for the build, and invokes `KilnPack`. The build fails instead of falling back to an obsolete release; a sidecar `bin/Release/kiln-sdk-provenance.json` records the exact version and commit.

Alternatively open **Actions → Package Survival QoL**, choose a successful run and download the **survival-qol-package** artifact. The artifact download contains the installable `survival-qol-<version>.zip` made by KilnPack.

## CI packaging and releases

The [packaging workflow](../.github/workflows/build.yml) runs on mod or build-script changes to `main`, for pull requests, and on demand via **Actions → Package Survival QoL → Run workflow**. It builds against the newest published Kiln SDK, compiles the mod, and retains the installable ZIP as a GitHub Actions artifact for 90 days.

To publish a versioned GitHub Release from the current `main` revision, push a tag that matches the `<Version>` in `SurvivalQoL.csproj`:

```bash
git checkout main
git pull --ff-only
git tag v0.1.0
git push origin v0.1.0
```

A `v*` tag triggers the same package build, checks the tag matches the project version, and attaches `survival-qol-0.1.0.zip` directly to the GitHub Release. For subsequent releases, first update the project version and then tag `v<new-version>`. No tag is needed to download packages from successful Actions runs.

## Install and configure

1. Install/update Kiln from [upstream](https://github.com/frostdev-ops/kiln) on your game/host or dedicated server. A newer Kiln runtime may be needed if it predates the SDK used to build the downloaded artifact; check `kiln-sdk-provenance.json`.
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

Informal in-game testing has reported the six fully implemented features working. The actual 2× oxygen and body-heat limits have not been independently measured in the game UI, and dedicated-server and unload/reload behaviour have not received formal validation.
