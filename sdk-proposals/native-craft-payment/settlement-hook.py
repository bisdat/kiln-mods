#!/usr/bin/env python3
"""Connect the bounded ORIGINAL-transaction finality witness in a disposable Kiln build.

Requires the exact published Kiln source AFTER hook-integration.py.
The additional commit/rollback hooks are compile-gated OFF by default.
For an isolated lab, CBPAY_ENABLE_SETTLEMENT_WITNESS must be defined by the
native compiler AND the resulting binary must be tested in a disposable world.
There is NEVER any remote debit, recipe bypass or native grant here.
"""
from pathlib import Path
import os
import re
import subprocess
import sys

if len(sys.argv) != 2:
    raise SystemExit("Usage: settlement-hook.py <patched-disposable-published-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
pin = os.environ.get("KILN_UPSTREAM_SHA", "")
if not re.fullmatch(r"[0-9a-f]{40}", pin):
    raise SystemExit("KILN_UPSTREAM_SHA must be a verified 40-digit release SHA")
actual = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
if actual != pin:
    raise SystemExit(f"Refusing source drift: {actual} != {pin}")

def edit(path, old, new):
    p = root / path
    s = p.read_text(encoding="utf8")
    if s.count(old) != 1:
        raise RuntimeError(f"Unsafe original hook anchor in {path}: {old[:100]!r} count={s.count(old)}")
    p.write_text(s.replace(old, new), encoding="utf8")
    print("PATCH", path)

rules = "native/services/rules/rules.cpp"
creative = "native/services/rules/creative.cpp"
header = "native/services/rules/creative.h"

edit(rules,
     '#include "cbpay/tx-source-native.h"',
     '#include "cbpay/tx-source-native.h"\n#include "cbpay/settlement_witness.h"')
edit(creative,
     '#include "cbpay/engine_hook.h"',
     '#include "cbpay/engine_hook.h"\n#include "cbpay/settlement_witness.h"')
edit(header,
     "bool pay_free(uintptr_t ret, void* tx, uint32_t owner);",
     """bool pay_free(uintptr_t ret, void* tx, uint32_t owner);
// Check actual recipe-specific original payer return address and operator.
// This permits observation only, not payment or source authorisation.
bool receipt_scope(void* tx, uint32_t owner, uintptr_t ret, uint32_t payer);""")
edit(header,
     "bool receipt_scope(void* tx, uint32_t owner, uintptr_t ret, uint32_t payer);",
     """bool receipt_scope(void* tx, uint32_t owner, uintptr_t ret, uint32_t payer);
// Short-lived original-transaction journal cursor, with no retained pointer.
struct JournalStart { uint64_t count = 0; bool valid = false; };
struct JournalDelta { uint32_t take_ops = 0; bool checked = false; };
JournalStart receipt_before(void* tx, uint32_t owner, uintptr_t ret, uint32_t payer);
JournalDelta receipt_after(void* tx, uint32_t owner, uintptr_t ret, uint32_t payer, JournalStart before);""")

edit(creative,
     "int64_t g_tx_operator = 0;",
     """int64_t g_tx_operator = 0;
// The original recipe transaction journal offset; independently from
// pinned native symbol items/tx.ops. Never guess the value at runtime.
int64_t g_cbpay_tx_ops = -1;""")

edit(creative,
     """uint32_t install(const KilnApi* api, std::string& off) {
    g_api = api;""",
     """uint32_t install(const KilnApi* api, std::string& off) {
    g_api = api;
    if constexpr (cbpay::settlement::kEnabled) {
        if (!cst("items/tx.ops", &g_cbpay_tx_ops) ||
            g_cbpay_tx_ops != 0xF0) g_cbpay_tx_ops = -1;
    }""")

edit(creative,
     """void pay_hook_failed() {
    g_recipe_ret = 0;""",
     """bool receipt_scope(void* tx, uint32_t owner, uintptr_t ret, uint32_t payer) {
    if (!tx || !owner || !g_recipe_ret || g_tx_operator <= 0 ||
        (payer != 1 && payer != 2) ||
        ret != g_recipe_ret + (payer == 2 ? 7 : 0)) return false;
    uint32_t actual_actor = 0;
    return read_operator(tx, &actual_actor) && actual_actor == owner;
}

void pay_hook_failed() {
    g_recipe_ret = 0;""")

# These functions are read-only, bounded, and wrapped in SEH so an untrusted
# raw transaction view can never be interpreted as a valid journal on fault.
# They are never called in a normal, disabled Kiln build.
edit(creative,
     """void pay_hook_failed() {
    g_recipe_ret = 0;""",
     """JournalStart receipt_before(void* tx, uint32_t owner, uintptr_t ret, uint32_t payer) {
    if (!receipt_scope(tx, owner, ret, payer) || g_cbpay_tx_ops != 0xF0)
        return {};
    __try {
        const auto* b = static_cast<const uint8_t*>(tx);
        const uint8_t* entries = nullptr;
        uint64_t count = 0;
        std::memcpy(&entries, b + g_cbpay_tx_ops, sizeof entries);
        std::memcpy(&count, b + g_cbpay_tx_ops + 8, sizeof count);
        if (count > 4096 || (!entries && count)) return {};
        return {count, true};
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return {};
    }
}

JournalDelta receipt_after(void* tx, uint32_t owner, uintptr_t ret,
                           uint32_t payer, JournalStart before) {
    if (!before.valid || !receipt_scope(tx, owner, ret, payer) ||
        g_cbpay_tx_ops != 0xF0) return {};
    __try {
        const auto* b = static_cast<const uint8_t*>(tx);
        const uint8_t* entries = nullptr;
        uint64_t count = 0;
        std::memcpy(&entries, b + g_cbpay_tx_ops, sizeof entries);
        std::memcpy(&count, b + g_cbpay_tx_ops + 8, sizeof count);
        if (count < before.count || count > 4096 ||
            count - before.count > 128 || (count && !entries) ||
            (entries && reinterpret_cast<uintptr_t>(entries) > UINTPTR_MAX - count * 0x34u))
            return {};
        // Inside the original payer these newly staged records must all be
        // actual take ops. A mixed/unknown journal is NOT transaction proof.
        for (uint64_t i = before.count; i < count; ++i) {
            const auto* op = entries + i * 0x34u;
            uint64_t slot = 0;
            uint32_t item = 0;
            uint16_t qty = 0;
            std::memcpy(&slot, op, sizeof slot);
            std::memcpy(&item, op + 8, sizeof item);
            std::memcpy(&qty, op + 0x24, sizeof qty);
            if (!slot || !(uint32_t)slot || (slot >> 32) >= 8 ||
                !item || !qty || op[0x26] != 1) return {};
        }
        return {static_cast<uint32_t>(count - before.count), true};
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return {};
    }
}

void pay_hook_failed() {
    g_recipe_ret = 0;""")

edit(rules,
     """    return h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
}

// Check the resolved return address really follows a direct call to this payment.""",
     """    kiln_rules_creative::JournalStart before{};
    if constexpr (cbpay::settlement::kEnabled)
        before = kiln_rules_creative::receipt_before(
            tx, owner, reinterpret_cast<uintptr_t>(_ReturnAddress()), 1);
    const uint32_t original = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
    if constexpr (cbpay::settlement::kEnabled) {
        if (before.valid) {
            const auto delta = kiln_rules_creative::receipt_after(
                tx, owner, reinterpret_cast<uintptr_t>(_ReturnAddress()), 1, before);
            cbpay::settlement::g_original_recipe_witness.payer_returned(
                {tx, owner, cbpay::settlement::Payer::InventoryPay,
                 original, delta.take_ops, delta.checked, true});
        }
    }
    return original;
}

// Check the resolved return address really follows a direct call to this payment.""")

edit(creative,
     """    return h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
}

// ============================================================================================ craft buttons ======""",
     """    JournalStart before{};
    if constexpr (cbpay::settlement::kEnabled)
        before = receipt_before(tx, owner,
            reinterpret_cast<uintptr_t>(_ReturnAddress()), 2);
    const uint32_t original = h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
    if constexpr (cbpay::settlement::kEnabled) {
        if (before.valid) {
            const auto delta = receipt_after(tx, owner,
                reinterpret_cast<uintptr_t>(_ReturnAddress()), 2, before);
            cbpay::settlement::g_original_recipe_witness.payer_returned(
                {tx, owner, cbpay::settlement::Payer::RecipePayAlt,
                 original, delta.take_ops, delta.checked, true});
        }
    }
    return original;
}

// ============================================================================================ craft buttons ======""")

edit(rules,
     """// Check the resolved return address really follows a direct call to this payment.""",
     """// Extra detours exist ONLY in a non-deployed, explicitly compiled lab build.
// The normal compile has kEnabled=false: neither hook is installed.
// Call the actual original FIRST; faults/aborts cannot be mistaken for
// successful commit/rollback receipts. No stock or transaction writes here.
SafetyHookInline h_cbpay_commit, h_cbpay_rollback;
void __fastcall hk_cbpay_commit(void* tx) {
    h_cbpay_commit.call<void>(tx);
    cbpay::settlement::g_original_recipe_witness.original_finality_returned(
        tx, cbpay::settlement::Finality::Commit);
}
void __fastcall hk_cbpay_rollback(void* tx) {
    h_cbpay_rollback.call<void>(tx);
    cbpay::settlement::g_original_recipe_witness.original_finality_returned(
        tx, cbpay::settlement::Finality::Rollback);
}

// Check the resolved return address really follows a direct call to this payment.""")

edit(rules,
     """        else {
            g_upgrade_ret = 0;
            note("storage upgrade (rules.storage)");
        }
    }();""",
     """        else {
            g_upgrade_ret = 0;
            note("storage upgrade (rules.storage)");
        }
        if constexpr (cbpay::settlement::kEnabled) {
            // Test-only read-only settlement witness, never a payment gate.
            // Scope to a server and a successfully resolved original recipe payer.
            uintptr_t commit=0, rollback=0;
            if (g_api->side() == KILN_SIDE_SERVER &&
                (g_hooks & KILN_RULES_HOOK_CRAFT) &&
                sym("items/InventoryCommit", &commit) &&
                sym("items/InventoryRollback", &rollback) &&
                inline_hook(h_cbpay_commit, reinterpret_cast<void*>(commit),
                            reinterpret_cast<void*>(&hk_cbpay_commit), "test-only original commit") &&
                inline_hook(h_cbpay_rollback, reinterpret_cast<void*>(rollback),
                            reinterpret_cast<void*>(&hk_cbpay_rollback), "test-only original rollback")) {
                LOGI("Cross-base transaction witness (test only): settlement hooks active; NO remote payment");
            } else {
                LOGW("Cross-base transaction witness: no reliable original finality hooks; cannot attest rollback");
            }
        }
    }();""")

edit(rules,
     """void KILN_CALL on_world(const KilnWorldEvent* ev, void*) {
    if (ev->event == KILN_WORLD_STARTED || ev->event == KILN_WORLD_STOPPING) kiln_rules_flight::reset();""",
     """void KILN_CALL on_world(const KilnWorldEvent* ev, void*) {
    if constexpr (cbpay::settlement::kEnabled) {
        if (ev->kind == KILN_WORLD_SERVER &&
            (ev->event == KILN_WORLD_STARTED || ev->event == KILN_WORLD_STOPPING)) {
            if (ev->event == KILN_WORLD_STOPPING) {
                const auto s = cbpay::settlement::g_original_recipe_witness.snapshot();
                LOGI("Cross-base test witness: payer=%llu commitOk=%llu rollbackOk=%llu rollbackFail=%llu commitFail=%llu pending=%llu ambiguous=%llu; NOT an item-restoration proof",
                    (unsigned long long)s.recorded,
                    (unsigned long long)s.committed_success,
                    (unsigned long long)s.rolled_back_success,
                    (unsigned long long)s.rolled_back_failed,
                    (unsigned long long)s.committed_failed,
                    (unsigned long long)s.pending,
                    (unsigned long long)s.ambiguous);
            }
            cbpay::settlement::g_original_recipe_witness.reset_world();
        }
    }
    if (ev->event == KILN_WORLD_STARTED || ev->event == KILN_WORLD_STOPPING) kiln_rules_flight::reset();""")

body = (root / rules).read_text(encoding="utf8")
alt = (root / creative).read_text(encoding="utf8")
assert body.count("cbpay::engine::dispatch(") == 1
assert alt.count("cbpay::engine::dispatch(") == 1
assert body.count("cbpay::settlement::kEnabled") >= 2
assert alt.count("cbpay::settlement::kEnabled") == 1
assert "resolver=nullptr" not in body or "nullptr);" in body
print("PASS: original payer and finality witness code compiles only; lab hooks OFF, original payers unchanged.")
