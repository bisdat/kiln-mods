# Flame Altar Expanded — why level 5 is absent (10 November 2026)

**Status: blocked on native altar-upgrade UI and progression integration. No gameplay-safe fix is proven.**

## Confirmed client evidence

- Official Kiln `v0.2.0-dev.21` under Proton now starts with Flame Altar Expanded EML `0.1.1` enabled, unlike older Kiln builds.
- On the controlled no-mod startup, EML restored the vanilla KFC; on the immediately following fresh enabled startup, `src/mod.lua` ran, printed `registered FlameAltar_HomeBase_Upgrade_4 (10 Shroud Nexus); level-5 build zone = 300 x 300 x 300 m`, and EML reported `Applied 2 patches`. State: `phase=done`, `patched=2`, `cached=false`, `error=null`; this is more than an EML cache hit.
- The user's **Riverreach 1** screenshot of a loaded world after the repatch shows **ALTAR LEVEL 4**, **BUILDING AREA 160 m × 160 m × 160 m**, and **Upgrade Altar greyed out**. Thus the actual upgrade UI is not offering level 5. This is not an ordinary missing craftable-item recipe.
- The screenshot does not show a fifth-tier build zone, because the altar **has not changed to level 5**. In particular, it does not establish that the level-5 `buildzoneSizesPerAltarLevel` data failed to patch.

## Sources and relevant engine facts

1. [Official Enshrouded Wiki: Flame Altar](https://enshrouded.wiki.gg/wiki/Flame_Altar) lists area upgrades only through level 4, with 160 m on each axis.
2. [Game-data-derived altar planner](https://pindrop.gg/enshrouded/flame-altar) identifies vanilla `FlameAltar_HomeBase_Upgrade_0_Claim` through `_3`; there is no level-5 recipe. The BalancingTable already includes `altarLevelMax=5`, but the fifth zone defaults to 160 m.
3. [Independent open reverse-engineering brief](https://github.com/HorizonRepublic/enshrouded-mods/blob/bc777ef422d2b7e55ec9261dedd1b1c67ef639dd/BRIEF.md) observes that `upgrade_building_range` writes the altar trigger size from its *current* level every tick; thus changing the level-5 table entry cannot change a level-4 altar's existing size by itself.
4. [Kiln GameUi enum](https://github.com/frostdev-ops/kiln/blob/v0.2.0-dev.21/managed/services/Kiln.Ui/GameUi.cs) exposes distinct `FlameAltar` (12) and `AltarUpgrade` (13) HUD screens. Do not conflate the dedicated altar-upgrade UI with the regular crafting menu.
5. [Kiln reverse-engineering: gameplay](https://github.com/frostdev-ops/kiln/blob/v0.2.0-dev.21/docs/re/gameplay.md) documents `CraftRecipeEvent` and `RecipeFind`; the two altar workshops require `CanExtendBase`. [Content analysis](https://github.com/frostdev-ops/kiln/blob/v0.2.0-dev.21/docs/re/content.md) describes normal recipe registry/index and `knowledgeRequirement` and separately how ordinary crafting menu items can be hidden by missing UI category assignment. **Neither document proves that the dedicated AltarUpgrade screen uses the ordinary crafting-menu category list.**
6. The mod's current source copies an existing output-less level-4 recipe, assigns a new ID/GUID/name/ingredient count, and changes only the level-5 BalancingTable zone. It does not inspect or modify the dedicated AltarUpgrade screen's level eligibility, selection or UI configuration. The role of `RecipeInfo.knowledgeRequirement` and any other progression gate also remains unverified for this menu.

## Investigation order (evidence before code)

1. **Read-only verify actual patched KFC values:** inspect the final `RecipeRegistryResource` to confirm `FlameAltar_HomeBase_Upgrade_4` is present with correct workshop, ID, input, knowledge fields and length (and the expected BalancingTable sixth entry). A successful Lua print proves the input resource was mutated; it does not establish that the game consumed the recipe correctly.
2. **Find the client `AltarUpgrade` screen's availability decision:** trace what distinguishes level 4 from levels 1–3. Determine whether it consults a maximum tier, an explicit recipe-id list, a special recipe lookup, player/altar authority and/or `knowledgeRequirement`. Avoid assuming generic crafting `FbUiBundle` categories govern this screen.
3. **Trace server altar-upgrade processing:** confirm how recipe payment maps to raising the altar entity's level and persisted `BaseIdComponent` build zone; verify client/server agreement and save-zone persistence. No raw change to the altar level as a workaround without this mapping and rollback.
4. Once a precise missing link is established, implement the *minimum* version-gated correction, add offline tests, and test on a disposable/backed-up world. If a native runtime hook is required, separate its permissions and version constraints from the existing GPL EML patch.
5. Alternatively, setting level 4's 160 m size to 300 m could meet a **different** range-only goal, but removes the new level and 10 Shroud Nexus cost and changes every level-4 altar. **Do not silently substitute this**, because it does not satisfy issue #12's acceptance criteria.

## Hold

PR #13 remains **draft**, EML `0.1.1` is **not a functional mod**, release artifacts remain quarantined, and no valuable-save building/terraforming may proceed outside the original 160 m cube. No additional manual launch/repatch loop is justified without a targeted diagnostic change.
