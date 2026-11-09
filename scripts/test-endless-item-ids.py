#!/usr/bin/env python3
"""Regression for Endless Chests issue #16 against the pinned Kiln item catalogue.

Run after scripts/build.sh EndlessChests, which checks out the exact SDK SHA
to .kiln-upstream. This is a source/catalogue contract test; the build checks
C# compilation and the player still verifies inventory behaviour in-game.
"""
from configparser import ConfigParser
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
registry_path = root / ".kiln-upstream/managed/services/Kiln.Items/Data/items.tsv"
mod_path = root / "EndlessChests/IniCatalog.cs"
ini_path = root / "EndlessChests/defaults/endless-chests.defaults.ini"
assert registry_path.is_file(), "Build the pinned Kiln SDK before this test"

entries = {}
for line in registry_path.read_text(encoding="utf-8").splitlines():
    fields = line.split("\t")
    if len(fields) < 4 or not fields[0].isdigit():
        continue
    ident, debug, display, _category = fields[:4]
    if display.casefold() == "rough wood block":
        entries[int(ident)] = debug

expected = {
    444874319: "quest_reward_icon_chest_silver",
    1441646853: "quest_reward_icon_chest_fog",
    1923097402: "quest_reward_icon_chest_gold",
    2012474258: "Block_T1_Wood_Rough_CRAFTING",
    2243637327: "quest_reward_icon_chest_wood",
}
assert entries == expected, f"pinned SDK Rough Wood Block matches changed: {entries}"
correct = 2012474258

source = mod_path.read_text(encoding="utf-8")
assert re.search(r"\bRoughWoodBlockId\s*=\s*2012474258u\s*;", source)
assert 'RoughWoodBlockDebugName = "Block_T1_Wood_Rough_CRAFTING"' in source
assert 'Items.Get(new ItemId(RoughWoodBlockId))' in source, "must resolve by ID, not display name"
assert 'string.Equals(matched.Name, RoughWoodBlockDebugName' in source, "missing fail-closed identity guard"
assert 'ItemInfo? info = LookupItem(name);' in source, "group and single-item paths must use the pin"
assert 'name.Equals("Rough Wood Block", StringComparison.OrdinalIgnoreCase)' in source
assert 'new List<string>()' in source and "ambiguous.Add(" in source, "missing duplicate diagnostics"

config = ConfigParser(interpolation=None)
config.optionxform = str
config.read(ini_path, encoding="utf-8-sig")
groups = {
    k: [item.strip() for item in value.split("|") if item.strip()]
    for k, value in config.items("Groups")
}
assert len(groups) == 195, "default catalogue groups must be preserved"
assert sum(len(items) for items in groups.values()) == 3464, "default catalogue entries changed"
assert groups["Starter Blocks"] == [
    "Rough Stone Block", "Rough Wood Block", "Rough Flintstone Block"
]
for group in ("Starter Blocks", "Springlands Blocks", "Wood Blocks"):
    assert groups[group].count("Rough Wood Block") == 1, f"missing default entry: {group}"

print(
    f"PASS: verified item {correct} in pinned Kiln SDK; "
    "4 duplicate quest icons excluded by ID guard; "
    "3 original groups preserved; one-item path covered"
)
