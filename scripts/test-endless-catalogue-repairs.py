#!/usr/bin/env python3
"""Check the 100 audited catalogue repairs and the 27 deliberately unresolved labels.

Do not choose a candidate by numeric sort order or a generic fuzzy display-name
match: the audit captures the exact accepted internal game identity for each
former catalogue label and the pinned SDK is the authority.
"""
from pathlib import Path
from collections import Counter
import json

root = Path(__file__).resolve().parents[1]
audit = json.loads((root / "scripts/data/endless-chests-identity-audit-0.5.1.json").read_text(encoding="utf-8"))
catalogue = json.loads((root / "EndlessChests/defaults/catalogue.json").read_text(encoding="utf-8"))
final_audit = json.loads((root / "scripts/data/endless-chests-identity-audit-0.5.2.json").read_text(encoding="utf-8"))
final_by_position = {
    (x["group"], x["position"]): x for x in final_audit["mappings"]
}
sdk = root / ".kiln-upstream/managed/services/Kiln.Items/Data/items.tsv"
assert sdk.exists(), "Build against the pinned Kiln SDK before testing"

assert audit["schemaVersion"] == 1
assert audit["previousUnresolved"] == 127
assert audit["pinnedKilnSdkCommit"] == catalogue["kilnSdkCommit"]
assert len(audit["resolutions"]) == 100
assert len(audit["remaining"]) == 27

registry = {}
for line in sdk.read_text(encoding="utf-8").splitlines():
    fields = line.split("\t")
    if len(fields) >= 4 and fields[0].isdigit():
        registry[int(fields[0])] = tuple(fields[1:4])

seen = set()
for entry in audit["resolutions"]:
    group, index, old, item_id = (
        entry["group"], entry["position"], entry["formerLabel"], entry["id"]
    )
    key = (group, index)
    assert key not in seen and isinstance(old, str) and old, entry
    seen.add(key)
    assert isinstance(item_id, int) and catalogue["groups"][group][index] == item_id, entry
    identity = catalogue["items"][str(item_id)]
    assert (entry["debugName"], entry["displayName"], entry["category"]) == (
        identity["debugName"], identity["displayName"], identity["category"]
    ), entry
    expected = registry[item_id]
    assert expected[0] == entry["debugName"], entry
    assert expected[1].casefold() == entry["displayName"].casefold(), entry
    assert expected[2] == entry["category"], entry
    assert entry["evidence"] in {
        "curated", "color-size-form", "fern-size-form", "grass-size-form",
        "named-pot", "magic-chest-slots", "chest-slots", "quoted-display",
        "numbered-piece"
    }, entry

# The 27 entries left by the old audit must be covered, not deleted,
# by the subsequent 0.5.2 catalogue repair. All original positions remain.
for entry in audit["remaining"]:
    key = entry["group"], entry["position"]
    assert key not in seen and key in final_by_position, key
    seen.add(key)
    mapping = final_by_position[key]
    assert mapping["previousLabel"] == entry["unresolved"], (key, mapping)
    slot = catalogue["groups"][key[0]][key[1]]
    ids = [slot] if isinstance(slot, int) else slot.get("variants", [])
    assert ids == [item["id"] for item in mapping["selected"]], (key, slot)
    assert ids and all(id_ in registry for id_ in ids), key

assert len(seen) == 127
counts = Counter(
    entry["evidence"] for entry in audit["resolutions"]
)
assert counts["magic-chest-slots"] == 5
assert counts["chest-slots"] == 4
assert counts["quoted-display"] >= 20
assert sum(counts[k] for k in ("color-size-form", "fern-size-form", "grass-size-form")) == 34
assert counts["named-pot"] == 11
assert counts["numbered-piece"] == 5
assert not any(
    isinstance(item, dict) and "unresolved" in item
    for group in catalogue["groups"].values() for item in group
)

print("PASS: 100 original 0.5.1 identity repairs still pinned, "
      "27 subsequent fixes validated against original audit positions")
