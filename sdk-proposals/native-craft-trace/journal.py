#!/usr/bin/env python3
"""Append a bounded, passive recipe-payment journal observer to the existing
native-craft-trace patch in a disposable, EXACT-revision Kiln checkout.

Run native.py and managed.py first. This file changes neither installed Kiln
nor any shipping Cross-base Storage project. There are NO extra detours.
"""
from pathlib import Path
import os
import subprocess
import sys

# Historical dev snapshot remains the local default; CI always sets the newest
# PUBLISHED release commit and then checks it against the checkout.
PIN = os.environ.get("KILN_UPSTREAM_SHA", "77e525888ac0d5f4f6b16103e945ed7ac049b50e")
if len(sys.argv) != 2:
    raise SystemExit("Usage: journal.py <already-patched-disposable-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
revision = subprocess.check_output(
    ["git", "-C", str(root), "rev-parse", "HEAD"], text=True
).strip()
if revision != PIN:
    raise SystemExit(f"Wrong upstream revision: {revision}")

def edit(path, before, after, matches=1):
    target = root / path
    original = target.read_text(encoding="utf8")
    if original.count(before) != matches:
        raise RuntimeError(f"Unexpected upstream anchor in {path}: {before[:90]!r}")
    target.write_text(original.replace(before, after, matches), encoding="utf8")
    print("PATCH", path)

abi = "native/core/abi/services/kiln_rules.h"
edit(abi, "typedef struct KilnRulesApi {",
"""/* Only staged, original-game payer operations. NEVER a committed debit, an
 * authorized remote source, or a pointer into the engine. Eight samples max.
 * status: 0=no observation, 1=complete, 2=invalid, 3=truncated. */
typedef struct KilnRulesJournalTrace {
    uint32_t size;
    uint32_t status;
    uint64_t calls;
    uint64_t rejected;
    uint32_t kind;                 /* 1 primary, 2 alternative */
    uint32_t owner;
    uint32_t native_result;
    uint32_t new_ops;
    uint32_t take_ops;
    uint32_t distinct;
    uint32_t inventory_entities[8]; /* underlying slot inventory ids, not chest owners */
    uint32_t units[8];
} KilnRulesJournalTrace;

typedef struct KilnRulesApi {""")
edit(abi,
"""    KilnResult(KILN_CALL* craft_trace)(KilnRulesCraftTrace* out);
} KilnRulesApi;""",
"""    KilnResult(KILN_CALL* craft_trace)(KilnRulesCraftTrace* out);
    KilnResult(KILN_CALL* journal_trace)(KilnRulesJournalTrace* out);
} KilnRulesApi;""")

hh = "native/services/rules/creative.h"
edit(hh,
"""void reset_craft_trace();
KilnResult KILN_CALL craft_trace(KilnRulesCraftTrace* out);""",
"""void reset_craft_trace();
KilnResult KILN_CALL craft_trace(KilnRulesCraftTrace* out);
// These calls bracket ONLY the original game recipe payer; the observer does not
// access costs, alter payments, or retain pointers to the borrowed transaction.
uint64_t journal_before(void* tx, uintptr_t caller, uint32_t kind);
void journal_after(void* tx, uintptr_t caller, uint64_t mark,
                   uint32_t owner, uint32_t native_result, uint32_t kind);
void journal_reset();
KilnResult KILN_CALL journal_trace(KilnRulesJournalTrace* out);""")

cpp = "native/services/rules/creative.cpp"
edit(cpp, "}  // namespace\n\nbool idle() { return g_flame.load(std::memory_order_relaxed); }\n",
r"""}  // namespace

// The game owns the transaction. We read only the delta queued by the
// ORIGINAL recipe payment while the caller still owns its ECS context.
// Offsets from pinned upstream items/tx.* sigdb and docs/re/items.md.
// This is deliberately disabled if any of those constants change.
std::atomic<bool> g_journal_ready{false};
std::atomic<uint64_t> g_journal_calls{0}, g_journal_rejected{0};
std::mutex g_journal_mu;
KilnRulesJournalTrace g_journal_last{};

struct JournalCursor {
    const uint8_t* ops = nullptr;
    uint64_t count = 0;
};

bool read_journal_cursor(void* tx, JournalCursor* out) {
    if (!tx || !out) return false;
    __try {
        const auto* base = static_cast<const uint8_t*>(tx);
        std::memcpy(&out->ops, base + 0xF0, sizeof(out->ops));
        std::memcpy(&out->count, base + 0xF8, sizeof(out->count));
        return out->ops != nullptr && out->count <= 4096;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

// Read one 52-byte, engine-owned operation without retaining its address.
bool read_journal_op(const uint8_t* address, uint32_t* slot_entity,
                     uint8_t* slot_index, uint32_t* item,
                     uint16_t* amount, uint8_t* kind) {
    if (!address) return false;
    __try {
        uint8_t bytes[0x34]{};
        std::memcpy(bytes, address, sizeof bytes);
        std::memcpy(slot_entity, bytes, sizeof(uint32_t));
        *slot_index = bytes[4];
        std::memcpy(item, bytes + 0x08, sizeof(uint32_t));
        std::memcpy(amount, bytes + 0x24, sizeof(uint16_t));
        *kind = bytes[0x26];
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

uint64_t journal_before(void* tx, uintptr_t caller, uint32_t kind) {
    if (!g_journal_ready.load(std::memory_order_relaxed) ||
        !g_recipe_ret || caller != g_recipe_ret + (kind == 2 ? 7 : 0))
        return UINT64_MAX;
    JournalCursor c{};
    if (!read_journal_cursor(tx, &c)) {
        g_journal_rejected.fetch_add(1, std::memory_order_relaxed);
        return UINT64_MAX;
    }
    return c.count;
}

void journal_after(void* tx, uintptr_t caller, uint64_t mark,
                   uint32_t owner, uint32_t native_result, uint32_t kind) {
    if (mark == UINT64_MAX || !g_recipe_ret ||
        caller != g_recipe_ret + (kind == 2 ? 7 : 0)) return;
    g_journal_calls.fetch_add(1, std::memory_order_relaxed);
    KilnRulesJournalTrace v{};
    v.size = sizeof v;
    v.kind = kind;
    v.owner = owner;
    v.native_result = native_result;
    v.status = 2; // invalid until every checked read succeeds

    JournalCursor c{};
    if (read_journal_cursor(tx, &c) &&
        c.count >= mark && c.count - mark <= 128 &&
        reinterpret_cast<uintptr_t>(c.ops) <= UINTPTR_MAX - c.count * 0x34u) {
        v.status = 1;
        v.new_ops = static_cast<uint32_t>(c.count - mark);
        for (uint64_t i = mark; i < c.count; ++i) {
            uint32_t entity = 0, item = 0;
            uint16_t amount = 0;
            uint8_t index = 0, type = 0;
            if (!read_journal_op(c.ops + i * 0x34u, &entity, &index,
                                 &item, &amount, &type) ||
                (type != 0 && type != 1) ||
                (type == 1 && (!entity || index >= 8 || !item || !amount))) {
                v = {};
                v.size = sizeof v;
                v.status = 2;
                v.kind = kind;
                v.owner = owner;
                v.native_result = native_result;
                break;
            }
            if (type != 1) continue; // only original game's take operations
            ++v.take_ops;
            uint32_t at = 0;
            while (at < v.distinct && at < 8 &&
                   v.inventory_entities[at] != entity) ++at;
            if (at == v.distinct) ++v.distinct;
            if (at >= 8) {
                v.status = 3; // more than eight sources; truncated sample
                continue;
            }
            v.inventory_entities[at] = entity;
            const uint64_t sum = static_cast<uint64_t>(v.units[at]) + amount;
            v.units[at] = sum > UINT32_MAX ? UINT32_MAX : static_cast<uint32_t>(sum);
        }
    }
    if (v.status == 2)
        g_journal_rejected.fetch_add(1, std::memory_order_relaxed);
    std::unique_lock<std::mutex> lock(g_journal_mu, std::try_to_lock);
    if (lock.owns_lock()) g_journal_last = v;
    else g_journal_rejected.fetch_add(1, std::memory_order_relaxed);
}

void journal_reset() {
    std::lock_guard<std::mutex> lock(g_journal_mu);
    g_journal_last = {};
    g_journal_calls.store(0, std::memory_order_relaxed);
    g_journal_rejected.store(0, std::memory_order_relaxed);
}

KilnResult KILN_CALL journal_trace(KilnRulesJournalTrace* out) {
    if (!out || out->size < sizeof(KilnRulesJournalTrace))
        return KILN_ERR_INVALID_ARGUMENT;
    std::lock_guard<std::mutex> lock(g_journal_mu);
    KilnRulesJournalTrace v = g_journal_last;
    v.size = sizeof v;
    v.calls = g_journal_calls.load(std::memory_order_relaxed);
    v.rejected = g_journal_rejected.load(std::memory_order_relaxed);
    *out = v;
    return KILN_OK;
}

bool idle() { return g_flame.load(std::memory_order_relaxed); }
""")
edit(cpp,
"""    const uint32_t result = h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
    if (g_recipe_ret && ret == g_recipe_ret + 7) {""",
"""    const uint64_t mark = journal_before(tx, ret, 2);
    const uint32_t result = h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
    journal_after(tx, ret, mark, owner, result, 2);
    if (g_recipe_ret && ret == g_recipe_ret + 7) {""")
edit(cpp,
"""    CT.ui_cap.store(0, std::memory_order_relaxed);
}""",
"""    CT.ui_cap.store(0, std::memory_order_relaxed);
    journal_reset();
}""")
edit(cpp,
"""    g_api = api;
    auto note =""",
"""    g_api = api;
    int64_t ops = -1, inline_ops = -1, operator_offset = -1;
    g_journal_ready.store(
        api->side() == KILN_SIDE_SERVER &&
        cst("items/tx.ops", &ops) && ops == 0xF0 &&
        cst("items/tx.ops_inline", &inline_ops) && inline_ops == 0x118 &&
        cst("items/tx.operator", &operator_offset) && operator_offset == 0xE0,
        std::memory_order_relaxed);
    auto note =""")

rules = "native/services/rules/rules.cpp"
edit(rules,
"""    if (kiln_rules_creative::idle()) {
        const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
        kiln_rules_creative::trace_primary_payment(reinterpret_cast<uintptr_t>(_ReturnAddress()), owner, result);
        return result;
    }""",
"""    if (kiln_rules_creative::idle()) {
        const auto ret = reinterpret_cast<uintptr_t>(_ReturnAddress());
        const auto mark = kiln_rules_creative::journal_before(tx, ret, 1);
        const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
        kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);
        kiln_rules_creative::trace_primary_payment(ret, owner, result);
        return result;
    }""")
edit(rules,
"""    const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
    kiln_rules_creative::trace_primary_payment(reinterpret_cast<uintptr_t>(_ReturnAddress()), owner, result);
    return result;""",
"""    const auto ret = reinterpret_cast<uintptr_t>(_ReturnAddress());
    const auto mark = kiln_rules_creative::journal_before(tx, ret, 1);
    const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
    kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);
    kiln_rules_creative::trace_primary_payment(ret, owner, result);
    return result;""")
edit(rules,
"""    &kiln_rules_creative::craft_trace,
};""",
"""    &kiln_rules_creative::craft_trace, &kiln_rules_creative::journal_trace,
};""")

managed = "managed/services/Kiln.Rules/Native.cs"
edit(managed, """    private static CraftTraceTable* _p;""",
"""    [StructLayout(LayoutKind.Sequential)]
    public struct JournalTrace
    {
        public uint Size, Status;
        public ulong Calls, Rejected;
        public uint Kind, Owner, NativeResult, NewOps, TakeOps, Distinct;
        public uint Id0, Id1, Id2, Id3, Id4, Id5, Id6, Id7;
        public uint Units0, Units1, Units2, Units3, Units4, Units5, Units6, Units7;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JournalTable
    {
        public CraftTraceTable Previous;
        public delegate* unmanaged[Cdecl]<JournalTrace*, int> Snapshot;
    }
    private static JournalTable* _j;
    public static bool JournalSnapshot(out JournalTrace trace)
    {
        trace = default;
        var table = _j != null ? _j : (_j = Services.Get<JournalTable>(Service, Version));
        if (table == null) return false;
        JournalTrace v = default;
        v.Size = (uint)sizeof(JournalTrace);
        if (table->Snapshot(&v) != 0) return false;
        trace = v;
        return true;
    }

    private static CraftTraceTable* _p;""")

rulescs = "managed/services/Kiln.Rules/Rules.cs"
edit(rulescs, """    /// <summary>Read-only original crafting payment/UI trace""",
"""    /// <summary>Experimental native journal observation of the ORIGINAL
    /// recipe payment operation delta. This reports staged take-operations, NOT
    /// committed debits or authorized remote storage. Null on ordinary Kiln.
    /// A status of 2 invalidates all source samples; 3 means truncated.</summary>
    public static RulesJournalProbe? TraceRecipeJournal()
    {
        if (!RulesNative.JournalSnapshot(out var v)) return null;
        var ids = new uint[] { v.Id0, v.Id1, v.Id2, v.Id3,
                               v.Id4, v.Id5, v.Id6, v.Id7 };
        var units = new uint[] { v.Units0, v.Units1, v.Units2, v.Units3,
                                 v.Units4, v.Units5, v.Units6, v.Units7 };
        var samples = new List<RulesJournalSlotSource>();
        if (v.Status is 1 or 3)
            for (var i = 0; i < Math.Min((int)v.Distinct, 8); ++i)
                if (ids[i] != 0) samples.Add(new(ids[i], units[i]));
        return new RulesJournalProbe(v.Status, v.Calls, v.Rejected,
            v.Kind, v.Owner, v.NativeResult, v.NewOps, v.TakeOps,
            v.Distinct, samples);
    }

    /// <summary>Read-only original crafting payment/UI trace""")
edit(rulescs,
"""/// <summary>Experimental passive trace of original recipe payer and client""",
"""/// <summary>One sampled underlying inventory entity and its staged take units.
    /// This is not a chest owner ID and no commit has occurred.</summary>
public sealed record RulesJournalSlotSource(uint InventoryEntity, uint StagedUnits);

/// <summary>Experimental passive original recipe-payer journal delta.
    /// Status: 0 never observed, 1 complete, 2 invalid, 3 more than 8 sources.
    /// Call counts are process-local; no remote eligibility is inferred.</summary>
public sealed record RulesJournalProbe(
    uint Status, ulong Calls, ulong Rejected, uint PayerKind, uint Owner,
    uint NativeResult, uint NewOperations, uint TakeOperations,
    uint DistinctInventories, IReadOnlyList<RulesJournalSlotSource> Sources);

/// <summary>Experimental passive trace of original recipe payer and client""")

print("PASS: passive staged journal-source observer added to existing hooks, ABI tail only.")
