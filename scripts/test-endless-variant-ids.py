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

assert len(variants) == 88, f"Expected 88 disambiguated items, got {len(variants)}"
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
assert 'string.Equals(item.DisplayName, label, StringComparison.OrdinalIgnoreCase)' in source
assert 'ItemInfo? info = LookupItem(name);' in source
assert 'return Items.Lookup(name);' in source, "ordinary names must still work"
assert "RoughWoodBlockId" in source, "issue #16 fix must remain intact"

# The 22 unnumbered vanity entries from the pre-0.4.4 user INI must
# continue resolving without modifying that saved user file.
legacy = dict(
    (label, int(item_id))
    for label, item_id in re.findall(r'\["([^"]+ Variant)"\] = (\d+)u,', source)
)
assert len(legacy) == 22, f"expected 22 legacy vanity identities, got {len(legacy)}"
assert "LegacyUnnumberedVariantIds.TryGetValue(name, out var legacyId)" in source
assert 'name.EndsWith(bareMarker, StringComparison.OrdinalIgnoreCase)' in source
assert 'GetVerifiedVariant(name[..^bareMarker.Length], legacyId, regularVanity: true)' in source
assert 'item.Name.StartsWith("Vanity_", StringComparison.OrdinalIgnoreCase)' in source
assert 'item.Name.Contains("TwitchDrop", StringComparison.OrdinalIgnoreCase)' in source
assert ("Flame Pact Gloves Variant" in legacy
        and legacy["Flame Pact Gloves Variant"] == 3829626055)
assert ("Hunter Boots Variant" in legacy
        and legacy["Hunter Boots Variant"] == 1965220207)

for label, item_id in legacy.items():
    plain_label = label.removesuffix(" Variant")
    assert any(group.startswith("Vanity Outfits ") and display == plain_label
               and numeric_id == item_id
               for group, display, numeric_id, _debug, _category in variants), (
        f"legacy name {label} must have the same explicit ID in the shipped INI"
    )
    debug, display, category = items[item_id]
    assert category == "Equipment" and display.casefold() == plain_label.casefold()
    assert debug.startswith("Vanity_") and "twitchdrop" not in debug.casefold(), (
        f"legacy variant must resolve to the regular vanity identity: {label}"
    )
assert not any(
    entry.endswith(" Variant")
    for names in groups.values()
    for entry in names
), "shipped defaults must not contain unnumbered Variant suffixes"

print("PASS: 88 valid Variant <ID> entries (66 original numbered + 22 repaired) "
      "in 195 original groups; Mirage song sheet not weapon; "
      "22 legacy unnumbered labels resolve to audited non-Twitch vanity IDs")
