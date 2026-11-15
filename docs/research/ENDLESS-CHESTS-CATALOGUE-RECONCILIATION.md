# Endless Chests catalogue identity reconciliation — 0.5.1

Source: canonical `EndlessChests/defaults/catalogue.json` vs pinned Kiln SDK registry (`frostdev-ops/kiln`, commit `eba9a5e1e0e0044a9146fdd53021a751e2957030`). Audit provenance: [`scripts/data/endless-chests-identity-audit-0.5.1.json`](../../scripts/data/endless-chests-identity-audit-0.5.1.json).

The original 195 named groups and all 3,464 ordered positions are preserved. Previous v0.5.0 catalogue: **3,337 resolved, 127 unresolved**. After repair: **3,437 resolved (2,908 distinct game IDs), 27 unresolved**. No runtime code change or automatic ambiguous-name substitution was required.

## What was safely repaired

| Evidence used | Positions |
|---|---:|
| chest slots | 4 |
| color size form | 28 |
| curated | 17 |
| fern size form | 3 |
| grass size form | 3 |
| magic chest slots | 5 |
| named pot | 11 |
| numbered piece | 5 |
| quoted display | 24 |
| **Total** | **100** |

The accepted mappings use explicit IDs tied to the current SDK's **internal debug name, display name and category**. Highlights include: plant pot size/color forms, 24/32/40/56/72-slot Magic Chests, quoted tombstone/painting/mannequin/divider labels, numbered collectible pieces, two distinct tiers of Shroud flasks, and the actual fire grenade rather than a test projectile.

### All 100 repaired labels

