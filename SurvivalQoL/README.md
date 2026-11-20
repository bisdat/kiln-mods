# Survival QoL (Kiln SDK mod)

An Enshrouded mod built with the Kiln SDK and .NET 10. Runs on the authoritative server world, including Kiln-hosted single-player; no client-side installation is required for hosted server logic.

**Current test candidate: v0.2.4 — experimental staff-spell charge-time slider. NOT yet verified in-game.** v0.2.3 set the default health and mana bonus regeneration to zero (normal game); v0.2.2 introduced stamina regeneration and v0.2.1 introduced oxygen/frost multipliers.

## Features

| Option | Implementation | Default |
| --- | --- | --- |
| Infinite Stamina | Kiln Rules `MinStamina = 1`, `UnlimitedGliding` | On |
| Stamina Regen Multiplier | `StaminaRegenMultiplier` 1×–10× | **1× (normal game)** |
| Staff Casting Speed Multiplier (experimental) | `StaffCastingSpeedMultiplier` 1×–10×; changes spell ammo Charge Time config | **1× (normal game)** |
| Infinite Mana | Kiln Rules `MinMana = 1` | On |
| Underwater Breath Multiplier | `UnderwaterBreathMultiplier` 1×–10× | 10× |
| Frost Resistance Multiplier | `FrostResistanceMultiplier` 1×–10× | 10× |
| Fast Health Regen / Health Regen Per Second | Optional `Combat.Heal` bonus while injured | On, **0 extra HP/s** (normal game) |
| Fast Mana Regen / Mana Regen Per Second | Optional `Abilities.Attributes.Add` bonus | On, **0 extra mana/s** (normal game) |
| No Fall Damage | Cancel player `EntityFallEvent` | On |
| Infinite Weapon Durability | Temporarily disable equipment durability globally | On |

`BodyHeat` provides frost protection, **not** the Shroud timer.

## Staff casting-speed experiment — v0.2.4

The new **Staff Casting Speed Multiplier** slider ranges from **1× to 10×**, defaults to **1×**, and is independent of Infinite Mana or mana regeneration. It uses Kiln's `Content.ItemData` / `kiln.gamedata` API to identify spell-ammunition entries named `Ammo_*_Spell_*` with the existing `Charge Time` config id `0x579b2657`. The source data for Fireball I contains a 2.5-second charge time. For 2× the proposed value is 1.25 s; for 5× it is 0.5 s; for 10× it is 0.25 s.

**This is a research prototype, not a proven cast-speed feature.** The Kiln developers have identified the `Charge Time` config in spell items, but its precise engine runtime reader is not yet confirmed. It may alter the tooltip without reducing the actual held-input or repeat-cast delay. Staff spells also have timed actor sequences (including animation, release events, loop/keep-alive and action windows). We deliberately **do not** retime those events without measured evidence; otherwise the casting animation, mana payment, projectile spawn and cancel windows can become inconsistent. Wand attacks are not modified.

The multiplier always uses the **original** charge time and does not compound edits when the setting changes. Returning to **1×** removes this mod's own content layer. Kiln automatically reverts its layer on unload. All changes are session-only and need the Kiln `Content` game-data service, available in dev.29.21. The mod warns when no matching spells are found; the existing QoL features continue working.

**Required in-game acceptance (do not merge/tag before PASS):**

1. In the active profile install `survival-qol-0.2.4.zip` and verify that version is loaded. At **1×**, compare Fireball or Ice Bolt charge and repeated-cast timing with the mod disabled.
2. Try **2×**, **5×**, and **10×** with the same staff, character, spell and equipment. Measure *time held until the spell is ready*, *release-to-projectile timing*, and *time until a second cast is possible*. Verify speed improves rather than merely the tooltip.
3. Test a different spell (including an Eternal spell if available), then a wand. Wands must behave exactly as before. Repeat with/without Quick Charge if available.
4. Change from **10× back to 1×** without restarting, check the full original delay and charges return, then unload/reload the mod and repeat.
5. Check that mana charges, visuals, animation, ability cancellations, and other QoL sliders still work. Collect `kiln/logs/kiln.log` (search `Staff casting-speed`) and note any spell whose observed timing did not change.

## Health and mana regeneration — default changes in v0.2.3

Both additional-regeneration sliders use **0–100 points/s**. Their default is now **0**: this means *no bonus* on top of normal Enshrouded regeneration, not a request to suppress native regeneration. The corresponding Fast Health Regen and Fast Mana Regen switches remain enabled by default; with a zero bonus the mod does not query or write health/mana through these features. Values above zero add a fixed rate while below maximum (health also requires a living, injured character).

For players who want the former boosted settings, choose **25 HP/s** and **35 mana/s**, respectively. The mod does not change Enshrouded's built-in recovery formulas or expose a native health/mana recovery multiplier.

**Existing installations:** Kiln preserves stored configuration values on upgrade. If your active profile already has `healthRegenPerSecond: 25` or `manaRegenPerSecond: 35`, those will remain in effect after installing v0.2.3. Set each slider to **0** in the Kiln Configs UI (or edit the active profile's `survival-qol.json`) to select the normal-game baseline. No automatic reset or migration overwrites previously saved preferences.

## Stamina regeneration — introduced in v0.2.2

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

1. Build or obtain **`survival-qol-0.2.4.zip`** from the [Package Survival QoL GitHub Action](../.github/workflows/build.yml). The package must be installed into Kiln Library and **version 0.2.4 selected in the active profile**. Reactivate/apply the profile and restart Enshrouded. For legacy drop-in installs only, unpack into `kiln/mods/`.
2. In Kiln Configs, set **Health Regen Per Second** and **Mana Regen Per Second** to 0 (particularly when upgrading a profile previously set to 25/35); also verify **Stamina Regen Multiplier** remains 1–10, initially 1, independently of the **Infinite Stamina** toggle.
3. Disable **Infinite Stamina**, spend stamina, and compare natural recovery at **1×, 2×, 5× and 10×** with the same character and equipment. At 1× recovery and its delay should remain vanilla; at 10× recovery should be substantially faster while the game's recharge delay remains.
4. Re-enable **Infinite Stamina** and check it still keeps stamina full regardless of the multiplier. Change the slider during play and reconnect to ensure no stale bonus is applied.
5. With both health/mana bonuses at 0, verify the game regenerates normally with your existing equipment and effects; raise each bonus to 25/35 to verify it still adds healing/mana, then return to 0. Check oxygen/frost, fall protection and durability for regressions; report any failures with active config values and `kiln/logs/kiln.log`.

Kiln's active profile can pin an older mod even after a new ZIP is imported. `kiln/state/mods.json` identifies the version actually loaded. In profiles with `dropins: false`, copying into `kiln/mods/` alone does not activate it. **Do not tag/release v0.2.4 before staff-casting gameplay acceptance.**

## Building and verification

Requires git, .NET 10 and internet access:

```bash
python3 -m unittest discover -s tests/survival-qol -p 'test_*.py' -v
dotnet run --project tests/survival-qol/DepletionMathTests/DepletionMathTests.csproj -c Release
bash scripts/build.sh
```

The pinned build now uses official Kiln dev.29.21 release source `b1c5b3a0cd77017c9d6baafb2394c7968efc24ef` and SDK package `0.2.0-dev.29.21` (needed for `kiln.gamedata`). The separate [follow-latest-Kiln compatibility workflow](../.github/workflows/kiln-latest.yml) validates against a checksum-verified published Kiln development SDK. CI arithmetic and compilation success cannot substitute for an actual Enshrouded gameplay test.
