#!/usr/bin/env python3
"""Check all six weapon classes plus equipable shields against Kiln item IDs.

Use weapon debug identities, not ambiguous display names. Retain every
existing v0.5.2 group and item verbatim, and partition complete weapon
classes into separate <=72-slot Magic Chest groups.
"""
from collections import defaultdict
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]
CATALOGUE = ROOT / "EndlessChests/defaults/catalogue.json"
SDK = ROOT / ".kiln-upstream/managed/services/Kiln.Items/Data/items.tsv"
assert SDK.is_file(), "Build the pinned Kiln SDK before validating weapons"
data = json.loads(CATALOGUE.read_text(encoding="utf-8"))
groups = data["groups"]
items = data["items"]

assert data["schemaVersion"] == 2
assert data["kilnSdkCommit"] == "eba9a5e1e0e0044a9146fdd53021a751e2957030"

ORDER = (
    "One-Handed Melee",
    "One-Handed Melee 2",
    "One-Handed Melee 3",
    "Two-Handed Melee",
    "Two-Handed Melee 2",
    "Daggers",
    "Bows",
    "Wands",
    "Staffs",
    "Shields",
)
PARTS = {
    "One-Handed Melee": 3,
    "Two-Handed Melee": 2,
    "Daggers": 1,
    "Bows": 1,
    "Wands": 1,
    "Staffs": 1,
}
COUNTS = {
    "One-Handed Melee": 153,
    "Two-Handed Melee": 107,
    "Daggers": 60,
    "Bows": 62,
    "Wands": 58,
    "Staffs": 49,
}
PATTERN = re.compile(
    r"(?:Craft_)?Weapon_(T(?:[0-9]+|X)(?:-TX)?)_(1H|2H)_(.+)"
)

def fnv_codepoints(lines):
    """Portable fingerprint of the original catalogue's ordered Unicode text."""
    h = 0xCBF29CE484222325
    for line in lines:
        for char in line:
            h ^= ord(char)
            h = h * 0x100000001B3 & ((1 << 64) - 1)
    return f"{h:016x}"

def slot_text(value):
    if type(value) is int:
        return str(value)
    assert set(value) == {"variants"} and len(value["variants"]) >= 2
    return ",".join(map(str, value["variants"]))

assert len(groups) == 231 and len(items) == 3481
assert sum(len(x) for x in groups.values()) == 5255
assert tuple(list(groups)[195:205]) == ORDER, "Append ten class groups after the original groups"

assert fnv_codepoints(
    name + "\t" + "|".join(map(slot_text, entries)) + "\n"
    for name, entries in list(groups.items())[:207]
) == "a2290908d57e623a", "The 207 pre-consolidation groups changed"

original_groups = list(groups.items())[:195]
assert sum(map(lambda g: len(g[1]), original_groups)) == 3464
assert fnv_codepoints(
    name + "\t" + "|".join(map(slot_text, entries)) + "\n"
    for name, entries in original_groups
) == "3d513abf06d105ac", "The 195 accepted pre-weapon groups changed"

registered = {}
typed = defaultdict(list)
excluded = []
for line in SDK.read_text(encoding="utf-8").splitlines():
    fields = line.split("\t")
    if len(fields) < 4 or not fields[0].isdigit():
        continue
    id_, debug, display, category = fields[:4]
    item_id = int(id_)
    registered[item_id] = (debug, display, category)
    if category != "Weapons":
        continue
    m = PATTERN.fullmatch(debug)
    if not m:
        excluded.append(item_id)
        continue
    tier, hand, detail = m.groups()
    if "dagger" in detail.lower():
        group = "Daggers"
    elif "bow" in detail.lower():
        group = "Bows"
    elif "wand" in detail.lower():
        group = "Wands"
    elif "staff" in detail.lower():
        group = "Staffs"
    else:
        group = "One-Handed Melee" if hand == "1H" else "Two-Handed Melee"
    typed[group].append((tier, debug, item_id))

assert len(excluded) == 21, "Do not treat torches, throwables or test objects as handheld melee/magic weapons"
assert sum(len(values) for values in typed.values()) == 489
assert {k: len(v) for k, v in typed.items()} == COUNTS

