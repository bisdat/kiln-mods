#!/usr/bin/env python3
"""Validate every shipped group entry against the workflow-pinned Kiln SDK.

The JSON catalogue is the only source for item identities. Unresolved entries
are explicit and never treated as display names by the game runtime.
"""
from collections import Counter
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]
CAT = ROOT / "EndlessChests/defaults/catalogue.json"
SDK = ROOT / ".kiln-upstream/managed/services/Kiln.Items/Data/items.tsv"
RESOLVER = ROOT / "EndlessChests/ItemCatalogue.cs"
MOD = ROOT / "EndlessChests/EndlessChestsMod.cs"
assert SDK.is_file(), "Build the workflow-pinned Kiln SDK before validation"
data = json.loads(CAT.read_text(encoding="utf-8"))
assert data["schemaVersion"] == 2
assert data["kilnSdkCommit"] == "eba9a5e1e0e0044a9146fdd53021a751e2957030"

registry = {}
for line in SDK.read_text(encoding="utf-8").splitlines():
    record = line.split("\t")
    if len(record) >= 4 and record[0].isdigit():
        id_, debug, display, category = record[:4]
        registry[int(id_)] = (debug, display, category)

items, groups = data["items"], data["groups"]
assert len(groups) == 207
assert len(items) == 3481
assert len(groups) == len(set(name.casefold() for name in groups)), "case-colliding groups"

for raw_id, item in items.items():
    assert re.fullmatch(r"[1-9][0-9]*", raw_id), raw_id
    item_id = int(raw_id)
    assert item_id in registry, raw_id
    expected = (item["debugName"], item["displayName"], item["category"])
    actual = registry[item_id]
    assert expected[0] == actual[0] and expected[1].casefold() == actual[1].casefold()
    assert expected[2] == actual[2], (raw_id, expected, actual)

total = resolved = unresolved = variants = variant_refs = 0
used = set()
for group, members in groups.items():
    assert isinstance(group, str) and group and isinstance(members, list) and members
    assert len(members) <= 512
    for entry in members:
        total += 1
        if isinstance(entry, int) and not isinstance(entry, bool):
            assert str(entry) in items, (group, entry)
            used.add(str(entry))
            resolved += 1
        else:
            assert isinstance(entry, dict), (group, entry)
            if "variants" in entry:
                assert set(entry) == {"variants"}, (group, entry)
                refs = entry["variants"]
                assert isinstance(refs, list) and 2 <= len(refs) <= 64
                assert all(type(x) is int and str(x) in items for x in refs), (group, entry)
                assert len(set(refs)) == len(refs), (group, entry)
                variants += 1
                variant_refs += len(refs)
                used.update(str(x) for x in refs)
                resolved += 1
            else:
                assert set(entry).issubset({"unresolved", "candidates"})
                assert isinstance(entry.get("unresolved"), str) and entry["unresolved"]
                if "candidates" in entry:
                    assert isinstance(entry["candidates"], list)
                    assert all(isinstance(x, int) and x in registry for x in entry["candidates"])
                unresolved += 1
assert total == 4103, total
assert resolved == 4103, resolved
assert unresolved == 0, unresolved
assert variants == 11 and variant_refs == 39, (variants, variant_refs)
assert all(
    sum(len(e["variants"]) if isinstance(e, dict) and "variants" in e else 1 for e in members) <= 72
    for name, members in groups.items() if name != "Blocks"
), "Group exceeds maximum 72 Magic Chest slots"
# Blocks is deliberately the full union; Blocks 2 is its 72-slot overflow.
block_ids = list(dict.fromkeys(
    item_id for name, members in groups.items() if name.endswith(" Blocks")
    for entry in members for item_id in ([entry] if type(entry) is int else entry["variants"])
))
assert len(block_ids) == 82
assert groups["Blocks"] == block_ids
assert groups["Blocks 2"] == block_ids[72:]
assert len(groups["Blocks 2"]) == 10
assert set(items) == used, "Unreferenced item definitions or missing IDs"

def contains(group, item_id):
    return item_id in groups[group]

# Historical fixes must stay pinned to their game identities.
assert contains("Starter Blocks", 2012474258)  # Rough Wood Block
assert contains("Songs", 1165213726)  # Mirage song, not staff
assert not contains("Songs", 1992044990)
assert groups["Alchemist Wall Hanging"] == [2365197861, 1372134498]
assert contains("Crafting G-O", 4276529065)  # Indigo material, not flower pot
assert not contains("Crafting G-O", 3965963935)
assert contains("Flower Pots 2", 3965963935)
assert not contains("Flower Pots 2", 4276529065)

# Mycelium is a distinct crafting material versus overgrowth BuildTools.
assert contains("Crafting G-O", 3509012331)
assert not contains("Crafting G-O", 1353762644)
assert contains("Overgrowth Materials", 1353762644)
assert registry[3509012331] == (
    "Material_T1_FogTerrain_Mycelium", "Mycelium", "Materials")
