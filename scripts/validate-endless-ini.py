#!/usr/bin/env python3
"""Validate the shipped Endless Chests catalogue against the user-provided defaults.

These counts are intentional: a truncated or accidentally replaced INI must
fail CI instead of shipping a reduced item/group selection.
"""
from configparser import ConfigParser
from pathlib import Path

source = Path(__file__).resolve().parents[1] / "EndlessChests/defaults/endless-chests.defaults.ini"
config = ConfigParser(interpolation=None, strict=True)
config.optionxform = str
with source.open(encoding="utf-8-sig") as f:
    config.read_file(f)

groups = {k: [x.strip() for x in v.split("|") if x.strip()] for k, v in config.items("Groups")}
aliases = dict(config.items("Aliases"))
disabled = {k for k, v in config.items("DisabledItems") if v.strip() == "1"}
entries = sum(len(v) for v in groups.values())

# Every active key is ordered alphabetically within its own section.
# Original item lists on the right-hand side retain their exact order, and
# symbolic group names (such as $$) are placed after alphabetic names.
def entry_sort_key(name: str) -> tuple[int, str, str]:
    return (0 if name[0].isalnum() else 1, name.casefold(), name)


for section in config.sections():
    keys = list(config.options(section))
    assert keys == sorted(keys, key=entry_sort_key), (
        f"[{section}] keys must be sorted alphabetically within the section"
    )

assert len(groups) == 195, f"expected 195 groups; found {len(groups)}"
assert entries == 3464, f"expected 3464 group entries; found {entries}"
assert len(aliases) == 3, f"expected 3 aliases; found {len(aliases)}"
assert len(disabled) == 45, f"expected 45 default disabled items; found {len(disabled)}"
assert groups["Starter Blocks"] == [
    "Rough Stone Block", "Rough Wood Block", "Rough Flintstone Block"
]
assert groups["Tools"][0] == "Iron Pickaxe"
assert aliases["Elixir"] == "Mysterious Flask"
assert aliases["Diver’s Endurance Flask"] == "Diver's Endurance Flask"
assert "Empty" not in groups, "Empty must remain a special confirmed clear action"
assert config.getboolean("Options", "AllowMagicChests") is True
assert all(x for names in groups.values() for x in names)
print(f"PASS: {len(groups)} groups, {entries} group entries, {len(aliases)} aliases, {len(disabled)} disabled items")
