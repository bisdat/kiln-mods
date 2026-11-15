#!/usr/bin/env python3
"""Validate the user-editable settings-only INI for Endless Chests v0.5."""
from configparser import ConfigParser
from pathlib import Path
import json

root = Path(__file__).resolve().parents[1]
cfg_path = root / "EndlessChests/defaults/endless-chests.defaults.ini"
catalogue = json.loads((root / "EndlessChests/defaults/catalogue.json").read_text(encoding="utf-8"))

config = ConfigParser(interpolation=None, strict=True)
config.optionxform = str
config.read(cfg_path, encoding="utf-8-sig")
assert set(config.sections()) == {
    "Options", "DisabledGroups", "DisabledItems", "Aliases"
}, "INI now contains settings only; [Groups] belongs in catalogue.json"
assert config.getboolean("Options", "AllowMagicChests") is True
assert config.getboolean("Options", "EnableGemLevels") is False
assert config.getboolean("Options", "FullyUpgradeGear") is False
assert not config.items("DisabledGroups")
assert not config.items("DisabledItems")
assert set(dict(config.items("Aliases"))) == {
    "Elixir", "Shroud Meteor Shower"
}, "Do not ship aliases pointing to unavailable old display names"

names = set(catalogue["groups"])
identity_names = {entry["displayName"] for entry in catalogue["items"].values()}
for alias, selection in config.items("Aliases"):
    assert alias != selection
    assert selection in names or selection in identity_names, (alias, selection)

print("PASS: settings-only INI, 2 valid aliases, numerical exclusions, "
      "canonical groups are JSON-owned")
