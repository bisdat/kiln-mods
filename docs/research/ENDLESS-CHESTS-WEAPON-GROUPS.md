# Endless Chests dedicated weapon groups — v0.6.0

## Purpose and source of truth

The user requested dedicated groups for **one-handed melee, two-handed melee, daggers, bows, wands and staffs**, then added **shields** before merging the same PR, following the accepted 0.5.2 catalogue reconciliation. This release is a **new catalogue feature**, not a change to the refill runtime or to the original 195 groups.

The identity authority is `frostdev-ops/kiln`'s `managed/services/Kiln.Items/Data/items.tsv`, pinned by this repository at commit `eba9a5e1e0e0044a9146fdd53021a751e2957030`. The entire table is identical to the official Kiln `v0.2.0-dev.28` revision `2301cc5fd3138aa91119e8bc0f082569218315cd` (4,262 records, no changed game IDs or metadata). The 489 typed weapons use SDK category `Weapons`; the 58 equipable shields use `Equipment`.

## Exact classification contract

Only items whose internal debug identity matches:

```text
^(Craft_)?Weapon_(T[0-9]+|TX)(-TX)?_(1H|2H)_(remainder)$
```

enter the six weapon classes. Classification of the `remainder` is **case-insensitive** and in this precedence order:

1. Contains `dagger` → Daggers, even though Kiln labels them as 2H.
2. Contains `bow` → Bows.
3. Contains `wand` → Wands, even though Kiln labels them as 1H.
4. Contains `staff` → Staffs, even though Kiln labels them as 2H.
5. Remaining 1H → One-Handed Melee (swords, axes, maces).
6. Remaining 2H → Two-Handed Melee (greatswords, greataxes, hammers).

The 489 typed weapon IDs are all distinct and belong to exactly one of these classes. The other **21** records in category `Weapons` are deliberately excluded from these classes: ammunition-like grenades, torches, miscellaneous throwable toys, unarmed fists and internal test objects. The existing Throwables and Handheld Torches groups remain unchanged; this feature does not reinterpret those records or remove their older group memberships.

When multiple tier variants have identical human-readable display names, **keep their different numeric IDs**. This includes the crafted `Craft_Weapon_...` and found `Weapon_...` families. No display-name fallback is added.

## New named chest selections

| Group name | Item IDs | Notes |
|---|---:|---|
| `One-Handed Melee` | 51 | First tier-ordered tranche |
| `One-Handed Melee 2` | 51 | Second tranche |
| `One-Handed Melee 3` | 51 | Third tranche |
| `Two-Handed Melee` | 54 | First tranche |
| `Two-Handed Melee 2` | 53 | Second tranche |
| `Daggers` | 60 | Dual blades are 2H in the game registry |
| `Bows` | 62 | Normal and crafted bows |
| `Wands` | 58 | 1H magical weapons |
| `Staffs` | 49 | 2H magical weapons |
| `Shields` | 58 | Equipable shields (normal, crafted and loot forms) |
| **Total** | **547** | Ten new groups; none overlaps another |

Items within a class are ordered by ascending internal tier (`T0` to `T7`, placing each `Tn-TX` just after `Tn`, then `TX`), then exact internal debug name and ID. Classes too large for a chest are partitioned into near-equal, contiguous groups: one-handed melee into 3 × 51, two-handed melee into 54 + 53. Neither arbitrary selection nor display-name lookup is used.

**Capacity matters:** a 72-slot Magic Chest can hold each new complete group, including `Shields` (58). A 24/32/40/56-slot chest cannot hold 49–62 unique items; the existing `ItemCatalogue.Resolve` uses `Math.Min(inventory.GenericSlots,72)` and only fills the first eligible items in the ordered list. Use separate **72-slot** chests per group for a complete catalogue, or smaller chests to hold partial selections. Each numbered group requires a separate chest to cover every item in that class.

## Catalogue invariants

| Metric | Accepted 0.5.2 | 0.6.0 candidate |
|---|---:|---:|
| Original groups, unmodified | 195 | 195 |
| New weapon/shield groups | 0 | 10 |
| Total groups | 195 | 205 |
| Original membership positions, unmodified | 3,464 | 3,464 |
| Total membership positions | 3,464 | 4,011 |
| Distinct verified item IDs | 2,934 | 3,481 |
| Unresolved entries | 0 | 0 |
| Original multi-variant positions | 11 | 11 |

`scripts/test-endless-weapon-groups.py` computes the expected group membership directly from the pinned SDK, tests exactly 489 non-overlapping weapon IDs and 58 equipable shield IDs, verifies the entire original 195-group membership and 2,934-item metadata using fixed FNV fingerprints, ensures all numeric identities match the SDK, excludes five decorative shield props and two magical shield spells, and enforces the 72-item maximum group size. This runs in both the normal stable package CI and the latest official Kiln compatibility workflow. The original v0.5.1 and v0.5.2 correction tests still run.

## In-game acceptance

Back up the world and use disposable **72-slot Magic Chests** to check:

- Rename to `One-Handed Melee`, `One-Handed Melee 2` and `One-Handed Melee 3`; confirm swords/axes/maces appear and that each chest contains different identities.
- Rename to `Two-Handed Melee` and `Two-Handed Melee 2`; confirm greatswords/greataxes/hammers appear, not bows/daggers/staffs.
- `Daggers` contains dual daggers, `Bows` contains bows, `Wands` wands, `Staffs` staffs, and `Shields` contains 58 equipable shields (not decorative props or spells). Expect multiple tiers with repeated *display names* but distinct item IDs.
- `echest groups melee` and `echest groups wands` should list these groups. `echest bind Staffs` should work for an empty chest.
- Check the accepted `Crafting G-O` Mycelium, Indigo, Basalt and `Fishing Bait` with Grasshopper/Scarab still work. Check known rename → clear/refill, unknown rename → preserve, `Empty` → destructive clear, on disposable chests.

Static SDK validity and successful packaging do **not** by themselves confirm that every weapon ID is permitted by `Items.Give` in-game. Any denied or unsupported IDs should be diagnosed by item ID from the existing per-item warning, rather than silently removed. Wait for owner in-game acceptance before merging or tagging.
