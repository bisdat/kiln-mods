#!/usr/bin/env python3
"""Append a bounded, passive native recipe cost-list observer to EXACT released Kiln.

Apply native.py, managed.py and journal.py first. No new detours, no payment
substitution, no transaction mutation and no patched DLL release.
"""
from pathlib import Path
import os
import subprocess
import sys

PIN = os.environ.get("KILN_UPSTREAM_SHA", "77e525888ac0d5f4f6b16103e945ed7ac049b50e")
if len(sys.argv) != 2:
    raise SystemExit("Usage: costs.py <previously-patched-disposable-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
actual = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
if actual != PIN:
    raise SystemExit(f"Refusing Kiln source {actual}; expected published {PIN}")

def edit(path, before, after, matches=1):
    target = root / path
    source = target.read_text(encoding="utf-8")
    if source.count(before) != matches:
        raise RuntimeError(f"Unexpected upstream anchor in {path}: count={source.count(before)}, wanted={matches}: {before[:115]!r}")
    target.write_text(source.replace(before, after, matches), encoding="utf-8")
    print("PATCH", path)

abi = "native/core/abi/services/kiln_rules.h"
edit(abi, "typedef struct KilnRulesApi {",
"""/* PASSIVE, UNVERIFIED raw cost-row observation from ORIGINAL recipe payers.
 * These may be category, selection or alternative costs. Raw item/count
 * fields are NOT proof of authoritative ingredients or successful debits.
 * status 0=no sample, 1=readable, 2=invalid, 3=truncated at eight rows.
 * There are no native pointers in this stable copy of the observation. */
typedef struct KilnRulesCostTrace {
    uint32_t size;
    uint32_t status;
    uint64_t calls;
    uint64_t rejected;
    uint32_t kind;        /* 1=InventoryPay, 2=RecipePayAlt */
    uint32_t owner;
    uint32_t tx_operator;
    uint32_t multiplier;
    uint32_t native_result;
    uint32_t raw_entries;
    uint32_t samples;
    uint32_t flags;       /* 1=layout assumption only, never authorisation */
    uint32_t raw_items[8];
    uint32_t raw_counts[8];
} KilnRulesCostTrace;

typedef struct KilnRulesApi {""")
edit(abi,
"""    KilnResult(KILN_CALL* journal_trace)(KilnRulesJournalTrace* out);
} KilnRulesApi;""",
"""    KilnResult(KILN_CALL* journal_trace)(KilnRulesJournalTrace* out);
    KilnResult(KILN_CALL* cost_trace)(KilnRulesCostTrace* out);
} KilnRulesApi;""")

hdr = "native/services/rules/creative.h"
edit(hdr, """KilnResult KILN_CALL journal_trace(KilnRulesJournalTrace* out);""",
"""KilnResult KILN_CALL journal_trace(KilnRulesJournalTrace* out);
// No pointer to game state survives the original call, and no extra detour.
struct RawCostSample;
RawCostSample cost_before(void* tx, int64_t* costs, uintptr_t ret,
                          uint32_t kind, uint32_t owner, uint32_t multiplier);
void cost_after(const RawCostSample& sample, uint32_t native_result);
void cost_reset();
KilnResult KILN_CALL cost_trace(KilnRulesCostTrace* out);""")

cpp = "native/services/rules/creative.cpp"
# This struct has no pointers and can be passed by value between rules.cpp and creative.cpp.
edit(cpp, "}  // namespace\n\n// The game owns the transaction.",
""" }  // namespace

// Opaque-to-consumers, bounded copy captured BEFORE the original payer.
// Defined in creative.h so rules.cpp can hold it on its own stack.
// No resource mutating operations are performed by this trace.
// The observer assumes, but must NOT assert, that cost rows match the
// 0x18-byte ApplyMaterials entries documented in docs/re/rules.md.
""".lstrip()+"// The game owns the transaction.")
# A struct definition must be in the header to make by-value returns valid.
edit(hdr, """struct RawCostSample;
RawCostSample cost_before""",
"""struct RawCostSample { KilnRulesCostTrace value{}; bool eligible = false; };
RawCostSample cost_before""")
edit(cpp, "bool idle() { return g_flame.load(std::memory_order_relaxed); }\n",
r"""// Compact scalar-only native cost snapshot. The payer's costs argument is
// believed to be {ptr,count} of 0x18 rows, with raw item at +0x10 and raw
// count at +0x14. This has NOT been runtime-validated for RecipePayAlt.
// Guard every pointer read; do not log game pointers or infer "free" from 0.
std::atomic<uint64_t> g_cost_calls{0}, g_cost_rejected{0};
std::mutex g_cost_mu;
KilnRulesCostTrace g_cost_last{};

RawCostSample cost_before(void* tx, int64_t* costs, uintptr_t ret,
                          uint32_t kind, uint32_t owner, uint32_t multiplier) {
    RawCostSample out{};
    if (!g_journal_ready.load(std::memory_order_relaxed) ||
        !g_recipe_ret || (kind != 1 && kind != 2) ||
        ret != g_recipe_ret + (kind == 2 ? 7 : 0))
        return out;
    out.eligible = true;
    auto& s = out.value;
    s.size = sizeof s;
    s.kind = kind;
    s.owner = owner;
    s.multiplier = multiplier;
    s.flags = 1; // unverified recipe layout: no authorisation implied
    s.status = 2; // fail-closed until all checked pointer reads succeed
    if (!tx || !costs) return out;
    __try {
        const auto* txbytes = static_cast<const uint8_t*>(tx);
        std::memcpy(&s.tx_operator, txbytes + 0xE0, sizeof s.tx_operator);
        struct Pair { const uint8_t* rows; uint64_t count; } pair{};
        static_assert(sizeof(Pair) == 16);
        std::memcpy(&pair, costs, sizeof pair);
        // Bounds are defensive observations, not a proof that this layout
        // is in fact the game's selected recipe cost format.
        if (pair.count > 128 || (pair.count && !pair.rows) ||
            reinterpret_cast<uintptr_t>(pair.rows) > UINTPTR_MAX - pair.count * 0x18u)
            return out;
        s.raw_entries = static_cast<uint32_t>(pair.count);
        s.samples = static_cast<uint32_t>(std::min<uint64_t>(pair.count, 8));
        for (uint32_t i = 0; i < s.samples; ++i) {
            uint8_t row[0x18]{};
            std::memcpy(row, pair.rows + i * 0x18u, sizeof row);
            std::memcpy(&s.raw_items[i], row + 0x10, sizeof(uint32_t));
            std::memcpy(&s.raw_counts[i], row + 0x14, sizeof(uint32_t));
        }
        s.status = pair.count > 8 ? 3 : 1;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        s.status = 2;
        s.samples = 0;
        s.raw_entries = 0;
        std::memset(s.raw_items, 0, sizeof s.raw_items);
        std::memset(s.raw_counts, 0, sizeof s.raw_counts);
    }
    return out;
}

void cost_after(const RawCostSample& observed, uint32_t native_result) {
    if (!observed.eligible) return;
    g_cost_calls.fetch_add(1, std::memory_order_relaxed);
    auto sample = observed.value;
    sample.native_result = native_result;
    if (sample.status == 2)
        g_cost_rejected.fetch_add(1, std::memory_order_relaxed);
    std::unique_lock<std::mutex> lock(g_cost_mu, std::try_to_lock);
    if (lock.owns_lock()) g_cost_last = sample;
    else g_cost_rejected.fetch_add(1, std::memory_order_relaxed);
}

void cost_reset() {
    std::lock_guard<std::mutex> lock(g_cost_mu);
    g_cost_last = {};
    g_cost_calls.store(0, std::memory_order_relaxed);
    g_cost_rejected.store(0, std::memory_order_relaxed);
}

KilnResult KILN_CALL cost_trace(KilnRulesCostTrace* out) {
    if (!out || out->size < sizeof(KilnRulesCostTrace)) return KILN_ERR_INVALID_ARGUMENT;
    std::lock_guard<std::mutex> lock(g_cost_mu);
    auto v = g_cost_last;
    v.size = sizeof v;
    v.calls = g_cost_calls.load(std::memory_order_relaxed);
    v.rejected = g_cost_rejected.load(std::memory_order_relaxed);
    *out = v;
    return KILN_OK;
}

bool idle() { return g_flame.load(std::memory_order_relaxed); }
""")
edit(cpp,
"""    const uint64_t mark = journal_before(tx, ret, 2);
    const uint32_t result = h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
    journal_after(tx, ret, mark, owner, result, 2);""",
"""    const uint64_t mark = journal_before(tx, ret, 2);
    const auto raw = cost_before(tx, costs, ret, 2, owner, multiplier);
    const uint32_t result = h_alt.call<uint32_t>(tx, costs, owner, multiplier, extra);
    journal_after(tx, ret, mark, owner, result, 2);
    cost_after(raw, result);""")
edit(cpp, """    journal_reset();
}""",
"""    journal_reset();
    cost_reset();
}""")

rules = "native/services/rules/rules.cpp"
edit(rules,
"""        const auto mark = kiln_rules_creative::journal_before(tx, ret, 1);
        const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
        kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);""",
"""        const auto mark = kiln_rules_creative::journal_before(tx, ret, 1);
        const auto raw = kiln_rules_creative::cost_before(tx, costs, ret, 1, owner, multiplier);
        const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
        kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);
        kiln_rules_creative::cost_after(raw, result);""")
edit(rules,
"""    const auto mark = kiln_rules_creative::journal_before(tx, ret, 1);
    const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
    kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);""",
"""    const auto mark = kiln_rules_creative::journal_before(tx, ret, 1);
    const auto raw = kiln_rules_creative::cost_before(tx, costs, ret, 1, owner, multiplier);
    const auto result = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
    kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);
    kiln_rules_creative::cost_after(raw, result);""")
edit(rules,
"""    &kiln_rules_creative::craft_trace, &kiln_rules_creative::journal_trace,
};""",
"""    &kiln_rules_creative::craft_trace, &kiln_rules_creative::journal_trace, &kiln_rules_creative::cost_trace,
};""")

managed = "managed/services/Kiln.Rules/Native.cs"
edit(managed, """    private static JournalTable* _j;""",
"""    [StructLayout(LayoutKind.Sequential)]
    public struct CostTrace
    {
        public uint Size, Status;
        public ulong Calls, Rejected;
        public uint Kind, Owner, Operator, Multiplier;
        public uint NativeResult, RawEntries, Samples, Flags;
        public uint Id0, Id1, Id2, Id3, Id4, Id5, Id6, Id7;
        public uint Count0, Count1, Count2, Count3, Count4, Count5, Count6, Count7;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct CostTable
    {
        public JournalTable Previous;
        public delegate* unmanaged[Cdecl]<CostTrace*, int> Snapshot;
    }
    private static CostTable* _ct;
    public static bool CostSnapshot(out CostTrace trace)
    {
        trace = default;
        var table = _ct != null ? _ct : (_ct = Services.Get<CostTable>(Service, Version));
        if (table == null) return false;
        CostTrace v = default;
        v.Size = (uint)sizeof(CostTrace);
        if (table->Snapshot(&v) != 0) return false;
        trace = v;
        return true;
    }

    private static JournalTable* _j;""")

rulescs = "managed/services/Kiln.Rules/Rules.cs"
edit(rulescs,
"""    /// <summary>Experimental native journal observation of the ORIGINAL""",
"""    /// <summary>Read-only, experimental raw input-list observation from the
    /// ORIGINAL native recipe payer. Item/count offsets are an unverified
    /// assumption, NOT an authoritative recipe, source entitlement, or debit.
    /// Null on normal Kiln without this optional experiment.</summary>
    public static RulesRawCostProbe? TraceRecipeCostRows()
    {
        if (!RulesNative.CostSnapshot(out var v)) return null;
        var ids = new uint[] { v.Id0, v.Id1, v.Id2, v.Id3, v.Id4, v.Id5, v.Id6, v.Id7 };
        var counts = new uint[] { v.Count0, v.Count1, v.Count2, v.Count3,
                                  v.Count4, v.Count5, v.Count6, v.Count7 };
        var samples = new List<RulesRawCostRow>();
        if (v.Status is 1 or 3)
            for (var i = 0; i < Math.Min((int)v.Samples, 8); ++i)
                samples.Add(new(ids[i], counts[i]));
        return new RulesRawCostProbe(v.Status, v.Calls, v.Rejected, v.Kind,
            v.Owner, v.Operator, v.Multiplier, v.NativeResult, v.RawEntries,
            v.Flags, samples);
    }

    /// <summary>Experimental native journal observation of the ORIGINAL""")
edit(rulescs,
"""/// <summary>One sampled underlying inventory entity and its staged take units.""",
"""/// <summary>One unverified raw payer cost row (offsets based on building
/// ApplyMaterials, NOT validated for alternative recipe categories).</summary>
public sealed record RulesRawCostRow(uint RawItemId, uint RawCount);

/// <summary>Raw original-payer cost-list observation, NOT selected recipe
/// ingredients and NOT proof of committed resource debit. Status: 0 unseen,
/// 1 read, 2 invalid memory/layout, 3 sample truncated to eight rows.</summary>
public sealed record RulesRawCostProbe(
    uint Status, ulong Calls, ulong Rejected, uint PayerKind, uint Owner,
    uint TransactionOperator, uint Multiplier, uint NativeResult,
    uint RawEntryCount, uint Flags, IReadOnlyList<RulesRawCostRow> Rows);

/// <summary>One sampled underlying inventory entity and its staged take units.""")

print("PASS: bounded raw native payer-cost observer added without detours/payment mutation.")
