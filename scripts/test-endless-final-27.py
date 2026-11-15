#!/usr/bin/env python3
"""Regress all 27 final identity repairs against the official Kiln item table.

Keeps 195 named groups and 3464 logical positions. Some positions now denote
several distinct game objects using schema-v2 explicit {"variants": [ids]}.
None of these identities is inferred from a display name at runtime.
"""
from pathlib import Path
from collections import Counter
import json

root = Path(__file__).resolve().parents[1]
catalogue = json.loads((root / "EndlessChests/defaults/catalogue.json").read_text(encoding="utf-8"))
previous = json.loads((root / "scripts/data/endless-chests-identity-audit-0.5.1.json").read_text(encoding="utf-8"))
audit = json.loads((root / "scripts/data/endless-chests-identity-audit-0.5.2.json").read_text(encoding="utf-8"))
sdk = root / ".kiln-upstream/managed/services/Kiln.Items/Data/items.tsv"
runtime = (root / "EndlessChests/ItemCatalogue.cs").read_text(encoding="utf-8")
assert sdk.is_file(), "Build the official or pinned Kiln SDK first"

registry = {}
for row in sdk.read_text(encoding="utf-8").splitlines():
    parts = row.split("\t")
    if len(parts) >= 4 and parts[0].isdigit():
        registry[int(parts[0])] = parts[1:4]

assert catalogue["schemaVersion"] == 2
assert audit["schemaVersion"] == 1
assert audit["sourceCatalogueSchemaVersion"] == 1
assert audit["resultCatalogueSchemaVersion"] == 2
assert audit["sourceKilnRelease"] == "v0.2.0-dev.28"
assert audit["sdkCommit"] == "2301cc5fd3138aa91119e8bc0f082569218315cd"
assert audit["oldUnresolved"] == len(previous["remaining"]) == 27
assert audit["remainingUnresolved"] == 0
assert len(audit["mappings"]) == 27

old = {(x["group"], x["position"]): x for x in previous["remaining"]}
assert len(old) == 27
seen = set()
variant_sets = variant_refs = 0
for fix in audit["mappings"]:
    key = fix["group"], fix["position"]
    assert key in old and key not in seen, fix
    seen.add(key)
    assert old[key]["unresolved"] == fix["previousLabel"]
    original_candidates = set(old[key].get("candidates", []))
    slot = catalogue["groups"][key[0]][key[1]]
    ids = [slot] if isinstance(slot, int) else slot["variants"]
    assert ids == [x["id"] for x in fix["selected"]], fix
    assert len(ids) == len(set(ids)), fix
    if isinstance(slot, dict):
        assert set(slot) == {"variants"} and 2 <= len(ids) <= 64
        variant_sets += 1
        variant_refs += len(ids)
    assert fix["reason"] in {
        "exact-debug-name-or-fingerprint",
        "current-craftable-backpack",
        "decoration-not-production",
        "placeable-fence-not-fixed-variant",
        "normal-non-prefixed-decoration",
        "all-distinct-registered-forms",
    }
    for selected in fix["selected"]:
        item_id = selected["id"]
        record = registry[item_id]
        assert (selected["debugName"], selected["displayName"], selected["category"]) == tuple(record)
        identity = catalogue["items"][str(item_id)]
        assert (identity["debugName"], identity["displayName"], identity["category"]) == tuple(record)
        if fix["reason"] != "exact-debug-name-or-fingerprint":
            assert item_id in original_candidates, (key, item_id)
    # Already available physical variants were not duplicated in the group.
    group_ids = [
        id_ for value in catalogue["groups"][key[0]]
        for id_ in ([value] if isinstance(value, int) else value.get("variants", []))
    ]
    assert all(id_ in group_ids for id_ in fix["alreadyAvailable"])

assert seen == set(old)
assert variant_sets == 11 and variant_refs == 39, (variant_sets, variant_refs)
assert not any(isinstance(v, dict) and "unresolved" in v
               for entries in catalogue["groups"].values() for v in entries)

by_group = {(x["group"], x["previousLabel"]): x for x in audit["mappings"]}
backpacks = catalogue["groups"]["Backpacks"]
assert backpacks[1:4] == [3728213390, 3773368910, 996296787]
for old_id in (4231879756, 4053516652, 3131144042):
    assert old_id not in backpacks
book = by_group[("Books", "Book I")]
assert len(book["selected"]) == 14
assert all(x["debugName"].startswith("Prop_Decoration_Book_") for x in book["selected"])
fern = by_group[("Flower Pots 1", "Fern Pot")]
assert [x["debugName"] for x in fern["selected"]] == ["Empty_Flower_Pot_CNY_Fern_01"]
bundle = by_group[("Miscellaneous 2", "Resource Bundle I")]
assert bundle["selected"] == [{
    "id": 1313835688,
    "debugName": "Prop_Decoration_T1_RessourceBundle_01",
    "displayName": "Resource  Bundle I",
    "category": "BuildTools"
}], bundle
assert all(not x["debugName"].startswith("Prop_Production_Farmbed")
           for x in by_group[("Decorations 3", "Empty Farmbed")]["selected"])

# Structural C# parsing must expand every variant ID into a separately
# validated GroupEntry; no runtime display-name aliases or guessing.
assert 'value.TryGetProperty("variants", out var variants)' in runtime
assert 'entries.Add(new GroupEntry(id, null));' in runtime
assert 'catalogue._identities.ContainsKey(id)' in runtime
assert 'variants.GetArrayLength() is < 2 or > 64' in runtime
assert "Items.Lookup(" not in runtime

print(
    f"PASS: all 27 previously unresolved positions mapped; "
    f"{variant_sets} explicit variant sets / {variant_refs} item IDs; "
    "craftable backpacks, 14 book models, distinct Fern and double-spaced "
    "Resource Bundle I verified against the Kiln catalogue"
)
