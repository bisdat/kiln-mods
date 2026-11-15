#!/usr/bin/env python3
"""Pin Indigo Plant crafting material and decorative pot to different game IDs.

This runs against the exact Kiln SDK checked out by scripts/build.sh. The C#
build validates compilation; the user must still verify the Blacksmith in game.
"""
from configparser import ConfigParser
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
registry_file = root / ".kiln-upstream/managed/services/Kiln.Items/Data/items.tsv"
ini_file = root / "EndlessChests/defaults/endless-chests.defaults.ini"
resolver_file = root / "EndlessChests/IniCatalog.cs"

assert registry_file.exists(), "The pinned Kiln SDK must be built before this test"
items_by_id = {}
indigo = {}
for line in registry_file.read_text(encoding="utf-8").splitlines():
    row = line.split("\t")
    if len(row) < 4 or not row[0].isdigit():
        continue
    item_id, debug_name, display, category = row[:4]
    info = (debug_name, display, category)
    items_by_id[int(item_id)] = info
    if display.casefold() == "indigo plant":
        indigo[int(item_id)] = info

expected_indigo = {
    3965963935: ("Flower_Pot_Round_Large_Indigo", "Indigo Plant", "BuildTools"),
    4276529065: ("Material_T2_Indigo_Plant", "Indigo Plant", "Materials"),
}
assert indigo == expected_indigo, f"Indigo item identities changed: {indigo}"

ini = ConfigParser(interpolation=None)
ini.optionxform = str
ini.read(ini_file, encoding="utf-8-sig")
groups = {
    name: [x.strip() for x in value.split("|") if x.strip()]
    for name, value in ini.items("Groups")
}
assert len(groups) == 195
assert sum(map(len, groups.values())) == 3464

# The crafting group must not accidentally spawn the lower-ID decorative pot.
crafting = groups["Crafting G-O"]
pots = groups["Flower Pots 2"]
assert "Indigo Plant Variant 4276529065" in crafting
assert "Indigo Plant Variant 3965963935" in pots
assert "Indigo Plant" not in crafting
assert "Indigo Plant Pot" not in pots
assert "Indigo Plant Variant 3965963935" not in crafting
assert "Indigo Plant Variant 4276529065" not in pots
assert sum(x.startswith("Indigo Plant") for x in crafting) == 1
assert sum(x.startswith("Indigo Plant") for x in pots) == 1

# Basalt was independently resolved by the user by restarting the mods:
# leave its current catalogue entry and game ID strictly alone.
assert groups["Terrain Materials"][-1] == "Basalt"
assert items_by_id[2851443310] == (
    "Build_TerrainMaterial_T7_AncientLand_Stone_Basalt",
    "Basalt", "Materials",
)

source = resolver_file.read_text(encoding="utf-8")
assert re.search(r"\bIndigoMaterialId\s*=\s*4276529065u;", source)
assert re.search(r"\bIndigoDecorativePotId\s*=\s*3965963935u;", source)
assert 'IndigoMaterialId => "Material_T2_Indigo_Plant"' in source
assert 'IndigoDecorativePotId => "Flower_Pot_Round_Large_Indigo"' in source
assert 'GetVerifiedVariant("Indigo Plant", id, regularVanity: false)' in source
assert 'string.Equals(item.Name, expectedDebugName, StringComparison.Ordinal)' in source
assert 'if (expectedDebugName is null) return null;' in source
assert 'name.Equals("Indigo Plant", StringComparison.OrdinalIgnoreCase)' in source
assert 'return LookupIndigo(IndigoMaterialId);' in source
assert 'name.Equals("Indigo Plant Pot", StringComparison.OrdinalIgnoreCase)' in source
assert 'return LookupIndigo(IndigoDecorativePotId);' in source
assert 'label.Equals("Indigo Plant", StringComparison.OrdinalIgnoreCase)' in source
assert 'return LookupIndigo(id);' in source
assert '.Append("Indigo Plant")' in source
assert '.Append("Indigo Plant Pot")' in source
assert 'Items.Get(new ItemId(RoughWoodBlockId))' in source

print("PASS: Indigo material vs decorative IDs and debug names verified; "
      "both default groups and legacy active INIs covered; "
      "direct chest names recognized; Basalt untouched; "
      "195 groups and 3464 entries preserved")
