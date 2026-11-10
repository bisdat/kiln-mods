# Flame Altar Expanded — 0.1.3 (bounded level-4 diagnostic)

> **EXPERIMENTAL / NOT GAMEPLAY-ACCEPTED.** This retains the original `flame-altar-expanded` EML mod ID. The previous **0.1.2** version wrote 300m to level 4 but the actual game displayed **44 × 44 × 44 m** at Riverreach 1's level-4 Flame Altar. Since `300 % 256 = 44`, a downstream 8-bit conversion is suspected but not yet proven. The BalancingTable field itself is described as **float3**, so it would be inaccurate to claim that EML's stored field is inherently 8-bit. This version deliberately probes a value **below 256**. The old separate-ID level-4 test that previously blocked startup is withdrawn.

## What 0.1.3 changes

- The **existing altar level 4** BalancingTable size becomes **240 × 240 × 240 m**, instead of vanilla 160 × 160 × 160 m. The intended original goal remains **300 m**; this 240 m intermediate is an **engineering diagnostic only**.
- All other altar levels (including 5), upgrade recipes, costs, and `altarLevelMax` are **untouched**.
- Only `keen::BalancingTable` is requested. Exactly one resource, a six-entry size array, vanilla 40/80/120/160/160 tier sizes and `altarLevelMax=5` are required before writing.
- The script has a separate guard limiting this diagnostic to 161..255m. Unknown layouts or a conflicting earlier patch fail before resource publication.
- No fifth altar level is offered and **Upgrade Altar** at level 4 may remain disabled.

## Safe controlled test on official Kiln dev.21+

1. Exit Enshrouded and ensure **0.1.2**, the old **0.1.1**, and the withdrawn separately identified `flame-altar-level4-range-test` are not enabled. Only **Flame Altar Expanded 0.1.3** may be active. Use the Kiln UI to disable old versions; do not edit KFC files or EML caches yourself.
2. If 0.1.2 was active, start with **no active EML mods**, launch only to the title screen, exit, and check that Kiln restored the vanilla game data (the EML state can show `restored=true`).
3. Import the directly installable `flame-altar-expanded-0.1.3.zip` in Kiln, confirming the **existing original mod identity** `flame-altar-expanded` at version **0.1.3**. This must replace, not coexist with, any older version.
4. Start Enshrouded to the main menu. Confirm both Kiln and game launch. For an uncached patch, EML should log `flame-altar-expanded 0.1.3: level-4 build-zone 160x160x160 -> 240x240x240 m`, with `phase=done`, **`patched=1`**, `error=null`. The mod's patch can be cached on later runs, when `patched=0` is expected.
5. In a **disposable or backed-up test world**, inspect a level-4 altar's menu. Capture a screenshot showing the Building Area. The expected diagnostic value is **240 × 240 × 240 m**. If it instead shows 160, 44 or another unexpected size, stop and capture EML logs. Do **not** save/build in altered bounds.
6. **Only if the menu shows 240m**, test in that disposable world whether a block can be placed at ~100m horizontally from the altar's centre (beyond the vanilla 80m half-width, inside the proposed 120m half-width), outside any other altar zone. Report separately whether the build cursor permits it, placement succeeds, and the block survives a save/restart **while the mod remains active**.
7. Exit the game. Disable 0.1.3 in Kiln, start once with no EML mods to restore vanilla, and **discard/restore the test world** rather than loading new out-of-vanilla builds without the patch. Never delete `.kfc`, `.kfc.bak` or `.dat` files manually.

Capture diagnostics: `kiln/state/eml.json`, `kiln/logs/eml.log`, and `kiln/logs/kiln.log` from the same run. If startup fails, also include `kiln/state/boot.json` and `kiln/logs/proxy.log`.

## Interpretation

- **UI reads 240m:** supports a conversion threshold at 256, but does not prove that the game **authoritatively** allows 240m, nor that 300m is representable via the same path.
- **UI and construction reach 240m:** a limited range increase works. Decide whether the compromise is acceptable or if engine/UI changes are needed for 300m.
- **UI remains 44m or otherwise incorrect:** investigate numeric conversions, stale patched data, and actual server world-zone evaluation before another trial.

The prior 0.1.2 **gameplay FAIL** has been recorded on [draft PR #13](https://github.com/bisdat/kiln-mods/pull/13) and [issue #12](https://github.com/bisdat/kiln-mods/issues/12). The original level-5 recipe investigation is historical research ([ENGINE-INVESTIGATION.md](ENGINE-INVESTIGATION.md)), not an active part of this test.

## Development

Source `src/mod.lua`; manifest `mod.json`; tests `tests/test_patch.lua`; CI `.github/workflows/flame-altar-expanded.yml`. Run `luac5.4 -p src/mod.lua` and `lua5.4 tests/test_patch.lua`. Tests are offline mock verification, **not in-game validation**. MIT source code; Kiln's GPL EML runtime is not redistributed.