| Group | Previous label | Canonical item ID | Game display name | Debug identity |
|---|---|---:|---|---|
| Cemetery | Tombstone Always Loved | `1653941305` | Tombstone "Always Loved" | `Prop_Decoration_T3_Tombstone4` |
| Cemetery | Tombstone Forever In Our Hearts | `2907325488` | Tombstone "Forever In Our Hearts" | `Prop_Decoration_T3_Tombstone6` |
| Cemetery | Tombstone In Loving Memory | `2678117077` | Tombstone "In Loving Memory" | `Prop_Decoration_T3_Tombstone10` |
| Cemetery | Tombstone Together Again | `75272151` | Tombstone "Together Again" | `Prop_Decoration_T3_Tombstone9` |
| Cemetery | Tombstone Until We Meet Again | `3941822412` | Tombstone "Until We Meet Again" | `Prop_Decoration_T3_Tombstone5` |
| Decorations 3 | Embervale Miniature World Map | `467801292` | Embervale Miniature Model | `Prop_Decoration_T7_Embervale_Miniature_WorldMap` |
| Decorative Collections | Pikemead Statue Showcase | `171960142` | Pikemead Statue Showcase | `Prop_Collection_06_Statues_Showcase_PikeMead` |
| Dividers | Room Divider Dragon | `1008591425` | Room Divider "Dragon" | `Prop_Decoration_T5_CNY_Divider_04` |
| Dividers | Room Divider Fish | `1108531204` | Room Divider "Fish" | `Prop_Decoration_T5_CNY_Divider_02` |
| Dividers | Room Divider Flower | `1209952398` | Room Divider "Flower" | `Prop_Decoration_T5_CNY_Divider_01` |
| Dividers | Room Divider Snake | `3460936505` | Room Divider "Snake" | `Prop_Decoration_T5_CNY_Divider_03` |
| Flower Pots 1 | Tiny Pink Flower Pot | `4272163256` | Pink Flower | `Flower_Pot_Tiny_Pink` |
| Flower Pots 1 | Small Pink Flower Pot | `1682046505` | Pink Flower | `Flower_Pot_Small_Ground_Pink` |
| Flower Pots 1 | Small Pink Wall Pot | `533905592` | Pink Wall Flower | `Flower_Pot_Small_Wall_Pink` |
| Flower Pots 1 | Medium Pink Flower Pot | `2927706943` | Pink Flower | `Flower_Pot_Medium_Pink` |
| Flower Pots 1 | Long Pink Flower Pot | `308344880` | Pink Flower | `Flower_Pot_Long_Ground_Pink` |
| Flower Pots 1 | Long Pink Wall Flower Pot | `1416182802` | Pink Wall Flower | `Flower_Pot_Long_Wall_Pink` |
| Flower Pots 1 | Square Pink Flower Pot | `2741207192` | Pink Flower | `Flower_Pot_Square_Pink` |
| Flower Pots 1 | Tiny Red Flower Pot | `3094342003` | Red Flower | `Flower_Pot_Tiny_Red` |
| Flower Pots 1 | Small Red Flower Pot | `157147909` | Red Flower | `Flower_Pot_Small_Ground_Red` |
| Flower Pots 1 | Small Red Wall Flower Pot | `2029375274` | Red Wall Flower | `Flower_Pot_Small_Wall_Red` |
| Flower Pots 1 | Medium Red Flower Pot | `2413776919` | Red Flower | `Flower_Pot_Medium_Red` |
| Flower Pots 1 | Long Red Flower Pot | `2373764287` | Red Flower | `Flower_Pot_Long_Ground_Red` |
| Flower Pots 1 | Long Red Wall Flower Pot | `1699546726` | Red Wall Flower | `Flower_Pot_Long_Wall_Red` |
| Flower Pots 1 | Square Red Flower Pot | `3804066383` | Red Flower | `Flower_Pot_Square_Red` |
| Flower Pots 1 | Tiny White Flower Pot | `3367211688` | White Flower | `Flower_Pot_Tiny_White` |
| Flower Pots 1 | Small White Flower Pot | `120176694` | White Flower | `Flower_Pot_Small_Ground_White` |
| Flower Pots 1 | Small White Wall Flower Pot | `1763603908` | White Wall Flower | `Flower_Pot_Small_Wall_White` |
| Flower Pots 1 | Medium White Flower Pot | `2140221206` | White Flower | `Flower_Pot_Medium_White` |
| Flower Pots 1 | Long White Flower Pot | `2557793336` | White Flower | `Flower_Pot_Long_Ground_White` |
| Flower Pots 1 | Long White Wall Flower Pot | `2553292740` | White Wall Flower | `Flower_Pot_Long_Wall_White` |
| Flower Pots 1 | Square White Flower Pot | `2204868420` | White Flower | `Flower_Pot_Square_White` |
| Flower Pots 1 | Tiny Yellow Flower Pot | `811648475` | Yellow Flower | `Flower_Pot_Tiny_Yellow` |
| Flower Pots 1 | Small Yellow Flower Pot | `1594649719` | Yellow Flower | `Flower_Pot_Small_Ground_Yellow` |
| Flower Pots 1 | Small Yellow Wall Flower Pot | `2862328564` | Yellow Wall Flower | `Flower_Pot_Small_Wall_Yellow` |
| Flower Pots 1 | Medium Yellow Flower Pot | `1212068557` | Yellow Flower | `Flower_Pot_Medium_Yellow` |
| Flower Pots 1 | Long Yellow Flower Pot | `163823517` | Yellow Flower | `Flower_Pot_Long_Ground_Yellow` |
| Flower Pots 1 | Long Yellow Wall Flower Pot | `3643820464` | Yellow Wall Flower | `Flower_Pot_Long_Wall_Yellow` |
| Flower Pots 1 | Square Yellow Flower Pot | `1526921804` | Yellow Flower | `Flower_Pot_Square_Yellow` |
| Flower Pots 1 | Small Fern Pot | `2019603203` | Fern | `Flower_Pot_Small_Ground_Fern` |
| Flower Pots 1 | Medium Fern Pot | `1228884892` | Fern | `Flower_Pot_Medium_Fern` |
| Flower Pots 1 | Square Fern Pot | `1665119413` | Fern | `Flower_Pot_Square_Fern` |
| Flower Pots 1 | Small Grass Pot | `4231642797` | Grass | `Flower_Pot_Small_Ground_Gras` |
| Flower Pots 1 | Medium Grass Pot | `1479156124` | Grass | `Flower_Pot_Medium_Gras` |
| Flower Pots 1 | Square Grass Pot | `814558577` | Grass | `Flower_Pot_Square_Gras` |
| Flower Pots 2 | Small Monstera Flower Pot | `523366717` | Monstera Plant | `Flower_Pot_Small_Monstera` |
| Flower Pots 2 | Medium Monstera Pot | `1666087652` | Monstera Plant | `Flower_Pot_Medium_Monstera` |
| Flower Pots 2 | Grapple Plant Flower Pot | `1824422740` | Grapple Plant Flower | `Flower_Pot_Medium_DevilsClaw` |
| Flower Pots 2 | Medium Cactus Plant Pot | `2779909442` | Cactus Plant | `Flower_Pot_Medium_Cactus` |
| Flower Pots 2 | Large Cactus Plant Pot | `274681682` | Cactus Plant | `Flower_Pot_Round_Large_Cactus` |
| Flower Pots 2 | Heather Flower Pot | `2360020747` | Heather Flower | `Flower_Pot_Low_Large_Heatherbush` |
| Flower Pots 2 | Pink Lily Flower Pot | `3148035436` | Pink Lily Flower | `Empty_Flower_Pot_CNY_Lily_01` |
| Flower Pots 2 | Two-colored Lily Flower Pot | `1920323571` | Two-colored Lily Flower | `Empty_Flower_Pot_CNY_Lily_03` |
| Flower Pots 2 | Yellow Lily Flower Pot | `3859722488` | Yellow Lily Flower | `Empty_Flower_Pot_CNY_Lily_02` |
| Flower Pots 2 | White Cymbidum Flower Pot | `3688073501` | White Cymbidum Flower | `Empty_Flower_Pot_CNY_Cymbidum_02` |
| Flower Pots 2 | Yellow Cymbidum Flower Pot | `3125832694` | Yellow Cymbidum Flower | `Empty_Flower_Pot_CNY_Cymbidum_01` |
| Magic Chests | Magic Storage Chest (24 slots) | `4044650321` | Small Magic Chest | `Prop_Storage_24_T1_Box_Magic` |
| Magic Chests | Magic Storage Chest (32 slots) | `2463706463` | Medium Magic Chest | `Prop_Storage_32_T2_Box_Magic` |
| Magic Chests | Magic Storage Chest (40 slots) | `3681207826` | Large Magic Chest | `Prop_Storage_40_T3_Magic` |
| Magic Chests | Magic Storage Chest (56 slots) | `4157937420` | Huge Magic Chest | `Prop_Storage_56_T4_Magic` |
| Magic Chests | Magic Storage Chest (72 slots) | `30818586` | Enormous Magic Chest | `Prop_Storage_72_T6_Magic` |
| Mannequin | Mannequin Back Off | `2681848943` | Mannequin "Back Off" | `Prop_Collection_Mannequin_Armor_pose03` |
| Mannequin | Mannequin Here I Come | `2030809898` | Mannequin "Here I Come" | `Prop_Collection_Mannequin_Armor_pose04` |
| Mannequin | Mannequin Let Me Serve You | `501208175` | Mannequin "Let Me Serve You" | `Prop_Collection_Mannequin_Armor_pose06` |
| Mannequin | Mannequin Look At Me | `320245652` | Mannequin "Look At Me" | `Prop_Collection_Mannequin_Armor_pose02` |
| Mannequin | Mannequin Strike A Pose | `3072123387` | Mannequin "Strike A Pose" | `Prop_Collection_Mannequin_Armor_pose05` |
| Mannequin | Mannequin Victory | `1193821325` | Mannequin "Victory" | `Prop_Collection_Mannequin_Armor_pose01` |
| Other Potions | Diver's Endurance Flask | `3254225118` | Diver’s Endurance Flask | `Consumable_Potion_Flask_SwimSprintStaminaReductionMod_T6` |
| Potions | Greater Shroud Flask (T3) | `209497692` | Greater Shroud Survival Flask | `Consumable_Potion_Flask_Fog_Survival_Flask_T3` |
| Potions | Greater Shroud Flask (T5) | `1843136640` | Greater Shroud Survival Flask | `Consumable_Potion_Flask_Fog_Survival_Flask_T5` |
| Potions | Diver's Endurance Flask | `3254225118` | Diver’s Endurance Flask | `Consumable_Potion_Flask_SwimSprintStaminaReductionMod_T6` |
| Queen Pikemead Statue | Queen Pikemead Piece I | `3883219916` | Queen Pikemead Statue Piece | `Collection_StatuePikeMead_Piece_01` |
| Queen Pikemead Statue | Queen Pikemead Piece II | `1556757497` | Queen Pikemead Statue Piece | `Collection_StatuePikeMead_Piece_02` |
| Queen Pikemead Statue | Queen Pikemead Piece III | `2624605842` | Queen Pikemead Statue Piece | `Collection_StatuePikeMead_Piece_03` |
| Secret Doors 1 | Carved Secret Bookshelf Door | `3126384595` | Carved Secret Bookshelf Door | `Base_Shelf_Secret_02` |
| Secret Doors 2 | Fake Ancient Door | `2172312678` | Fake Ancient Door | `Base_Door_Secret_Ancient_Fake` |
| Shroud Flasks | Greater Shroud Flask (T3) | `209497692` | Greater Shroud Survival Flask | `Consumable_Potion_Flask_Fog_Survival_Flask_T3` |
| Shroud Flasks | Greater Shroud Flask (T5) | `1843136640` | Greater Shroud Survival Flask | `Consumable_Potion_Flask_Fog_Survival_Flask_T5` |
| Special Chests | Wooden Storage Chest (32 slots) | `1302084846` | Wooden Chest | `Prop_Storage_32_T2_Chest_Wood` |
| Special Chests | Silver Storage Chest (40 slots) | `1788793277` | Silver Chest | `Prop_Storage_40_T5_Chest_Silver` |
| Statues | Big Bronze Statue Snake | `2346845127` | Big Bronze Statue "Snake" | `Prop_Decoration_T4_Statue_Snake` |
| Statues | Drak Stone Totem I | `674281730` | Drak Stone Totem I | `Prop_Decoration_T6_StoneTotem_01_A` |
| Storage Chests | Storage Box (24 slots) | `213939880` | Small Chest | `Prop_Storage_24_T1_Box` |
| Storage Chests | Storage Box (32 slots) | `3642338342` | Medium Chest | `Prop_Storage_32_T2_Box` |
| Storage Chests | Storage Box (40 slots) | `4204849649` | Large Chest | `Prop_Storage_40_T3_Box` |
| Storage Chests | Storage Box (56 slots) | `2834235854` | Huge Chest | `Prop_Storage_56_T4_Box` |
| Throwables | Incendiary Ball | `1462961786` | Incendiary Ball | `Consumable_Grenade_Fire_T5` |
| Tools & Components | Cauldron | `311508484` | Cauldron | `Prop_Component_T5_Black_Cauldron` |
| Treasure Maps | The Dragons' Map | `2381061644` | The Dragons' Map | `8kLoreText_TreasureMap_completed_communityQuest_2025` |
| Vanity Outfits 1 | Royal Gloves | `3203382450` | Royal Gloves | `Vanity_T2_Royal_Gloves` |
| Wall Decorations 1 | Boar At Home | `3201054228` | "Boar At Home" | `Prop_Decoration_Comfort_T4_Wall_Painting_7` |
| Wall Decorations 1 | Flower Snake | `3753859139` | "Flower Snake" | `Prop_Decoration_Comfort_T4_Wall_Painting_3` |
| Wall Decorations 1 | Flower Vase | `1343085975` | "Flower Vase" | `Prop_Decoration_Comfort_T4_Wall_Painting_6` |
| Wall Decorations 1 | Round-Billed Sailer On One Leg | `19079311` | "Round-Billed Sailer On One Leg" | `Prop_Decoration_Comfort_T4_Wall_Painting_4` |
| Wall Decorations 1 | Still Life | `1237085664` | "Still Life" | `Prop_Decoration_Comfort_T4_Wall_Painting_1` |
| Wall Decorations 1 | Temple Landscape | `1790893349` | "Temple Landscape" | `Prop_Decoration_Comfort_T4_Wall_Painting_2` |
| Wall Decorations 1 | Tower In The Night | `3847020528` | "Tower In The Night" | `Prop_Decoration_Comfort_T4_Wall_Painting_5` |
| Wall Decorations 1 | Wolf's Choir | `1688806746` | "Wolf's Choir" | `Prop_Decoration_Comfort_T4_Wall_Painting_8` |
| Wall Tapestry | Wall Tapestry Piece I | `3603348812` | Wall Tapestry Piece | `Collection_WallTapestry_Piece_01` |
| Wall Tapestry | Wall Tapestry Piece II | `2252200136` | Wall Tapestry Piece | `Collection_WallTapestry_Piece_02` |

