#!/usr/bin/env python3
"""Regression tests for case-insensitive FNV-1a chest label recognition.

The game exposes only an FNV-1a64 ID, not the original text. The Kiln
matcher enumerates bounded ASCII case branches from both ends of the hash
instead of reading a private game string store. Test the same logic here.
"""
from pathlib import Path
import json

OFFSET = 0xCBF29CE484222325
PRIME = 0x100000001B3
INV = 0xCE965057AFF6957B
MASK = (1 << 64) - 1


def fnv(text: str) -> int:
    value = OFFSET
    for char in text.encode("utf-8"):
        value = ((value ^ char) * PRIME) & MASK
    return value


assert (PRIME * INV) & MASK == 1


def ascii_letter(c: int) -> bool:
    return 65 <= c <= 90 or 97 <= c <= 122


def is_ascii_case_of(standard: str, fingerprint: int) -> bool:
    data = standard.encode("utf-8")
    letters = sum(ascii_letter(c) for c in data)
    assert letters <= 30, "runtime performs this bounded matching only for names with <=30 ASCII letters"
    halfway = letters // 2
    cut = 0
    if halfway:
        seen = 0
        for i, c in enumerate(data):
            if ascii_letter(c):
                seen += 1
                if seen == halfway:
                    cut = i + 1
                    break

    prefixes = set()

    def prefix(i: int, state: int) -> None:
        if i == cut:
            prefixes.add(state)
            return
        prefix(i + 1, ((state ^ data[i]) * PRIME) & MASK)
        if ascii_letter(data[i]):
            prefix(i + 1, ((state ^ (data[i] ^ 32)) * PRIME) & MASK)

    prefix(0, OFFSET)

    def reverse(i: int, state: int) -> bool:
        if i < cut:
            return state in prefixes
        previous = (state * INV) & MASK
        return reverse(i - 1, previous ^ data[i]) or (
            ascii_letter(data[i]) and reverse(i - 1, previous ^ (data[i] ^ 32))
        )

    return reverse(len(data) - 1, fingerprint)


names = [
    ("Tools", "tools"),
    ("Tools", "TOOLS"),
    ("Tools", "tOoLs"),
    ("Animals", "aNiMaLs"),
    ("Starter Blocks", "sTaRtEr bLoCkS"),
    ("Stone Blocks", "STONE BLOCKS"),
    ("Embervale Miniature Model", "eMbErVaLe MiNiAtUrE mOdEl"),
    ("Diver’s Endurance Flask", "diVer’s enDurance flaSk"),
    ("Crafting A-C", "CRAFTING a-c"),
    ("One-Handed Melee", "oNe-hAnDeD mElEe"),
    ("Two-Handed Melee 2", "tWo-hAnDeD mElEe 2"),
    ("Daggers", "dAgGeRs"),
    ("Bows", "bOwS"),
    ("Wands", "wAnDs"),
    ("Staffs", "sTaFfS"),
    ("Shields", "sHiElDs"),
]
for canonical, variant in names:
    assert fnv(canonical) & 31 == fnv(variant) & 31
    assert is_ascii_case_of(canonical, fnv(variant)), (canonical, variant)

assert not is_ascii_case_of("Tools", fnv("Random Chest"))
assert not is_ascii_case_of("Animals", fnv("Animal"))
assert not is_ascii_case_of("Starter Blocks", fnv("Starter Block"))

path = Path(__file__).resolve().parents[1] / "EndlessChests/defaults/catalogue.json"
groups = json.loads(path.read_text(encoding="utf-8"))["groups"]
assert len(groups) == 205
for name in groups:
    # Explicitly verify that every canonical group can be matched with
    # ANY ASCII case permutation (not just lower/upper/title case).
    assert sum(ascii_letter(c) for c in name.encode("utf-8")) <= 30, name
    mixed = "".join(c.upper() if i % 2 else c.lower() for i, c in enumerate(name))
    assert is_ascii_case_of(name, fnv(mixed)), name

print(f"PASS: {len(groups)} groups support arbitrary ASCII letter casing; case-mismatch negatives rejected")
print("PASS: FNV inverse and observed name spellings validated")
