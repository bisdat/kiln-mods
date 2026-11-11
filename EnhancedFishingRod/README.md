# Enhanced Fishing Rod (Kiln)

A standalone, data-only Kiln mod that doubles the fishing strength and endurance of the **Excellent Fishing Rod** (`Tool_T6_FishingRod_epic`).

| Attribute | Unmodified | Modded |
| --- | ---: | ---: |
| Fishing Strength (underlying value) | 2.5 | 5.0 |
| Fishing Endurance | 55 | 110 |

The game normally rounds the rod's strength to +3 in its item tooltip. This mod sets the underlying value to 5.0. Fishing Strength governs how quickly fish tire; Fishing Endurance governs how long you can fight. The mod does **not** alter the Lucky Fishing Rod, treasure and Wet Boot multipliers, crafting recipes, item rarity or item IDs.

## Install

1. Download the `enhanced-fishing-rod-0.1.0.zip` artifact from the **Package Enhanced Fishing Rod** GitHub Actions run, or the equivalent release asset after acceptance.
2. Import the ZIP in Kiln, or unzip it so the files are in `Enshrouded/kiln/mods/enhanced-fishing-rod/kiln.mod.json` and `Enshrouded/kiln/mods/enhanced-fishing-rod/content/rod.json`.
3. Use Kiln **0.2.0-dev.22 or newer** (manifest-only/data-only mod support), enable the mod and launch a hosted or solo world.
4. Equip an **existing** Excellent Fishing Rod. Check the tooltip and catch a few fish. Expected fishing stats are strength 5 and endurance 110. If the tooltip remains unchanged, inspect `kiln/logs/kiln.log` and `kiln/logs/mods.txt` for pack errors; client-side Kiln content sync/UI may also be relevant.
5. Disable the mod and relaunch to restore vanilla stats.

## Implementation and verification

The only game-data rules are `impactValues.FishingStrength` and `impactValues.FishingEndurance` under `items.Tool_T6_FishingRod_epic`. These paths are verified against the upstream Kiln item-block compiler and fishing-rod test (`frostdev-ops/kiln`). No DLL, native hooks, generated items, crafting changes or persistent save modifications.

Static package tests run on every PR, but **in-game loading, displayed stats and actual fishing behaviour remain to be tested**. If a mod uses the same fields, Kiln's last-applied layer wins, and it logs conflicts.

### Packaging

From the repository root:

```bash
python3 -m unittest discover -s EnhancedFishingRod/tests -v
python3 EnhancedFishingRod/package.py
```

Package output: `dist/enhanced-fishing-rod-0.1.0.zip`. Uses Python 3's standard library only.

### Development references

- [Kiln data-pack schema](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/modding/schemas/content-pack.md)
- [Kiln content service](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/content.md)
- [Fishing-rod test](https://github.com/frostdev-ops/kiln/blob/main/mods/tests/blocks-more-test/Tools.cs)
