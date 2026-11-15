# Endless Chests — final 27 catalogue identities (0.5.2)

The preceding [0.5.1 reconciliation](ENDLESS-CHESTS-CATALOGUE-RECONCILIATION.md) left 27 original group positions unresolved; this phase resolves **all 27** without changing 195 group names or 3,464 original membership positions.

**Sources:** the pinned Kiln item registry at `frostdev-ops/kiln@eba9a5e1e0e0044a9146fdd53021a751e2957030`, verified identical (all 4,262 records) to the official released Kiln `v0.2.0-dev.28` commit `2301cc5fd3138aa91119e8bc0f082569218315cd`; full provenance in [`scripts/data/endless-chests-identity-audit-0.5.2.json`](../../scripts/data/endless-chests-identity-audit-0.5.2.json).

## The decisions

- **Backpacks:** select `Backpack_T1_NEW`, `Backpack_T2_NEW`, and `Backpack_T3_NEW`, the current craftable 16/24/32-slot equipment. The older `Backpack_T1/T2/T3` versions have no 1.0 crafting recipes and have different capacity stats, despite the same display labels. Supporting [independent game-file analysis](https://pindrop.gg/enshrouded/backpacks) and the [official wiki](https://enshrouded.wiki.gg/wiki/Backpacks).
- **Decorations/books:** when the SDK contains genuinely different models using an identical display name, represent the original group position as `{"variants": [idA, idB, ...]}` in catalogue schema v2. The runtime expands this to separately checked items in the same group, not a lowest-ID guess. **11 multi-variant positions, 39 explicit ID references**. Real IDs already available elsewhere in that group are not duplicated.
- **Farmbeds, lanterns and prison fences:** select the missing decorative variant, the non-underscored wall lantern and the ordinary fence rather than production or fixed/static placeholders.
- **Fern Pot:** the three size-qualified Fern items already exist next to this original entry. The missing unsized Fern model has debug identity `Empty_Flower_Pot_CNY_Fern_01` (ID `673405648`, displayed in-game as `Fern`). Needs an in-game visual confirmation of the exact pot model.
- **Resource Bundle I:** this was never absent. Its actual SDK display label is `Resource  Bundle I` (**two spaces**), ID `1313835688`, debug `Prop_Decoration_T1_RessourceBundle_01`. Verified against [current game item list](https://enshroudedsaveeditor.com/items) and [official wiki craft recipe](https://enshrouded.wiki.gg/wiki/Resource_Bundle_I). Never substitute Resource Bundle II.
- **Behaviour:** no display-name guessing, and no numeric-ID reassignments to the existing 3,437 verified slots. The only C# change is support for schema-v2 explicit variant arrays. Individual item verification and refill/rename semantics remain unchanged.

## Full resolution ledger

| Group | Former unresolved name | Resolved new ID(s) and debug identity | Rationale |
|---|---|---|---|
| Backpacks | Small Backpack | `3728213390` (`Backpack_T1_NEW`) | current craftable backpack |
| Backpacks | Medium Backpack | `3773368910` (`Backpack_T2_NEW`) | current craftable backpack |
| Backpacks | Large Backpack | `996296787` (`Backpack_T3_NEW`) | current craftable backpack |
| Books | Book I | `2975370828` (`Prop_Decoration_Book_01`); `3853710961` (`Prop_Decoration_Book_02`); `1042567701` (`Prop_Decoration_Book_03`); `676824530` (`Prop_Decoration_Book_04`); `1665817031` (`Prop_Decoration_Book_06`); `743954578` (`Prop_Decoration_Book_07`); `2283198170` (`Prop_Decoration_Book_08`); `1920341845` (`Prop_Decoration_Book_10`); `3330526371` (`Prop_Decoration_Book_12`); `2548133338` (`Prop_Decoration_Book_13`); `413542990` (`Prop_Decoration_Book_14`); `589587638` (`Prop_Decoration_Book_16`); `4242562036` (`Prop_Decoration_Book_17`); `1878602124` (`Prop_Decoration_Book_18`) | all distinct registered forms |
| Books | Collection Of Books I | `3259443869` (`Prop_Decoration_Book_Pile_01_A`); `1276880412` (`Prop_Decoration_Book_Pile_02_B`) | all distinct registered forms |
| Books | Collection Of Books II | `147747455` (`Prop_Decoration_Book_Pile_03_C`); `1732682352` (`Prop_Decoration_Book_Pile_04_D`) | all distinct registered forms |
| Books | Collection Of Books III | `186386614` (`Prop_Decoration_Book_Pile_05_E`); `523691097` (`Prop_Decoration_Book_Pile_06_F`) | all distinct registered forms |
| Decorations 2 | Bamboo Stairs | `1517467835` (`Prop_Decoration_T6_Lizard_Stairs_2m_01_A`) | all distinct registered forms |
| Decorations 2 | Conifer Crate | `553841242` (`Prop_Decoration_T5_Crate_Conifer02`) | all distinct registered forms |
| Decorations 3 | Empty Cymbidium Pot | `2125538426` (`Empty_Flower_Pot_CNY_Empty_02b`) | all distinct registered forms |
| Decorations 3 | Empty Farmbed | `604994553` (`Prop_Decoration_Farmbed_01c`) | decoration not production |
| Decorations 3 | Empty Lily Pot | `2328440963` (`Empty_Flower_Pot_CNY_Empty_01a`) | all distinct registered forms |
| Decorations 4 | Large Bamboo Basket | `1506922480` (`Prop_Decoration_T6_Basket_Large_02`) | all distinct registered forms |
| Decorations 5 | Simple Wood Support | `2242544273` (`Prop_Decoration_T6_Simple_Support_4m_02_A`) | all distinct registered forms |
| Decorations 6 | Veilwater Basin Boardwalk I | `33507171` (`Prop_Decoration_T6_Boardwalk_8m_01_A`) | all distinct registered forms |
| Decorations 6 | Veilwater Basin Boardwalk II | `1195149031` (`Prop_Decoration_T6_Simple_Boardwalk_4m_01_A`) | all distinct registered forms |
| Dividers | Bamboo Room Divider I | `371722890` (`Prop_Decoration_T6_Lizard_Gate_01`); `260723984` (`Prop_Decoration_T6_Lizard_Gate_02`); `3413337702` (`Prop_Decoration_T6_Lizard_Support_4m_01_A`); `1950301003` (`Prop_Decoration_T6_Lizard_Support_4m_02_A`); `3607939791` (`Prop_Decoration_T6_Lizard_Wall_2m_01_A`) | all distinct registered forms |
| Fences 1 | Bamboo Fence Pole | `3121223720` (`Prop_Decoration_T6_Fence_Bamboo_Pole_01`); `3349466374` (`Prop_Decoration_T6_Fence_Bamboo_Pole_02`) | all distinct registered forms |
| Fences 1 | Carved Tropical Wood Fence Pole | `161588181` (`Prop_Decoration_T6_Fence_Wood_Pole_01`); `1698793327` (`Prop_Decoration_T6_Fence_Wood_Pole_02`); `2429035331` (`Prop_Decoration_T6_Fence_Wood_Pole_03`) | all distinct registered forms |
| Fences 2 | Prison Fence | `3434035481` (`Prop_Decoration_T4_Fence_Prison`) | placeable fence not fixed variant |
| Flower Pots 1 | Fern Pot | `673405648` (`Empty_Flower_Pot_CNY_Fern_01`) | exact debug name or fingerprint |
| Illumination 2 | Wall Lantern | `2560383707` (`Prop_Decoration_Comfort_T4_IlluminationWall`) | normal non prefixed decoration |
| Miscellaneous 1 | Bamboo Crate I | `4136278992` (`Prop_Decoration_T6_Crate_Bamboo_01`); `1412268864` (`Prop_Decoration_T6_Crate_Bamboo_02`) | all distinct registered forms |
| Miscellaneous 1 | Large Bamboo Crate | `3554673768` (`Prop_Decoration_T6_Crate_Bamboo_Large_01`); `2326445767` (`Prop_Decoration_T6_Crate_Bamboo_Large_02`) | all distinct registered forms |
| Miscellaneous 2 | Iron Grate | `2583752204` (`Prop_Decoration_T4_Grate_Iron_Big`); `3638722075` (`Prop_Decoration_T4_Grate_Steel_small`) | all distinct registered forms |
| Miscellaneous 2 | Palm Leaf Basket | `4206640908` (`Prop_Decoration_T6_Basket_01`); `1108421963` (`Prop_Decoration_T6_Basket_02`); `4179466778` (`Prop_Decoration_T6_Basket_03`) | all distinct registered forms |
| Miscellaneous 2 | Resource Bundle I | `1313835688` (`Prop_Decoration_T1_RessourceBundle_01`) | exact debug name or fingerprint |

## Validation

`scripts/test-endless-catalogue-repairs.py` continues to verify all 100 of the previous 0.5.1 fixes at their **same group positions**, and `scripts/test-endless-final-27.py` independently checks each new position's IDs, game debug names, display labels and categories against the checked-out SDK. The full validator asserts **0 unresolved positions**, **2,934 distinct game IDs**, **3,464 original positions** and no variant set with unknown or repeated IDs. Both the stable build and Kiln latest-release follower run these tests.

The 1.0 in-game acceptance test should check `Backpacks`, `Books`, `Dividers`, `Flower Pots 1`, `Miscellaneous 2`, and one of the decoration groups using a disposable empty Magic Chest. A passed build/registry validation is not an assertion that game inventory transactions succeed.
