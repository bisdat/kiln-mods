#!/usr/bin/env python3
"""Add a read-only craft payment/UI observation endpoint to disposable Kiln main source.

No gameplay or inventory semantics are changed. The changes attach to existing
kiln.rules detours, not new hooks. Requires an EXACT known upstream commit.
"""
from pathlib import Path
import os
import subprocess
import sys

# Historical dev snapshot remains the local default; CI always sets the newest
# PUBLISHED release commit and then checks it against the checkout.\nPIN = os.environ.get("KILN_UPSTREAM_SHA", "77e525888ac0d5f4f6b16103e945ed7ac049b50e")
if len(sys.argv) != 2:
    raise SystemExit("Usage: native.py <disposable-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
actual = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
if actual != PIN:
    raise SystemExit(f"Refusing upstream revision {actual}; expected {PIN}")

def edit(path, before, after):
    file = root / path
    source = file.read_text(encoding="utf8")
    if source.count(before) != 1:
        raise RuntimeError(f"Upstream anchor missing or ambiguous in {path}: {before[:100]!r}")
    file.write_text(source.replace(before, after, 1), encoding="utf8")
    print("PATCH", path)

h = "native/core/abi/services/kiln_rules.h"
edit(h, "typedef struct KilnRulesApi {",
"""/* Research-only craft trace of EXISTING kiln.rules hooks. Read-only observations.
 * This deliberately does not expose raw payment costs, slots or tx pointers.
 * Last samples are approximate across worker threads, not an atomic tuple. */
typedef struct KilnRulesCraftTrace {
    uint32_t size;
    uint32_t reserved;
    uint64_t primary_calls;
    uint64_t alternative_calls;
    uint64_t ui_calls;
    uint32_t last_primary_owner;
    uint32_t last_primary_result;
    uint32_t last_alternative_owner;
    uint32_t last_alternative_result;
    uint32_t last_ui_recipe;
    uint32_t last_ui_vanilla_amount;
    uint32_t last_ui_cap;
    uint32_t flags;
} KilnRulesCraftTrace;

typedef struct KilnRulesApi {""")
edit(h,
"""    KilnResult(KILN_CALL* creative)(KilnRulesCreative* out);
} KilnRulesApi;""",
"""    KilnResult(KILN_CALL* creative)(KilnRulesCreative* out);
    /* Optional tail. Clients check header.size. No new hooks or mutations. */
    KilnResult(KILN_CALL* craft_trace)(KilnRulesCraftTrace* out);
} KilnRulesApi;""")

hh = "native/services/rules/creative.h"
edit(hh,
"""void pay_hook_failed();
""",
"""void pay_hook_failed();
// Passive observers of original payer results; only validated recipe callsites count.
void trace_primary_payment(uintptr_t ret, uint32_t owner, uint32_t native_result);
void reset_craft_trace();
KilnResult KILN_CALL craft_trace(KilnRulesCraftTrace* out);
""")

cpp = "native/services/rules/creative.cpp"
edit(cpp, "std::atomic<uint32_t> g_hooks{0};",
"""std::atomic<uint32_t> g_hooks{0};
// Statistics collected from the existing client/server hooks only. All are
// detached scalars; never hold the game transaction, cost list, or UI pointers.
struct CraftTraceStats {
    std::atomic<uint64_t> primary{0}, alternative{0}, ui{0};
    std::atomic<uint32_t> primary_owner{0}, primary_result{0};
    std::atomic<uint32_t> alternative_owner{0}, alternative_result{0};
    std::atomic<uint32_t> ui_recipe{0}, ui_vanilla{0}, ui_cap{0};
} CT;
""")
edit(cpp,
"""    return h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
}""",
"""    const auto ret = reinterpret_cast<uintptr_t>(_ReturnAddress());
    const uint32_t result = h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
    if (g_recipe_ret && ret == g_recipe_ret + 7) {
        CT.alternative.fetch_add(1, std::memory_order_relaxed);
        CT.alternative_owner.store(owner, std::memory_order_relaxed);
        CT.alternative_result.store(result, std::memory_order_relaxed);
    }
    return result;
}""")
edit(cpp,
"""    const uint32_t n = h_craftable.call<uint32_t>(ui, recipe, cap, selections);
    if (idle()) return n;""",
"""    const uint32_t n = h_craftable.call<uint32_t>(ui, recipe, cap, selections);
    CT.ui.fetch_add(1, std::memory_order_relaxed);
    CT.ui_recipe.store(recipe, std::memory_order_relaxed);
    CT.ui_vanilla.store(n, std::memory_order_relaxed);
    CT.ui_cap.store(cap & 0xffffu, std::memory_order_relaxed);
    if (idle()) return n;""")
edit(cpp,
"""void pay_hook_failed() {""",
"""void trace_primary_payment(uintptr_t ret, uint32_t owner, uint32_t native_result) {
    if (!g_recipe_ret || ret != g_recipe_ret) return;
    CT.primary.fetch_add(1, std::memory_order_relaxed);
    CT.primary_owner.store(owner, std::memory_order_relaxed);
    CT.primary_result.store(native_result, std::memory_order_relaxed);
}

void reset_craft_trace() {
    CT.primary.store(0, std::memory_order_relaxed);
    CT.alternative.store(0, std::memory_order_relaxed);
    CT.ui.store(0, std::memory_order_relaxed);
    CT.primary_owner.store(0, std::memory_order_relaxed);
    CT.primary_result.store(0, std::memory_order_relaxed);
    CT.alternative_owner.store(0, std::memory_order_relaxed);
    CT.alternative_result.store(0, std::memory_order_relaxed);
    CT.ui_recipe.store(0, std::memory_order_relaxed);
    CT.ui_vanilla.store(0, std::memory_order_relaxed);
    CT.ui_cap.store(0, std::memory_order_relaxed);
}

KilnResult KILN_CALL craft_trace(KilnRulesCraftTrace* out) {
    if (!out || out->size < sizeof(KilnRulesCraftTrace)) return KILN_ERR_INVALID_ARGUMENT;
    KilnRulesCraftTrace v{};
    v.size = sizeof v;
    v.primary_calls = CT.primary.load(std::memory_order_relaxed);
    v.alternative_calls = CT.alternative.load(std::memory_order_relaxed);
    v.ui_calls = CT.ui.load(std::memory_order_relaxed);
    v.last_primary_owner = CT.primary_owner.load(std::memory_order_relaxed);
    v.last_primary_result = CT.primary_result.load(std::memory_order_relaxed);
    v.last_alternative_owner = CT.alternative_owner.load(std::memory_order_relaxed);
    v.last_alternative_result = CT.alternative_result.load(std::memory_order_relaxed);
    v.last_ui_recipe = CT.ui_recipe.load(std::memory_order_relaxed);
    v.last_ui_vanilla_amount = CT.ui_vanilla.load(std::memory_order_relaxed);
    v.last_ui_cap = CT.ui_cap.load(std::memory_order_relaxed);
    // This is a pointer-free value snapshot, not a recipe bill or source resolver.
    v.flags = g_hooks.load(std::memory_order_relaxed);
    *out = v;
    return KILN_OK;
}

void pay_hook_failed() {""")

rules = "native/services/rules/rules.cpp"
edit(rules,
"""    if (kiln_rules_creative::idle()) return h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);""",
"""    if (kiln_rules_creative::idle()) {
        const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
        kiln_rules_creative::trace_primary_payment(reinterpret_cast<uintptr_t>(_ReturnAddress()), owner, result);
        return result;
    }""")
edit(rules,
"""    if (kiln_rules_creative::pay_free(reinterpret_cast<uintptr_t>(_ReturnAddress()), tx, owner)) return 0;
    return h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);""",
"""    if (kiln_rules_creative::pay_free(reinterpret_cast<uintptr_t>(_ReturnAddress()), tx, owner)) return 0;
    const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
    kiln_rules_creative::trace_primary_payment(reinterpret_cast<uintptr_t>(_ReturnAddress()), owner, result);
    return result;""")
edit(rules,
"""    if (ev->event == KILN_WORLD_STARTED) arm_payments();""",
"""    if (ev->event == KILN_WORLD_STARTED) {
        kiln_rules_creative::reset_craft_trace();
        arm_payments();
    }""")
edit(rules,
"""    &kiln_rules_creative::map_reveal, &kiln_rules_creative::creative_status,
};""",
"""    &kiln_rules_creative::map_reveal, &kiln_rules_creative::creative_status,
    &kiln_rules_creative::craft_trace,
};""")
print("PASS: existing native payer/UI detours now expose read-only counters; game payments unchanged.")
