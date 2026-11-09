#!/usr/bin/env python3
"""Validate the exact *published* Kiln source shape before native integration.

This is a source-level gate only. It does not install/compile a patched Kiln
runtime or prove transaction/permission semantics in a running game.
"""
import json
from pathlib import Path
import subprocess
import sys

if len(sys.argv) != 2:
    raise SystemExit("Usage: check-upstream.py <released-kiln-source>")
root = Path(sys.argv[1])
sha = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
info = json.loads(Path(".kiln-release.json").read_text(encoding="utf-8"))
if sha != info.get("release_target"):
    raise SystemExit(f"CHECK FAILED: SDK checkout {sha} != published release target {info.get('release_target')}")

patterns = {
    "native/services/rules/creative.cpp": [
        "uint32_t __fastcall hk_alt(void* tx, int64_t* costs, uint32_t owner, uint32_t multiplier, void* extra)",
        "return h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);",
        "reinterpret_cast<uintptr_t>(_ReturnAddress()) == g_recipe_ret + 7",
        'sym("rules/RecipePaid", &recipe_ret)',
    ],
    "native/services/rules/rules.cpp": [
        "uint32_t __fastcall hk_pay(void* tx, int64_t* costs, uint32_t owner, uint32_t multiplier, void* extra)",
        "if (kiln_rules_creative::pay_free(reinterpret_cast<uintptr_t>(_ReturnAddress()), tx, owner)) return 0;",
    ],
    "native/services/items/engine.h": [
        "using RemoveItemsFn = Result12*(__fastcall*)",
        "bool access_from_tx(void* tx, Access* a);",
    ],
    "native/services/items/engine.cpp": [
        "E.remove_items(&out, tx, r.entity, r.item, r.slot, r.count);",
        "const bool commit = out.error == 0 && missing == 0 && !off_slot && !dry;",
        "if (commit) E.commit(tx);\n    else E.rollback(tx);",
    ],
    "docs/re/items.md": [
        "preferredSlot == 0",
        "op 1 per slot",
    ],
}
for path, required in patterns.items():
    data = (root / path).read_text(encoding="utf-8")
    for fragment in required:
        if data.count(fragment) != 1:
            raise SystemExit(f"CHECK FAILED: {path}: expected unique anchor {fragment!r}; got {data.count(fragment)}")
    print(f"PASS: original payer/native inventory anchors in {path}")

print(f"PASS: released Kiln {info['tag']} ({sha}) retains both original recipe payment hooks and removal/rollback seam.")
print("No claim of authorised remote sources, live staging, client craftability, or end-to-end crafting.")
