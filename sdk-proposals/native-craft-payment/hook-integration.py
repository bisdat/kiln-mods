#!/usr/bin/env python3
"""Wire a proof-gated payment bridge into BOTH existing released Kiln Rules hooks.

Requires an EXACT published Kiln source checkout and a separate lab build.
Does NOT add hooks, register a source resolver or turn on remote deductions.
The inserted hook calls pass resolver=nullptr until authoritative native
ingredient, access and rollback providers are implemented and verified.
"""
from pathlib import Path
import os
import re
import shutil
import subprocess
import sys

if len(sys.argv) != 2:
    raise SystemExit("Usage: hook-integration.py <disposable-released-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
repo = Path(__file__).resolve().parents[2]
sha = os.environ.get("KILN_UPSTREAM_SHA")
if not sha or not re.fullmatch("[0-9a-f]{40}", sha):
    raise SystemExit("KILN_UPSTREAM_SHA must be a verified immutable 40-hex commit")
actual = subprocess.check_output(
    ["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
if actual != sha:
    raise SystemExit(f"Refusing mismatched Kiln source {actual}, expected {sha}")
if not (root / "native/services/rules/rules.cpp").is_file():
    raise SystemExit("Missing published native Kiln Rules source")

def edit(path: str, old: str, new: str):
    target = root / path
    source = target.read_text(encoding="utf-8")
    if source.count(old) != 1:
        raise RuntimeError(f"Unexpected native hook anchor {path}: {old[:100]!r}, got {source.count(old)}")
    target.write_text(source.replace(old, new), encoding="utf-8")
    print("PATCH", path)

destination = root / "native/services/rules/cbpay"
destination.mkdir(parents=True, exist_ok=True)
src = repo / "sdk-proposals/native-craft-payment"
for name in ("payment_plan.h", "native_take_adapter.h",
             "recipe_payment_bridge.h", "engine_hook.h"):
    shutil.copyfile(src / name, destination / name)
    print("COPIED isolated native header", name)

# Leave ALL installed hooks and original creative-mode semantics in place.
# The bridge dispatcher is real compiled code but the provider is NULL:
# it cannot issue a payment or a new native game read on its own.
edit("native/services/rules/rules.cpp",
     '#include "creative.h"',
     '#include "creative.h"\n#include "cbpay/engine_hook.h"')
edit("native/services/rules/creative.cpp",
     '#include "creative.h"',
     '#include "creative.h"\n#include "cbpay/engine_hook.h"')

edit("native/services/rules/rules.cpp",
"""    if (kiln_rules_creative::pay_free(reinterpret_cast<uintptr_t>(_ReturnAddress()), tx, owner)) return 0;
    return h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);""",
"""    if (kiln_rules_creative::pay_free(reinterpret_cast<uintptr_t>(_ReturnAddress()), tx, owner)) return 0;
    // Game-native recipe payer path. The trusted resolver is intentionally
    // absent until transaction-time selected costs, Magic/base grants and
    // original RecipeApply rollback are VERIFIED. No new detour.
    const auto bridge = cbpay::engine::dispatch(
        {tx, costs, owner, multiplier, extra,
         reinterpret_cast<uintptr_t>(_ReturnAddress()), cbpay::engine::Payer::InventoryPay},
        nullptr);
    if (bridge.action != cbpay::RecipePaymentAction::DelegateVanilla)
        return bridge.original_payer_result;
    return h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);""")

edit("native/services/rules/creative.cpp",
"""    return h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
}""",
"""    // Alternate selected-input payer. Uses exactly the already-installed
    // Kiln Rules hook and original caller; no competing hook or free craft.
    const auto bridge = cbpay::engine::dispatch(
        {tx, costs, owner, multiplier, extra,
         reinterpret_cast<uintptr_t>(_ReturnAddress()), cbpay::engine::Payer::RecipePayAlt},
        nullptr);
    if (bridge.action != cbpay::RecipePaymentAction::DelegateVanilla)
        return bridge.original_payer_result;
    return h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
}""")
# Source checks: the ONLY dispatcher endpoints are the two recipe payers,
# and there is no registration/activation of an unverified native provider.
primary = (root / "native/services/rules/rules.cpp").read_text()
alternate = (root / "native/services/rules/creative.cpp").read_text()
assert primary.count("cbpay::engine::dispatch(") == 1
assert alternate.count("cbpay::engine::dispatch(") == 1
assert "cbpay::engine::Payer::InventoryPay" in primary
assert "cbpay::engine::Payer::RecipePayAlt" in alternate
assert "nullptr);" in primary and "nullptr);" in alternate
assert "return h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);" in primary
assert "return h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);" in alternate
print("PASS: exact released hooks are wired to native payment, resolver OFF; original payers untouched.")
