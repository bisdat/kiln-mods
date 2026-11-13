#!/usr/bin/env python3
"""Validate all original 'Name Variant <game item ID>' entries against pinned Kiln.

Run after scripts/build.sh EndlessChests, which populates .kiln-upstream from the
workflow-pinned SDK commit. This checks the catalogue contract; C# compilation
is tested by the build step and live in-game behaviour still needs acceptance.
"""
from configparser import ConfigParser
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
registry = root / ".kiln-upstream/managed/services/Kiln.Items/Data/items.tsv"
ini = root / "EndlessChests/defaults/endless-chests.defaults.ini"
resolver = root / "EndlessChests/IniCatalog.cs"

assert registry.is_file(), "Build the pinned Kiln SDK before this test"
items = {}
for line in registry.read_text(encoding="utf-8").splitlines():
    fields = line.split("\t")
    if len(fields) >= 4 and fields[0].isdigit():
        item_id, debug, display, category = fields[:4]
        items[int(item_id)] = (debug, display, category)

config = ConfigParser(interpolation=None)
config.optionxform = str
config.read(ini, encoding="utf-8-sig")
groups = {key: [value.strip() for value in values.split("|")]
          for key, values in config.items("Groups")}
assert len(groups) == 195
assert sum(len(members) for members in groups.values()) == 3464

variants = []
for group, names in groups.items():
    for name in names:
        match = re.fullmatch(r"(.+?) Variant ([0-9]+)", name, re.IGNORECASE)
        if match is None:
            continue
        label, raw_id = match.groups()
        item_id = int(raw_id)
        assert item_id <= 0xFFFFFFFF, f"Invalid unsigned ID in {group}: {name}"
        assert item_id in items, f"Unrecognised game ID in {group}: {name}"
        debug, display, category = items[item_id]
        assert display.casefold() == label.casefold(), (
            f"Wrong item identity in {group}: {name}, game says {display!r}"
        )
        variants.append((group, label, item_id, debug, category))

assert len(variants) == 66, f"Expected 66 disambiguated items, got {len(variants)}"
assert ("Songs", "Mirage", 1165213726, "8kLoreText_scroll_bard_song_12",
        "Collectibles") in variants, "Mirage sheet not mapped correctly"
assert items[1992044990] == ("Weapon_T4_2H_StaffEpic_01", "Mirage", "Weapons")
assert all(not (label == "Mirage" and item_id == 1992044990)
           for _, label, item_id, _, _ in variants), "Mirage weapon substituted"

source = resolver.read_text(encoding="utf-8")
assert 'const string variantMarker = " Variant ";' in source
assert 'name.LastIndexOf(variantMarker, StringComparison.OrdinalIgnoreCase)' in source
assert 'uint.TryParse(idText, System.Globalization.NumberStyles.None' in source
assert 'Items.Get(new ItemId(id))' in source
assert 'string.Equals(matched.DisplayName, label, StringComparison.OrdinalIgnoreCase)' in source
assert 'ItemInfo? info = LookupItem(name);' in source
assert 'return Items.Lookup(name);' in source, "ordinary names must still work"
assert "RoughWoodBlockId" in source, "issue #16 fix must remain intact"

print("PASS: 66 valid Variant <ID> entries in 195 original groups, "
      "Mirage collectible selected over same-named weapon, "
      "verified ID/display guard, and original catalogue preserved")
