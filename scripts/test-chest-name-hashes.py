#!/usr/bin/env python3
"""Check the real host-world chest-name IDs before packaging the automatic name matcher.

Three user measurements on one chest (2026-11-07) show that Enshrouded's
CustomPlayerString is FNV-1a64 of the exact UTF-8 chest name.
"""
from collections import Counter
from configparser import ConfigParser
from pathlib import Path
import json

OFFSET_BASIS = 0xCBF29CE484222325
PRIME = 0x100000001B3
MASK = (1 << 64) - 1


def fnv1a64(text: str) -> int:
    value = OFFSET_BASIS
    for byte in text.encode("utf-8"):
        value = ((value ^ byte) * PRIME) & MASK
    return value


assert fnv1a64("Starter Blocks") == 0x35F105E7ECC53E32
assert fnv1a64("Tools") == 0xC26501E3ADE7DA3C
assert fnv1a64("Empty") != 0x35F105E7ECC53E32
assert fnv1a64("Empty") != 0xC26501E3ADE7DA3C

root = Path(__file__).resolve().parents[1]
catalogue = json.loads((root / "EndlessChests/defaults/catalogue.json").read_text(encoding="utf-8"))
parser = ConfigParser(interpolation=None)
parser.optionxform = str
parser.read(root / "EndlessChests/defaults/endless-chests.defaults.ini", encoding="utf-8-sig")
identities = catalogue["items"]
name_counts = Counter(item["displayName"].casefold() for item in identities.values())
unique_labels = {
    item["displayName"] for item in identities.values()
    if name_counts[item["displayName"].casefold()] == 1
}
all_names = (set(catalogue["groups"]) | set(parser.options("Aliases")) |
             set(identities) | unique_labels | {"Empty"})

by_hash = {}
for name in all_names:
    key = fnv1a64(name)
    if key in by_hash and by_hash[key] != name:
        raise AssertionError(f"FNV collision: {name!r} vs {by_hash[key]!r}")
    by_hash[key] = name

assert by_hash[0x35F105E7ECC53E32] == "Starter Blocks"
assert by_hash[0xC26501E3ADE7DA3C] == "Tools"
assert by_hash[fnv1a64("Empty")] == "Empty"

# The destructive Empty command accepts any capitalization, without accepting
# a fingerprint that matches an original item/group/alias by accident.
empty_variants = {
    "".join(c.upper() if mask & (1 << i) else c for i, c in enumerate("empty"))
    for mask in range(1 << 5)
}
empty_hashes = {fnv1a64(name) for name in empty_variants}
assert len(empty_hashes) == 32
assert all(h not in by_hash or by_hash[h] == "Empty" for h in empty_hashes)
assert fnv1a64("empty") in empty_hashes
assert fnv1a64("EMPTY") in empty_hashes
print("PASS: all 32 Empty case variants are distinct from named catalogue selections")
print(f"PASS: observed game chest IDs match FNV-1a64; {len(all_names)} exact names with no collisions")
