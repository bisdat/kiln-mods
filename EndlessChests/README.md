# Endless Chests (Kiln) — v0.6.3 (experimental)

An independent C# implementation through the Kiln SDK, retaining the original Endless Chests group defaults (attributed to HipShot808) and adding dedicated weapon groups. It does **not** load or redistribute the Windows ShroudForge DLL or use undocumented native memory offsets.

**v0.6.0 — dedicated weapon groups ([issue #45](https://github.com/bisdat/kiln-mods/issues/45)):** Adds **489 verified weapon IDs and 58 equipable shields** across **10 new chest-sized groups**, covering one-handed melee, two-handed melee, daggers, bows, wands, staffs and shields. All 195 existing groups, 3,464 original positions and 2,934 pre-existing item identities are retained. Total: **205 groups, 4,011 entries and 3,481 distinct item IDs**, with zero unresolved entries. Weapon identities come from the Kiln SDK's internal class/debug names, not ambiguous display names. Older tier variants and craft/loot entries remain individually identifiable. The mod's runtime and settings INI are unchanged; only the embedded canonical JSON, version and tests change.

### Block consolidation correction (v0.6.3)

`Blocks` now contains 69 distinct non-roof block types from the 15 original block groups. All 13 roof block types remain in `Roof Materials` and `Building Materials` (70 items). `Blocks 2` is removed. Both combined groups fit separately in a 72-slot Magic Chest, with no overlapping IDs. All other groups and item definitions are unchanged. Existing chests retain their contents on initial observation; to clear and refill an existing `Blocks` chest, use the normal rename workflow on a disposable or backed-up chest. An old `Blocks 2` name is no longer recognised; remove any obsolete manual binding if applicable.

### Consolidated groups (v0.6.2)

All 24 groups below fit into a 72-slot Magic Chest. Each is an ordered union of existing groups, deduplicated by numeric item ID with variants expanded. All 207 previous groups and all 3,481 item identities remain unchanged. Rename a chest to the new group name; no INI changes are required.

| Group | Source groups | Slots |
|---|---|---:|
| Seedlings | Seedlings 1, Seedlings 2 | 63 |
| Fences | Fences 1, Fences 2 | 66 |
| Flower Pots | Flower Pots 1, Flower Pots 2 | 71 |
| Illumination | Illumination 1, Illumination 2 | 60 |
| Secret Doors | Secret Doors 1, Secret Doors 2 | 29 |
| Wall Decorations | Wall Decorations 1, Wall Decorations 2 | 67 |
| Animal Husbandry | Animal Bait, Animal Food, Animal Housing, Animals, Baby Animals, Pets | 55 |
| Chests | Magic Chests, Storage Chests, Special Chests | 11 |
| Stations | Cooking Stations, Crafting Stations, Farming Stations, More Stations, Processing Stations, Production Places | 41 |
| Fishing | Fishing Rods, Fishing Bait, Raw Fish, Fish | 57 |
| Medical Supplies | Health Potions, Mana Potions, Other Potions, Antidotes, Shroud Flasks, Bandages, Potions | 41 |
| Plants | Fruits, Vegetables, Herbs, Grains, Mushrooms, Night Plants | 68 |
| Building Materials | Soil and Dirt, Terrain Materials, Roof Materials, Rubble Materials, Overgrowth Materials | 70 |
| All Tools | Tools, Tools & Components, Rakes | 40 |
| Travel Equipment | Gliders, Grappling Hooks, Traversal | 11 |
| Utilities | Water & Power, Water Utilities, World Utilities | 26 |
| All Doors | Doors, Secret Doors 1, Secret Doors 2 | 65 |
| Wall Furnishings | Banners, Wall Tapestry, Paintings, Mirrors & Signs, Alchemist Wall Hanging | 40 |
| Storage Furniture | Cupboards, Shelves, Display Showcases, Dividers | 57 |
| Food Ingredients | Cooking Ingredients, Uncooked Foods, Meats, Egg Foods | 60 |
| Pyrotechnics | Fireworks, Sparkworks | 27 |
| Collectibles | Artifacts, Fossils, Treasure Maps, Keys | 36 |
| Music | Songs, Instruments | 30 |
| Levelled Spells | Spells Level 1, Spells Level 2, Spells Level 3, Spells Level 4, Spells Level 5, Spells Level 6, Spells Level 7, Spells Level 8 | 61 |

At v0.6.2 the catalogue contained 231 groups and 5,255 logical entries. CI checks the exact ordered union, slot capacity and preservation of all prior groups.

### Combined block groups (v0.6.1)

At v0.6.1, `Blocks` contained all 82 distinct item IDs from the 15 existing block groups, deduplicated in original catalogue order. A 72-slot Magic Chest receives the first 72; rename a second chest `Blocks 2` for the remaining ten. All original groups and item identities remain unchanged. No INI changes are required. That release contained 207 groups and 4,103 logical entries; see the v0.6.3 correction above.

### Dedicated weapon groups

| Rename a Magic Chest to | Exact weapons | Description |
|---|---:|---|
| `One-Handed Melee` | 51 | Swords, axes and maces, first tier-sorted tranche |
| `One-Handed Melee 2` | 51 | Next one-handed melee tranche |
| `One-Handed Melee 3` | 51 | Remaining one-handed melee entries |
| `Two-Handed Melee` | 54 | Greatswords, greataxes and hammers, first tranche |
| `Two-Handed Melee 2` | 53 | Remaining two-handed melee entries |
| `Daggers` | 60 | Dual daggers; the game identifies these as `2H` |
| `Bows` | 62 | Standard and crafted bows |
| `Wands` | 58 | One-handed magical wands |
| `Staffs` | 49 | Two-handed magical staffs |
| `Shields` | 58 | Equipable shields, including crafted and loot variants |
| **Total** | **547** | Ten distinct named selections |

Use a **72-slot Magic Chest** for a complete 49–62-item group, including `Shields` (58). A smaller chest has limited slots and receives only the first available entries from its ordered group; it cannot contain every weapon in these larger groups. Each numbered group is independent: `One-Handed Melee` does **not** include the later numbered tranches. Use separate chests for all parts. Rename matching and `echest bind <group>` work as with existing groups. When a game display name is shared by several weapon tiers, the explicit item IDs keep their identities separate.

The remaining 21 records in the broad SDK `Weapons` category are non-class weapons (such as throwables, torches, unarmed/test objects); they are deliberately not added to these six classes. Shields instead use 58 `Equipment` items with `Shield_T*` or `Craft_Shield_T*` identities; five shield decorations and two protective-barrier spells are excluded. Existing Throwables and Handheld Torches groups are untouched. [Classification and acceptance details](../docs/research/ENDLESS-CHESTS-WEAPON-GROUPS.md).

**v0.5.2 final identity reconciliation — [issue #38](https://github.com/bisdat/kiln-mods/issues/38):** All **27 remaining unresolved catalogue positions** have been mapped to current item identities. Catalogue schema **v2** supports explicit multi-variant sets (`{"variants":[123,456]}`) when different real items share a display name: the runtime expands and verifies every ID, never selecting an arbitrary same-name item. This preserves all **195 groups and 3,464 original positions**, now **0 unresolved**, covering **2,934 distinct verified game item IDs** (including 11 multi-variant positions and 39 variant references). The 27 include current craftable backpacks, distinct models of books and decorations, an additional Fern plant pot, and Resource Bundle I (whose game name contains **two spaces**). [Complete decisions and mapping ledger](../docs/research/ENDLESS-CHESTS-FINAL-27.md). Previous item IDs, renaming behaviour and user INI semantics are unchanged; **in-game testing is still required**.

**v0.5.1 catalogue reconciliation — [issue #38](https://github.com/bisdat/kiln-mods/issues/38):** Resolve **100 of the remaining 127** uncertain legacy catalogue labels by verified item IDs. Current coverage is **3,437 of 3,464 group positions**, across **2,908 distinct game item IDs**; **27** ambiguous/absent labels remain explicit unresolved markers, without invented substitutions. This update repairs 45 colour/size-specific plant pots, 5 Magic Chest sizes, 4 ordinary chest sizes, 24 quoted inscription/decoration/mannequin labels, 5 numbered collectible pieces, and other carefully identified items including tier-specific flasks. The audit lists [all 100 mappings and every remaining candidate](../docs/research/ENDLESS-CHESTS-CATALOGUE-RECONCILIATION.md). **Only catalogue data and tests changed; the v0.5.0 refill runtime, settings format, and name/Empty semantics are unchanged.** Group order, size and total count remain the same.

**v0.5.0 catalogue refactor — [issue #38](https://github.com/bisdat/kiln-mods/issues/38):** The bundled [canonical JSON catalogue](defaults/catalogue.json) is the sole source of group membership and game-item identities. All **195 original group names and 3,464 positions** remain in their original order. In v0.5.0, **3,337 entries** referenced verified game IDs (**2,812 distinct items**); **127 uncertain entries** were explicit unresolved markers that cannot spawn a wrong item. This also repairs **Mycelium crafting material** (`3509012331`) versus the **Mycelium overgrowth object** (`1353762644`), **133 loot armour aliases**, **17 unambiguous potted plants**, the real `Torch` and current `Oven`. Previous fixes for Indigo, Basalt, Rough Wood Block, Mirage and Alchemist wall hangings are covered by exact-ID tests. Fishing Bait includes the pinned Grasshopper and Scarab identities. A failed refill transaction now logs the specific item and continues attempting later items, unless the inventory is no longer available.

**Breaking catalogue configuration change:** No historical item aliases or suffixes are supported in v0.5.0. `endless-chests.ini` is **settings only** (options, aliases, disabled groups and numeric-ID disabled items); an old `[Groups]` section may remain on disk but is **ignored**. The source of truth is embedded `catalogue.json`, which is shipped separately for inspection. Normal group names, automatic chest-name matching, `chests.json` bindings and destructive rename/Empty semantics are retained. This migration is intentional because there is currently only one user.

### Canonical identities and duplicate display names

Each JSON `groups` array refers directly to a decimal ID defined under `items`; IDs are validated against the game item's internal debug name, display name and category **on every refill**. No `Items.Lookup(displayName)` fallback remains. For example:

```json
"Crafting G-O": [/* ... */, 3509012331, /* ... */],
"Overgrowth Materials": [/* ... */, 1353762644, /* ... */]
```

This snippet illustrates the IDs, not a complete JSON file. Names such as `Mycelium`, `Indigo Plant` or `Mirage` can refer to more than one game identity. A chest named with a **unique** item display name still resolves automatically; ambiguous direct item names must use a group or numeric ID. You can also run `echest bind 3509012331` for a direct ID binding.

Schema-v2 group entries are authoritative numeric IDs or explicit `{"variants":[idA,idB,...]}` lists when the game has multiple legitimate physical models with the same display name. Every ID is checked against the game before refill; missing/changed identities fail closed. All 27 previously unresolved group positions have been reconciled; the complete resolution ledger is in [the final audit](../docs/research/ENDLESS-CHESTS-FINAL-27.md).

## Name-change behaviour

| Action using Enshrouded's in-game Rename interface | Behaviour |
|---|---|
| Rename `Tools` → `Starter Blocks` | **Clear the previous items**, then refill Starter Blocks |
| Rename `Starter Blocks` → `Animals` | **Clear the previous items**, then refill Animals |
| Rename to `tools`, `TOOLS`, `tOoLs`, or other capitalisation of a known group | Recognise the original group; **clear old contents** (if an actual rename was observed), then refill |
| Rename to `My Storage` or any **unrecognised** name | **Do not delete anything** and **stop automatic refilling**; items already inside stay |
| Rename to `Empty`, `empty`, `EMPTY` or mixed-case variants | **Permanently clear automatically** at the next check and continue clearing anything put inside, with no confirmation |
| Restart with an ordinary named chest | Preserve contents on initial observation, then resume normal refill |
| Restart with a chest named `Empty` | **Automatically clear it**, even if no rename occurred during this session |

Automatic processing runs on the next scan (every 4 seconds by default). Renames to an unrecognised name also remove any obsolete manual binding, preventing the old group from refilling that chest. For manual `echest bind` use, existing assignments take precedence until renamed in-game or unbound.

**Back up your world.** Recognised renames and `Empty` intentionally delete inventory permanently. Deletions are serialized per chest via Kiln's `Items.TakeSlot` transactions, with rechecks of world, position, chest name fingerprint, exact slot and item identity. If another rename occurs while an asynchronous removal is in flight, remaining transactions are stopped; an already-issued removal cannot be undone. This release is experimental.

## Capitalisation support

Enshrouded exposes only an **FNV-1a64 fingerprint** of each custom chest name through its 8-byte `CustomPlayerString` component. A fingerprint by itself cannot simply be lowercased. The mod therefore compares the hash against variations of every canonical group, alias and unique item label:

- All **230 canonical group names** (including the original 195) and all **2 aliases** accept **any ASCII uppercase/lowercase mixture** (for example `sTaRtEr bLoCkS` or `animalS`), including multiword names. Any recognised rename triggers the normal clear-and-refill.
- For unique individual item labels with **30 or fewer ASCII letters**, the same arbitrary mixed-case matching is supported. For longer labels, original spelling, all-lowercase, ALL-UPPERCASE and title-case variants are covered, but arbitrary mixed-case letters are not yet guaranteed.
- Unicode characters, including non-ASCII letter case variations, may need an exact or common-case spelling. Spaces, punctuation and apostrophes are unchanged by case-insensitive matching. FNV name collisions are rejected when detected.
- All **32 ASCII capitalisations of `Empty`** are recognised independently and remain destructive.

No private string-store reads or Windows-client DLLs are required. The original name hashes were established from two successful live probes of the same game chest: `Starter Blocks = 0x35F105E7ECC53E32` and `Tools = 0xC26501E3ADE7DA3C`.

## Canonical catalogue and user settings

- [`defaults/catalogue.json`](defaults/catalogue.json) is **embedded into the mod and authoritative**. It defines 230 named groups, 5,232 logical entry positions and 3,481 verified item IDs with no unresolved entries; the original 195 groups and their 3,464 positions are unchanged. Eleven original positions expand to multiple explicitly listed physical variants. Group selection no longer reads display-name lists from an INI.
- [`defaults/endless-chests.defaults.ini`](defaults/endless-chests.defaults.ini) contains only `[Options]`, `[Aliases]`, `[DisabledGroups]`, and `[DisabledItems]`. The runtime copies it to its writable `endless-chests.ini` on first installation; an existing file remains in place, but any old `[Groups]` entries are ignored with a logged warning. To clean up an old file, back it up and replace its settings with the new small defaults, then `echest reload`.
- `[DisabledItems]` uses **numeric game IDs**, for example `3509012331=1`. Disabled groups exclude their item IDs globally even if another group includes them. Ambiguous plain display names are no longer valid exclusion keys. Two original aliases remain; the obsolete Diver's Endurance Flask alias was removed because its target is absent from the pinned catalogue.
- To curate uncertain identities, edit the tracked JSON catalogue in the repository and rebuild; do not edit the packaged binary or attempt display-name substitutions. The CI contract validates every explicit ID against the pinned Kiln registry.

The **tagged GitHub Release** publishes `endless-chests-kiln-0.6.3.zip`, standalone `endless-chests.catalogue.json` (reference), and `endless-chests.defaults.ini` (settings template). **Neither JSON nor INI is a Kiln-installable mod.**

The active `chests.json` file (typically in `kiln/data/endless-chests-kiln/`) stores existing manual chest bindings and settings:

```json
{
  "enabled": true,
  "autoNames": true,
  "clearOnRename": true,
  "autoEmpty": true,
  "recheckSeconds": 4,
  "positionToleranceMeters": 0.75,
  "chests": []
}
```

**Do not overwrite your existing `chests.json` with this example:** doing so would erase your manual binding list. Instead, edit individual options and use `echest reload` to apply them.

- `autoNames=false`: disable the automatic game-name system (manual commands still work)
- `clearOnRename=false`: recognised renames preserve existing items while changing the refill selection
- `autoEmpty=false`: stop *repeated* Empty clears; with `clearOnRename=true`, a new rename to Empty still performs one clear

Gear level/rarity/perk upgrades and gem-level special items from ShroudForge are not implemented through normal Kiln Items transactions. Magic chest and dedicated server behaviours require further tests.

## Installation

1. **Back up the world**, especially chests with valuables.
2. Use a compatible Kiln runtime (this repo pins published Kiln `v0.2.0-dev.15`).
3. From a successful [Endless Chests packaging run](https://github.com/bisdat/kiln-mods/actions/workflows/endless-chests.yml), download the `endless-chests-kiln-package` artifact, or use the [tagged GitHub Releases](https://github.com/bisdat/kiln-mods/releases) page for the individual ZIP and JSON catalogue and INI settings assets.
4. Extract the Actions artifact (if downloaded) and import the enclosed **`endless-chests-kiln-0.6.3.zip`** into Kiln's Library. Enable v0.6.3 in the active profile and restart Enshrouded. The accompanying JSON and settings INI are separate reference downloads, **not** an installable Kiln package.
5. Try **`TOOLS → starter blocks → My Storage → Empty`** on a throwaway chest. The unrecognised name must **not** delete the blocks; Empty **must** clear them without confirmation.

## Kiln console commands (optional)

In a locally hosted solo world, Kiln processes `kiln/control/console/*.cmd` after a character enters the game. The nearest-chest commands require your character within **4 metres** of a compatible chest. A missing command result may mean the game hasn't processed the file yet.

| Command | Effect |
|---|---|
| `echest nameprobe` | Read-only: report nearest chest's ECS name fingerprint, recognised canonical name and manual binding |
| `echest status` | Show settings, service readiness and group/name-map counts |
| `echest groups [search]` | Search canonical group labels |
| `echest bind Tools` | Manually bind an **empty** nearby chest to Tools, until unbound or renamed in-game |
| `echest unbind` | Remove manual binding without deleting contents |
| `echest empty` / `echest bind Empty` | **Legacy manual operation:** preview a clear without performing it |
| `echest empty confirm` | Confirm the manual preview within 60 seconds |
| `echest refill` | Request the next scan |
| `echest reload` | Reload chest bindings, canonical embedded JSON and settings INI |

**Automatic game-UI `Empty` does not require commands.** The separate manual `echest empty` command retains preview/confirmation.

Run a read-only probe from a terminal **in the Enshrouded game folder**:

```bash
mkdir -p kiln/control/console
printf 'echest nameprobe\n' > kiln/control/console/probe.tmp
mv kiln/control/console/probe.tmp kiln/control/console/probe.cmd
# After the game processes it:
cat kiln/control/console/probe.result.json
```

## CI and development

The GitHub Actions workflow validates all 230 groups and 5,232 entry positions, verifies all 3,481 item IDs against the pinned Kiln SDK and independently classifies all 489 weapons and 58 equipable shields into the ten dedicated groups. It also verifies that all 195 original groups and 2,934 previous identities remain exactly unchanged, checks the 100 v0.5.1 repairs, final 27 reconciliations and 11 explicit variant sets, and tests Mycelium, Indigo, Basalt, Grasshopper, Scarab, Torch, Oven, loot armour and historical fixes. It also tests INI settings, 32 Empty case variants and game-name FNV fingerprints. Release assets include the compiled mod, catalogue JSON and settings INI.

Local Linux build:

```bash
KILN_UPSTREAM_SHA=eba9a5e1e0e0044a9146fdd53021a751e2957030 \
KILN_SDK_VERSION=0.2.0-dev.local.20261107.61500 \
bash scripts/build.sh EndlessChests
```

Output: `bin/Release/endless-chests-kiln-0.6.3.zip`. After merging this change into `main`, publishing tag **`endless-chests-v0.6.3`** triggers all three release assets automatically. New weapon-group in-game acceptance is tracked in [issue #45](https://github.com/bisdat/kiln-mods/issues/45). Catalogue resolution issue #38 was closed after v0.5.2 acceptance; other gameplay parity checks remain in [issue #4](https://github.com/bisdat/kiln-mods/issues/4).