def tier_order(tier):
    if tier == "TX":
        return (99, 0)
    match = re.fullmatch(r"T([0-9]+)(-TX)?", tier)
    assert match, tier
    return (int(match.group(1)), 1 if match.group(2) else 0)

expected_groups = {}
all_weapon_ids = set()
for cls, part_count in PARTS.items():
    entries = sorted(typed[cls], key=lambda x: (tier_order(x[0]), x[1], x[2]))
    chunk_size = (len(entries) + part_count - 1) // part_count
    for index in range(part_count):
        name = cls if index == 0 else f"{cls} {index + 1}"
        chunk = entries[index * chunk_size: (index + 1) * chunk_size]
        expected_ids = [item_id for _, _, item_id in chunk]
        assert 1 <= len(expected_ids) <= 72, (name, len(expected_ids))
        assert groups[name] == expected_ids, f"Wrong ID set/order for {name}"
        assert len(expected_ids) == len(set(expected_ids)), f"Repeated weapon ID in {name}"
        assert not (all_weapon_ids & set(expected_ids)), f"Cross-group duplicated IDs in {name}"
        assert sum(c.isalpha() and c.isascii() for c in name) <= 30, name
        all_weapon_ids.update(expected_ids)
        expected_groups[name] = len(expected_ids)

assert len(all_weapon_ids) == 489
assert not (set(excluded) & all_weapon_ids)

# Shields live under SDK category "Equipment", not "Weapons". Only
# equippable Shield_Tx and Craft_Shield_Tx identities qualify; decorative
# wall mounts and defensive spells must not be treated as equipable shields.
SHIELD = re.compile(r"(?:Craft_)?Shield_(T(?:[0-9]+|X)(?:-TX)?)_(.+)")
shield_rows = []
decorative_or_spell = []
for item_id, (debug, display, category) in registered.items():
    if "shield" not in debug.lower() and "shield" not in display.lower():
        continue
    match = SHIELD.fullmatch(debug)
    if category == "Equipment" and match:
        shield_rows.append((match.group(1), debug, item_id))
    else:
        decorative_or_spell.append(item_id)
assert len(shield_rows) == 58, len(shield_rows)
assert len(decorative_or_spell) == 7, decorative_or_spell
shield_rows.sort(key=lambda x: (tier_order(x[0]), x[1], x[2]))
expected_shields = [item_id for _, _, item_id in shield_rows]
assert groups["Shields"] == expected_shields
assert len(expected_shields) == len(set(expected_shields)) == 58
assert set(expected_shields).isdisjoint(all_weapon_ids)
assert not set(expected_shields).intersection(decorative_or_spell)
for item_id in expected_shields:
    ident = items[str(item_id)]
    assert (ident["debugName"], ident["displayName"], ident["category"]) == registered[item_id]
    assert ident["category"] == "Equipment"
expected_groups["Shields"] = len(expected_shields)

for weapon_id in all_weapon_ids:
    assert str(weapon_id) in items
    ident = items[str(weapon_id)]
    assert (ident["debugName"], ident["displayName"], ident["category"]) == registered[weapon_id]
    assert ident["category"] == "Weapons"

new_ids = all_weapon_ids | set(expected_shields)
original_items = [(id_, identity) for id_, identity in items.items() if int(id_) not in new_ids]
assert len(original_items) == 2934
assert fnv_codepoints(
    f'{id_}\t{identity["debugName"]}\t{identity["displayName"]}\t{identity["category"]}\n'
    for id_, identity in original_items
) == "bcf1cc9827d4bd72", "A verified pre-weapon item identity changed"

# Every new entry must be a classified weapon or equippable shield; legacy
# entries may refer to other categories. All seven classes are disjoint and complete.
assert set(items) == {str(id_) for id_ in registered if str(id_) in items}
assert not any(isinstance(entry, dict) and "unresolved" in entry
               for entries in groups.values() for entry in entries)

print(f"PASS: six weapon classes / nine groups plus Shields; {len(all_weapon_ids)} "
      f"weapons + {len(expected_shields)} equipable shields, all verified; "
      f"{len(excluded)} unrelated Weapons and {len(decorative_or_spell)} decorative/spell shields excluded")
print("New chest groups: " + ", ".join(f"{name}={count}" for name, count in expected_groups.items()))
print("PASS: all original 195 groups / 3464 positions and 2934 identities unchanged")
