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
assert data["schemaVersion"] == 1
assert data["kilnSdkCommit"] == "eba9a5e1e0e0044a9146fdd53021a751e2957030"

registry = {}
for line in SDK.read_text(encoding="utf-8").splitlines():
    record = line.split("\t")
    if len(record) >= 4 and record[0].isdigit():
        id_, debug, display, category = record[:4]
        registry[int(id_)] = (debug, display, category)

items, groups = data["items"], data["groups"]
assert len(groups) == 195
assert len(items) == 2812
assert len(groups) == len(set(name.casefold() for name in groups)), "case-colliding groups"

for raw_id, item in items.items():
    assert re.fullmatch(r"[1-9][0-9]*", raw_id), raw_id
    item_id = int(raw_id)
    assert item_id in registry, raw_id
    expected = (item["debugName"], item["displayName"], item["category"])
    actual = registry[item_id]
    assert expected[0] == actual[0] and expected[1].casefold() == actual[1].casefold()
    assert expected[2] == actual[2], (raw_id, expected, actual)

total = resolved = unresolved = 0
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
            assert set(entry).issubset({"unresolved", "candidates"})
            assert isinstance(entry.get("unresolved"), str) and entry["unresolved"]
            if "candidates" in entry:
                assert isinstance(entry["candidates"], list)
                assert all(isinstance(x, int) and x in registry for x in entry["candidates"])
            unresolved += 1
assert total == 3464, total
assert resolved == 3337, resolved
assert unresolved == 127, unresolved
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

# All 133 old '<name> Loot' references now point to their unique _Loot
# identities. Their game display names do not have the word 'Loot'.
loot = sum(
    entry.endswith("_Loot")
    for group in groups.values()
    for id_ in group
    if isinstance(id_, int)
    for entry in [items[str(id_)]["debugName"]]
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
    f"{resolved} identity-verified references ({len(items)} distinct IDs), "
    f"{unresolved} explicit unresolved; Mycelium, Indigo, Basalt, "
    "Grasshopper, Scarab, Torch, Oven, Loot and previous fixes pinned"
)
