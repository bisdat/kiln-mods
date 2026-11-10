# Survival QoL (Kiln SDK mod)

A configurable, server-authoritative C# mod for Enshrouded, built with **Kiln.Sdk 0.2** and .NET 10. It runs on a dedicated Kiln server or a Kiln-enabled client hosting a world; clients need not install the server-side mod separately.

**Status: v0.2.0 candidate for in-game acceptance.** The Kiln developer added the supported oxygen/body-heat write API in **0.2.0-dev.20**. The integration in this branch has not yet passed real-player underwater and freezing-biome tests. See [issue #2](https://github.com/bisdat/kiln-mods/issues/2).

## Features

| Effect | Implementation | Status |
| --- | --- | --- |
| Infinite stamina | `Rules.Apply` with `MinStamina = 1` and `UnlimitedGliding` | Existing implementation; glider protection requires a Kiln client |
| Infinite mana | `Rules.Apply` with `MinMana = 1` | Existing implementation |
| Infinite underwater breath | On every server Pre tick, refill `GameAttribute.Oxygen` to the player's computed `Max` using `Kiln.Attributes.TrySet` | **New in v0.2.0; in-game verification pending** |
| Infinite frost protection | On every server Pre tick, refill `GameAttribute.BodyHeat` to the player's computed `Max` | **New in v0.2.0; in-game verification pending** |
| Fast health regeneration | `Combat.Heal` on the tick thread; 25 points/s by default | Existing implementation |
| Fast mana regeneration | `Abilities.Attributes.Add` on the tick thread; 35 points/s by default | Existing implementation |
| No fall damage | Cancel `EntityFallEvent` for players | Existing implementation |
| Infinite weapon durability | `Settings.Set("enableDurability", false)` | Existing implementation; applies to **all equipment** |

**Frost is not Shroud:** `FreezeShroudTimer` protects the Shroud timer, not the separate body-heat/frost resource. The new implementation uses `BodyHeat`, not `FreezeShroudTimer`.

The new refills work independently of the health/mana regeneration toggles. They use the game's own current maximums, which may change with player level, equipment or buffs. They affect joined players on the authoritative server world only; they do not alter creatures, saved player maxima, or world-wide diving/body-heat settings. If the attribute service is unavailable, the mod logs a warning once and leaves normal depletion untouched.

## Configuration and upgrade from v0.1.x

The generated `kiln/config/survival-qol.json` is hot-reloadable. Each option is individually configurable:

- `InfiniteUnderwaterBreath` — new, default `true`.
- `InfiniteFrostProtection` — new, default `true`.
- `DoubleUnderwaterBreath` — **deprecated compatibility gate**, default `true`.
- `DoubleFrostResistance` — **deprecated compatibility gate**, default `true`.

The old options **no longer double** a global game setting. To avoid silently enabling features a player had disabled in v0.1.x, their previous `false` values still act as gates. Oxygen protection is enabled only when *both* `InfiniteUnderwaterBreath` and `DoubleUnderwaterBreath` are `true`; frost protection similarly requires *both* `InfiniteFrostProtection` and `DoubleFrostResistance`. To enable protection after having disabled the legacy 2× feature, enable both relevant switches. Keep the old switches for now; they will be removed only with an explicit configuration migration.

Only the durability option still changes a **global** world setting. The mod remembers the previous durability value and restores it when the option is disabled or the mod unloads, provided nobody else has changed that value. It does not call `Settings.Save()`. Neither oxygen nor body heat requires any world-setting restoration: disabling the option simply stops refilling that attribute, and vanilla depletion resumes.

## Build

Requires `git`, the **.NET 10 SDK**, and internet access for the SDK and NuGet dependencies.

From the repository root:

```bash
python3 -m unittest discover -s tests/survival-qol -p 'test_*.py' -v
bash scripts/build.sh
```

The build script pins Survival QoL to Kiln commit `346a3ae5de436db1ba64bbbf0a27f4b8d0ed5be5` (includes dev.21) and locally packages SDK `0.2.0-dev.local.20261110.62101`. The project version is **0.2.0**. The ordinary **Actions → Package Survival QoL** workflow also runs the source-contract tests and builds this pinned SDK.

The [Follow latest Kiln development release](../.github/workflows/kiln-latest.yml) compatibility workflow independently compiles the mod against the latest **official published, checksum-verified** Kiln dev SDK and outputs `survival-qol-0.2.0.zip` with release provenance. Its builds are compatibility evidence, **not** gameplay acceptance. Both workflows build the mod; only the actual game can verify the oxygen and frost UI behaviour.

Do not publish a stable `v0.2.0` tag before issue #2 is accepted. After validation, a matching `v0.2.0` tag will trigger the normal release workflow.

## Install and validate

1. Update the Kiln runtime on the host/server to **0.2.0-dev.20 or newer** (dev.21 recommended); an older runtime does not implement the writable attributes.
2. Download the `survival-qol-0.2.0.zip` build artifact from a successful CI run. Extract the contained `survival-qol/` directory into `kiln/mods/` on the host.
3. Launch Enshrouded and check `kiln/logs/kiln.log` for the mod load message and absence of `Kiln.Attributes unavailable` warnings.
4. Back up your world, then perform the following acceptance tests.

- Dive longer than the normal and 2× limits; oxygen must stay full and the player must not drown. Turn off `InfiniteUnderwaterBreath`, verify normal depletion resumes, then enable it again.
- Enter a freezing biome and remain longer than normal frost resistance allows; body heat must stay full. Disable `InfiniteFrostProtection`, verify frost depletes again, then re-enable it.
- Turn **both** fast-regeneration options off while oxygen/frost remain enabled; both protections must continue working.
- Check the old compatibility gates separately: setting `DoubleUnderwaterBreath=false` or `DoubleFrostResistance=false` must disable that corresponding protection, without changing a global world setting.
- Check existing stamina, mana, regeneration, fall protection and equipment-durability behaviour for regressions.
- Test disconnect/reconnect and hosted-world restarts, and check that disabling/unloading the mod stops both refills while restoring only world settings the mod owns.

A successful build or lab SDK test does **not** establish real-client functionality. In particular, watch for a one-tick UI flicker, unexpected drowning/freezing damage, and mismatched client/server gauges, and report the Kiln version and `kiln.log` if observed.
