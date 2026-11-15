# Survival QoL (Kiln SDK mod)

An Enshrouded mod built with the Kiln SDK and .NET 10. Runs on the authoritative server world, including Kiln-hosted single-player; no client-side installation is required for hosted server logic.

**Current candidate: v0.2.2 — new stamina recovery multiplier, awaiting in-game acceptance.** The prior **v0.2.1** release and its oxygen/frost multipliers were confirmed in-game and accepted, including the game clock counting down more slowly.

## Features

| Option | Implementation | Default |
| --- | --- | --- |
| Infinite Stamina | Kiln Rules `MinStamina = 1`, `UnlimitedGliding` | On |
| **Stamina Regen Multiplier (NEW)** | `StaminaRegenMultiplier` 1×–10× | **1× (normal game)** |
| Infinite Mana | Kiln Rules `MinMana = 1` | On |
| Underwater Breath Multiplier | `UnderwaterBreathMultiplier` 1×–10× | 10× |
| Frost Resistance Multiplier | `FrostResistanceMultiplier` 1×–10× | 10× |
| Fast Health Regen | `Combat.Heal` while injured | On, +25 points/s |
| Fast Mana Regen | `Abilities.Attributes.Add` | On, +35 points/s |
| No Fall Damage | Cancel player `EntityFallEvent` | On |
| Infinite Weapon Durability | Temporarily disable equipment durability globally | On |

`BodyHeat` provides frost protection, **not** the Shroud timer.

## Stamina regeneration — new in v0.2.2

The Kiln configuration adds **Stamina Regen Multiplier**, a `[Range(1, 10)]` integer slider, default **1×**. It is **independent of Infinite Stamina**:

- **Infinite Stamina enabled:** Kiln Rules already holds stamina full; the regen multiplier does nothing.
- **Infinite Stamina disabled, multiplier 1×:** Vanilla stamina recovery; no stamina writes by this feature.
- **Infinite Stamina disabled, multiplier 2×–10×:** The mod observes the game's positive stamina recovery between authoritative server ticks and refunds `(multiplier − 1) × observed recovered points`, capped by the game's current stamina maximum.

The native game controls *when* stamina regenerates. The multiplier does **not** introduce recovery during the game's recharge delay, prevent stamina use, override the current stamina maximum, or use a fixed points-per-second bonus. Buffs, equipment and skill bonuses already included in observed natural recovery are respected. The sampled recovery rate is approximate rather than a precise engine-native recharge-rate setting.

**API limitation:** Kiln's supported attribute snapshot shows the *net* stamina change, not its cause. A potion, ability or other mod causing a positive stamina jump can also be multiplied. If spending and regeneration occur in the same step, their net change can understate real natural regeneration. This cannot be reliably distinguished through the current supported API.

Tracking state is isolated by player identity. Reconnects, changed attribute maximum, changed multiplier, changing Infinite Stamina, and leaving the server reset the baseline. Calculations use 64-bit intermediate arithmetic and clamp to the maximum, and do not generate extra stamina without a positive observed recovery. All writes use the supported `Kiln.Attributes.Get/TrySet` on the server tick thread.

## Oxygen and frost duration — accepted in v0.2.1

Independent 1×–10× `UnderwaterBreathMultiplier` and `FrostResistanceMultiplier` sliders (both default 10×) use the supported `GameAttribute.Oxygen` and `GameAttribute.BodyHeat` attributes. At 1× the mod does not modify the resource. At 10× the mod refunds roughly nine-tenths of observed depletion. The game's maxima and nominal timer start values are unchanged.

**In-game acceptance:** The player confirmed the existing countdown slows down rather than starting with more time, and accepted the practically equivalent increased duration. This is based on the game's net observed depletion and is not a modification of the native timer. Mixed depletion/restoration in a single tick can affect the measured ratio.

The legacy oxygen/frost `Double...` and `Infinite...` toggles were removed in v0.2.1. Kiln may preserve their old JSONC values on disk as inert unknown keys. Remove obsolete keys manually if warning-free configuration files are desired; do not overwrite unrelated settings.

## Installation and testing

1. Build or obtain **`survival-qol-0.2.2.zip`** from the [Package Survival QoL GitHub Action](../.github/workflows/build.yml). The package must be installed into Kiln Library and **version 0.2.2 selected in the active profile**. Reactivate/apply the profile and restart Enshrouded. For legacy drop-in installs only, unpack into `kiln/mods/`.
2. In Kiln Configs verify the new **Stamina Regen Multiplier** is 1–10, initially 1, and that the **Infinite Stamina** toggle remains available.
3. Disable **Infinite Stamina**, spend stamina, and compare natural recovery at **1×, 2×, 5× and 10×** with the same character and equipment. At 1× recovery and its delay should remain vanilla; at 10× recovery should be substantially faster while the game's recharge delay remains.
4. Re-enable **Infinite Stamina** and check it still keeps stamina full regardless of the multiplier. Change the slider during play and reconnect to ensure no stale bonus is applied.
5. Check oxygen/frost multipliers, stamina spending, health/mana regen, fall protection and durability for regressions; report any failures with the active config values and `kiln/logs/kiln.log`.

Kiln's active profile can pin an older mod even after a new ZIP is imported. `kiln/state/mods.json` identifies the version actually loaded. In profiles with `dropins: false`, copying into `kiln/mods/` alone does not activate it. **Do not tag/release stable v0.2.2 before in-game acceptance.**

## Building and verification

Requires git, .NET 10 and internet access:

```bash
python3 -m unittest discover -s tests/survival-qol -p 'test_*.py' -v
dotnet run --project tests/survival-qol/DepletionMathTests/DepletionMathTests.csproj -c Release
bash scripts/build.sh
```

The pinned build uses Kiln dev.21-capable SDK source `346a3ae5de436db1ba64bbbf0a27f4b8d0ed5be5` and local SDK package `0.2.0-dev.local.20261110.62101`. The separate [follow-latest-Kiln compatibility workflow](../.github/workflows/kiln-latest.yml) validates against a checksum-verified published Kiln development SDK. CI arithmetic and compilation success cannot substitute for an actual Enshrouded gameplay test.