assert registry[1353762644] == (
    "Material_T3_Overgrowth_Mycelium", "Mycelium", "BuildTools")

assert contains("Terrain Materials", 2851443310)  # Basalt unchanged
assert contains("Handheld Torches", 3474096535)  # Actual torch, not test prop
assert not contains("Handheld Torches", 440761375)
for group in ("Cooking Stations", "Production Places"):
    assert contains(group, 3397817290)  # Current Oven
    assert not contains(group, 3359917734)  # Deprecated
assert contains("Fishing Bait", 2342031255)  # Grasshopper
assert contains("Fishing Bait", 700318057)  # Scarab
assert registry[2342031255][2] == registry[700318057][2] == "Ammunition"

# Reconciled legacy IDs: verify semantics and numerical selection.
assert contains("Magic Chests", 4044650321) # 24-slot magic storage
assert contains("Magic Chests", 30818586) # 72-slot magic storage
assert contains("Flower Pots 1", 4272163256) # tiny pink flower pot
assert contains("Flower Pots 1", 3643820464) # long yellow wall flower pot
assert contains("Flower Pots 2", 523366717) # small monstera plant pot
assert contains("Cemetery", 1653941305) # tombstone inscription (quoted display)
assert contains("Mannequin", 2681848943) # Back Off pose (quoted display)
assert contains("Wall Decorations 1", 1688806746) # Wolf's Choir (quoted display)
assert contains("Queen Pikemead Statue", 3883219916) # collectible piece I
assert contains("Queen Pikemead Statue", 1556757497) # collectible piece II
assert contains("Queen Pikemead Statue", 2624605842) # collectible piece III
assert contains("Wall Tapestry", 3603348812) # tapestry piece I
assert contains("Wall Tapestry", 2252200136) # tapestry piece II
assert contains("Throwables", 1462961786) # fire grenade, not test ball
assert not contains("Throwables", 1036114942)
assert groups["Treasure Maps"][0] == 2381061644 # lore map at original unresolved position
assert groups["Treasure Maps"][1] == 3491672504 # separate valid decorative treasure map
assert contains("Vanity Outfits 1", 3203382450) # real Royal Gloves, not NO_ placeholder

# Final 27 catalogue slots now point to current IDs or explicit variant sets.
assert contains("Backpacks", 3728213390)  # Current craftable Small Backpack
assert contains("Backpacks", 3773368910)  # Current craftable Medium Backpack
assert contains("Backpacks", 996296787)   # Current craftable Large Backpack
assert 4231879756 not in groups["Backpacks"] # Old Small Backpack with no current recipe
assert contains("Flower Pots 1", 673405648) # distinct unsized CNY Fern pot
assert contains("Miscellaneous 2", 1313835688) # SDK spells Resource<two spaces>Bundle I
assert registry[1313835688] == (
    "Prop_Decoration_T1_RessourceBundle_01", "Resource  Bundle I", "BuildTools")
assert any(isinstance(e, dict) and len(e.get("variants", [])) == 14 for e in groups["Books"])


# All 133 old '<name> Loot' references now point to their unique _Loot
# identities. Their game display names do not have the word 'Loot'.
loot = sum(
    items[str(id_)]["debugName"].endswith("_Loot")
    for members in groups.values()
    for slot in members
    for id_ in (slot["variants"] if isinstance(slot, dict) and "variants" in slot else
                [slot] if isinstance(slot, int) else [])
)
assert loot >= 133, loot

source = RESOLVER.read_text(encoding="utf-8")
runtime = MOD.read_text(encoding="utf-8")
assert 'EmbeddedText("EndlessChests.CatalogueJson")' in source
assert 'Items.Get(new ItemId(id))' in source
assert 'string.Equals(item.Name, identity.DebugName, StringComparison.Ordinal)' in source
assert 'string.Equals(item.Category.ToString(), identity.Category, StringComparison.Ordinal)' in source
assert 'case "groups":' in source and "IgnoredLegacyGroups++;" in source
assert "Items.Lookup(" not in source, "Unverified display-name resolution is forbidden"
assert "LegacyUnnumberedVariantIds" not in source
assert "LegacyWallHangingIds" not in source
assert "ItemCatalogue.LoadOrCreate(_catalogFile)" in runtime
assert 'if (result.Status == ItemsStatus.NoInventory) break;' in runtime
assert "continue;" in runtime.split('if (!result.Succeeded)', 1)[-1]

print(
    f"PASS: {len(groups)} canonical groups, {total} positions, "
    f"{resolved} resolved catalogue positions ({len(items)} distinct IDs, "
    f"{variants} multi-variant groups / {variant_refs} variant IDs), "
    f"{unresolved} explicit unresolved; Mycelium, Indigo, Basalt, "
    "Grasshopper, Scarab, Torch, Oven, Loot, 489 weapons, 58 shields and previous fixes pinned"
)