## Unresolved identities — intentional, no arbitrary item selection

The 27 remaining positions have no unique, defensible replacement using only their old labels and the pinned SDK. Several candidates are **different-sized or different-versioned objects with identical display names**; the mod cannot infer which was originally intended. The embedded catalogue retains `{"unresolved": "..."}` for these positions rather than spawning the wrong item.

| Group | Original label | Candidate item IDs and internal identities |
|---|---|---|
| Backpacks | Small Backpack | `3728213390` (`Backpack_T1_NEW`); `4231879756` (`Backpack_T1`) |
| Backpacks | Medium Backpack | `3773368910` (`Backpack_T2_NEW`); `4053516652` (`Backpack_T2`) |
| Backpacks | Large Backpack | `996296787` (`Backpack_T3_NEW`); `3131144042` (`Backpack_T3`) |
| Books | Book I | `413542990` (`Prop_Decoration_Book_14`); `589587638` (`Prop_Decoration_Book_16`); `676824530` (`Prop_Decoration_Book_04`); `743954578` (`Prop_Decoration_Book_07`); `1042567701` (`Prop_Decoration_Book_03`); `1665817031` (`Prop_Decoration_Book_06`); `1878602124` (`Prop_Decoration_Book_18`); `1920341845` (`Prop_Decoration_Book_10`); `2283198170` (`Prop_Decoration_Book_08`); `2548133338` (`Prop_Decoration_Book_13`); `2975370828` (`Prop_Decoration_Book_01`); `3330526371` (`Prop_Decoration_Book_12`); `3853710961` (`Prop_Decoration_Book_02`); `4242562036` (`Prop_Decoration_Book_17`) |
| Books | Collection Of Books I | `1276880412` (`Prop_Decoration_Book_Pile_02_B`); `3259443869` (`Prop_Decoration_Book_Pile_01_A`) |
| Books | Collection Of Books II | `147747455` (`Prop_Decoration_Book_Pile_03_C`); `1732682352` (`Prop_Decoration_Book_Pile_04_D`) |
| Books | Collection Of Books III | `186386614` (`Prop_Decoration_Book_Pile_05_E`); `523691097` (`Prop_Decoration_Book_Pile_06_F`) |
| Decorations 2 | Bamboo Stairs | `1517467835` (`Prop_Decoration_T6_Lizard_Stairs_2m_01_A`); `2175416596` (`Prop_Decoration_T6_Lizard_Stairs_4m_01_A`) |
| Decorations 2 | Conifer Crate | `553841242` (`Prop_Decoration_T5_Crate_Conifer02`); `2537359689` (`Prop_Decoration_T5_Crate_Conifer01`); `3904030989` (`Prop_Decoration_T5_Crate_Conifer03`) |
| Decorations 3 | Empty Cymbidium Pot | `2125538426` (`Empty_Flower_Pot_CNY_Empty_02b`); `4135259126` (`Empty_Flower_Pot_CNY_Empty_02a`) |
| Decorations 3 | Empty Farmbed | `604994553` (`Prop_Decoration_Farmbed_01c`); `1392367211` (`Prop_Decoration_Farmbed_01b`); `2425261321` (`Prop_Decoration_Farmbed_01d`); `3310867211` (`Prop_Production_Farmbed_Empty`); `3916554796` (`Prop_Decoration_Farmbed_01a`) |
| Decorations 3 | Empty Lily Pot | `2328440963` (`Empty_Flower_Pot_CNY_Empty_01a`); `3534930699` (`Empty_Flower_Pot_CNY_Empty_01b`); `3593229714` (`Empty_Flower_Pot_CNY_Empty_01c`) |
| Decorations 4 | Large Bamboo Basket | `1506922480` (`Prop_Decoration_T6_Basket_Large_02`); `2694217922` (`Prop_Decoration_T6_Basket_Large_04`); `2867139849` (`Prop_Decoration_T6_Basket_Large_03`); `3541979658` (`Prop_Decoration_T6_Basket_Large_01`) |
| Decorations 5 | Simple Wood Support | `2242544273` (`Prop_Decoration_T6_Simple_Support_4m_02_A`); `2587423299` (`Prop_Decoration_T6_Support_4m_01_A`); `3556756437` (`Prop_Decoration_T6_Support_4m_02_A`); `3758512906` (`Prop_Decoration_T6_Simple_Support_4m_01_A`) |
| Decorations 6 | Veilwater Basin Boardwalk I | `33507171` (`Prop_Decoration_T6_Boardwalk_8m_01_A`); `123726817` (`Prop_Decoration_T6_Boardwalk_4m_02_A`); `669605144` (`Prop_Decoration_T6_Boardwalk_2m_01_A`); `1030788931` (`Prop_Decoration_T6_Boardwalk_4m_01_A`) |
| Decorations 6 | Veilwater Basin Boardwalk II | `1195149031` (`Prop_Decoration_T6_Simple_Boardwalk_4m_01_A`); `4043051492` (`Prop_Decoration_T6_Simple_Boardwalk_2m_01_A`) |
| Dividers | Bamboo Room Divider I | `260723984` (`Prop_Decoration_T6_Lizard_Gate_02`); `371722890` (`Prop_Decoration_T6_Lizard_Gate_01`); `1950301003` (`Prop_Decoration_T6_Lizard_Support_4m_02_A`); `3413337702` (`Prop_Decoration_T6_Lizard_Support_4m_01_A`); `3607939791` (`Prop_Decoration_T6_Lizard_Wall_2m_01_A`) |
| Fences 1 | Bamboo Fence Pole | `3121223720` (`Prop_Decoration_T6_Fence_Bamboo_Pole_01`); `3349466374` (`Prop_Decoration_T6_Fence_Bamboo_Pole_02`) |
| Fences 1 | Carved Tropical Wood Fence Pole | `161588181` (`Prop_Decoration_T6_Fence_Wood_Pole_01`); `1698793327` (`Prop_Decoration_T6_Fence_Wood_Pole_02`); `2429035331` (`Prop_Decoration_T6_Fence_Wood_Pole_03`) |
| Fences 2 | Prison Fence | `2452070869` (`Prop_Decoration_T4_Prison_Fence_Fixed`); `3434035481` (`Prop_Decoration_T4_Fence_Prison`) |
| Flower Pots 1 | Fern Pot | No unambiguous registered candidate |
| Illumination 2 | Wall Lantern | `2560383707` (`Prop_Decoration_Comfort_T4_IlluminationWall`); `3019273830` (`_Prop_Decoration_Comfort_T2_Lantern_01_A_Wall`) |
| Miscellaneous 1 | Bamboo Crate I | `1412268864` (`Prop_Decoration_T6_Crate_Bamboo_02`); `4136278992` (`Prop_Decoration_T6_Crate_Bamboo_01`) |
| Miscellaneous 1 | Large Bamboo Crate | `2326445767` (`Prop_Decoration_T6_Crate_Bamboo_Large_02`); `3554673768` (`Prop_Decoration_T6_Crate_Bamboo_Large_01`) |
| Miscellaneous 2 | Iron Grate | `2583752204` (`Prop_Decoration_T4_Grate_Iron_Big`); `3638722075` (`Prop_Decoration_T4_Grate_Steel_small`) |
| Miscellaneous 2 | Palm Leaf Basket | `1108421963` (`Prop_Decoration_T6_Basket_02`); `4179466778` (`Prop_Decoration_T6_Basket_03`); `4206640908` (`Prop_Decoration_T6_Basket_01`) |
| Miscellaneous 2 | Resource Bundle I | No unambiguous registered candidate |

### Specific concerns

- **Backpacks:** `Backpack_Tx` versus `Backpack_Tx_NEW` might differ functionally. Do not select the latter solely from its suffix; check the actual game item.
- **Decorations and books:** More than one item can legitimately share the same display text. Dimensions, geometry, and edition may differ. Individual screenshot or in-game item inspection is needed before choosing.
- **Fern Pot:** The game has several distinct items displayed `Fern`, including an `Empty_Flower_Pot_CNY_Fern_01` item. The original unsized label does not establish which.
- **Resource Bundle I:** The pinned registry contains `Resource Bundle II` (ID `2243725147`) but no verified `Resource Bundle I`. Do not relabel II as I.
- **Refill diagnostics:** The runtime already continues after per-item `Items.Give()` failures (unless the inventory is unavailable), independently of catalogue identity reconciliation.

## Validation and next step

`scripts/test-endless-catalogue-repairs.py` asserts each repaired group index, original label, numeric ID, debug name, display name and category against the pinned SDK. `scripts/validate-endless-catalogue.py` asserts all 195 groups, 3,464 positions, 3,437 resolved references and 27 unresolved markers. Test in-game on disposable chests before merging the PR; `Flower Pots 1/2`, `Magic Chests`, `Cemetery`, `Wall Decorations 1`, `Mannequin`, `Potions` and `Throwables` are particularly useful.

