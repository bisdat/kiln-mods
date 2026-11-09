#!/usr/bin/env python3
"""Optional same-invocation passive payer cost+journal snapshot.

Apply native.py, managed.py, journal.py, costs.py first, to the exact
published Kiln checkout. No extra detours; existing ORIGINAL payment
functions remain untouched; no inventory mutations.
"""
from pathlib import Path
import os
import subprocess
import sys

PIN = os.environ.get("KILN_UPSTREAM_SHA", "77e525888ac0d5f4f6b16103e945ed7ac049b50e")
if len(sys.argv) != 2:
    raise SystemExit("Usage: paired.py <previously-patched-disposable-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
actual = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
if actual != PIN:
    raise SystemExit(f"Refusing upstream source {actual}; expected {PIN}")

def edit(path, before, after, matches=1):
    file = root / path
    source = file.read_text(encoding="utf-8")
    count = source.count(before)
    if count != matches:
        raise RuntimeError(f"Unexpected anchor in {path}: expected {matches}, got {count}: {before[:100]!r}")
    file.write_text(source.replace(before, after), encoding="utf-8")
    print(f"PATCH {path}")

abi = "native/core/abi/services/kiln_rules.h"
edit(abi, "typedef struct KilnRulesApi {", """/* EXACT SAME original recipe payer invocation, recorded under ONE mutex.
 * The original functions still own ALL commits/rollbacks. No proof of a
 * committed debit or authoritative recipe cost encoding.
 * No game transaction pointers survive in this snapshot.
 * status: 0=unseen, 1=both complete, 2=at least one invalid,
 * 3=either sample truncated (at most 8 raw cost rows/sources). */
typedef struct KilnRulesPaymentTrace {
    uint32_t size;
    uint32_t status;
    uint64_t calls;
    uint64_t rejected;
    uint64_t sample_id;
    KilnRulesCostTrace cost;
    KilnRulesJournalTrace journal;
} KilnRulesPaymentTrace;

typedef struct KilnRulesApi {""")
edit(abi,
"""    KilnResult(KILN_CALL* cost_trace)(KilnRulesCostTrace* out);
} KilnRulesApi;""",
"""    KilnResult(KILN_CALL* cost_trace)(KilnRulesCostTrace* out);
    KilnResult(KILN_CALL* payment_trace)(KilnRulesPaymentTrace* out);
} KilnRulesApi;""")

hdr = "native/services/rules/creative.h"
edit(hdr, """void journal_after(void* tx, uintptr_t caller, uint64_t mark,
                   uint32_t owner, uint32_t native_result, uint32_t kind);""",
"""KilnRulesJournalTrace journal_after(void* tx, uintptr_t caller, uint64_t mark,
                                    uint32_t owner, uint32_t native_result, uint32_t kind);""")
edit(hdr, """KilnResult KILN_CALL cost_trace(KilnRulesCostTrace* out);""",
"""KilnResult KILN_CALL cost_trace(KilnRulesCostTrace* out);
// Feed BOTH copies from the very same invocation; never read globals from
// two separately sampled traces to infer a transaction relationship.
void paired_after(const RawCostSample& raw, const KilnRulesJournalTrace& journal,
                  uint32_t native_result);
void paired_reset();
KilnResult KILN_CALL payment_trace(KilnRulesPaymentTrace* out);""")

cpp = "native/services/rules/creative.cpp"
edit(cpp, """void journal_after(void* tx, uintptr_t caller, uint64_t mark,
                   uint32_t owner, uint32_t native_result, uint32_t kind) {""",
"""KilnRulesJournalTrace journal_after(void* tx, uintptr_t caller, uint64_t mark,
                                    uint32_t owner, uint32_t native_result, uint32_t kind) {""")
edit(cpp,
"""    if (mark == UINT64_MAX || !g_recipe_ret ||
        caller != g_recipe_ret + (kind == 2 ? 7 : 0)) return;""",
"""    if (mark == UINT64_MAX || !g_recipe_ret ||
        caller != g_recipe_ret + (kind == 2 ? 7 : 0)) return {};""")
edit(cpp,
"""    if (lock.owns_lock()) g_journal_last = v;
    else g_journal_rejected.fetch_add(1, std::memory_order_relaxed);
}""",
"""    if (lock.owns_lock()) g_journal_last = v;
    else g_journal_rejected.fetch_add(1, std::memory_order_relaxed);
    return v; // SAME original payer call, regardless of snapshot lock contention
}""")

edit(cpp, """bool idle() { return g_flame.load(std::memory_order_relaxed); }""",
r"""// Same-call observer: original payer execution and native tx journal sampling
// have already happened. Read detached local structs ONLY; never dereference
// tx, cost lists or live inventory here. The snapshot is atomic across fields
// and remains distinct from the older independently published diagnostics.
std::atomic<uint64_t> g_paired_calls{0}, g_paired_rejected{0};
std::mutex g_paired_mu;
KilnRulesPaymentTrace g_paired_last{};

void paired_after(const RawCostSample& raw, const KilnRulesJournalTrace& journal,
                  uint32_t native_result) {
    if (!raw.eligible) return;
    const uint64_t id = g_paired_calls.fetch_add(1, std::memory_order_relaxed) + 1;
    KilnRulesPaymentTrace sample{};
    sample.size = sizeof sample;
    sample.sample_id = id;
    sample.cost = raw.value;
    sample.cost.native_result = native_result;
    sample.journal = journal;
    if (raw.value.status == 0 || raw.value.status == 2 ||
        journal.status == 0 || journal.status == 2 ||
        raw.value.kind != journal.kind ||
        raw.value.owner != journal.owner ||
        journal.native_result != native_result) {
        sample.status = 2;
        g_paired_rejected.fetch_add(1, std::memory_order_relaxed);
    } else if (raw.value.status == 3 || journal.status == 3) {
        sample.status = 3;
    } else {
        sample.status = 1;
    }
    std::unique_lock<std::mutex> lock(g_paired_mu, std::try_to_lock);
    if (lock.owns_lock()) g_paired_last = sample;
    else g_paired_rejected.fetch_add(1, std::memory_order_relaxed);
}

void paired_reset() {
    std::lock_guard<std::mutex> lock(g_paired_mu);
    g_paired_last = {};
    g_paired_calls.store(0, std::memory_order_relaxed);
    g_paired_rejected.store(0, std::memory_order_relaxed);
}

KilnResult KILN_CALL payment_trace(KilnRulesPaymentTrace* out) {
    if (!out || out->size < sizeof(KilnRulesPaymentTrace))
        return KILN_ERR_INVALID_ARGUMENT;
    std::lock_guard<std::mutex> lock(g_paired_mu);
    KilnRulesPaymentTrace copy = g_paired_last;
    copy.size = sizeof copy;
    copy.calls = g_paired_calls.load(std::memory_order_relaxed);
    copy.rejected = g_paired_rejected.load(std::memory_order_relaxed);
    *out = copy;
    return KILN_OK;
}

bool idle() { return g_flame.load(std::memory_order_relaxed); }""")
edit(cpp, """    journal_after(tx, ret, mark, owner, result, 2);
    cost_after(raw, result);""",
"""    const auto journal = journal_after(tx, ret, mark, owner, result, 2);
    cost_after(raw, result);
    paired_after(raw, journal, result);""")
edit(cpp, """    journal_reset();
    cost_reset();
}""",
"""    journal_reset();
    cost_reset();
    paired_reset();
}""")

rules = "native/services/rules/rules.cpp"
edit(rules, """        kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);
        kiln_rules_creative::cost_after(raw, result);""",
"""        const auto journal = kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);
        kiln_rules_creative::cost_after(raw, result);
        kiln_rules_creative::paired_after(raw, journal, result);""")
edit(rules, """    kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);
    kiln_rules_creative::cost_after(raw, result);""",
"""    const auto journal = kiln_rules_creative::journal_after(tx, ret, mark, owner, result, 1);
    kiln_rules_creative::cost_after(raw, result);
    kiln_rules_creative::paired_after(raw, journal, result);""")
edit(rules,
"""    &kiln_rules_creative::craft_trace, &kiln_rules_creative::journal_trace, &kiln_rules_creative::cost_trace,
};""",
"""    &kiln_rules_creative::craft_trace, &kiln_rules_creative::journal_trace, &kiln_rules_creative::cost_trace,
    &kiln_rules_creative::payment_trace,
};""")

native = "managed/services/Kiln.Rules/Native.cs"
edit(native, """    private static CostTable* _ct;""",
"""    [StructLayout(LayoutKind.Sequential)]
    public struct PaymentTrace
    {
        public uint Size, Status;
        public ulong Calls, Rejected, SampleId;
        public CostTrace Cost;
        public JournalTrace Journal;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaymentTable
    {
        public CostTable Previous;
        public delegate* unmanaged[Cdecl]<PaymentTrace*, int> Snapshot;
    }

    private static PaymentTable* _pt;
    public static bool PaymentSnapshot(out PaymentTrace trace)
    {
        trace = default;
        var table = _pt != null ? _pt : (_pt = Services.Get<PaymentTable>(Service, Version));
        if (table == null) return false;
        PaymentTrace v = default;
        v.Size = (uint)sizeof(PaymentTrace);
        if (table->Snapshot(&v) != 0) return false;
        trace = v;
        return true;
    }

    private static CostTable* _ct;""")

managed = "managed/services/Kiln.Rules/Rules.cs"
edit(managed, """    /// <summary>Read-only, experimental raw input-list observation""",
"""    /// <summary>One same-invocation, optional read-only snapshot of the
    /// ORIGINAL recipe payer's raw cost rows and newly appended journal.
    /// The two samples are paired BEFORE publication under one native mutex.
    /// NOT proof that costs are decoded correctly, or that any debit commits.
    /// Null on normal/uninstrumented Kiln.</summary>
    public static RulesMatchedPaymentProbe? TraceRecipePayment()
    {
        if (!RulesNative.PaymentSnapshot(out var v)) return null;
        var c = v.Cost;
        var j = v.Journal;
        var ids = new uint[] { c.Id0, c.Id1, c.Id2, c.Id3, c.Id4, c.Id5, c.Id6, c.Id7 };
        var counts = new uint[] { c.Count0, c.Count1, c.Count2, c.Count3,
                                  c.Count4, c.Count5, c.Count6, c.Count7 };
        var rawRows = new List<RulesRawCostRow>();
        if (c.Status is 1 or 3)
            for (var i = 0; i < Math.Min((int)c.Samples, 8); ++i)
                rawRows.Add(new(ids[i], counts[i]));

        var inv = new uint[] { j.Id0, j.Id1, j.Id2, j.Id3,
                               j.Id4, j.Id5, j.Id6, j.Id7 };
        var units = new uint[] { j.Units0, j.Units1, j.Units2, j.Units3,
                                 j.Units4, j.Units5, j.Units6, j.Units7 };
        var sources = new List<RulesJournalSlotSource>();
        if (j.Status is 1 or 3)
            for (var i = 0; i < Math.Min((int)j.Distinct, 8); ++i)
                if (inv[i] != 0) sources.Add(new(inv[i], units[i]));

        return new RulesMatchedPaymentProbe(v.Status, v.Calls, v.Rejected, v.SampleId,
            new RulesRawCostProbe(c.Status, 0, 0, c.Kind, c.Owner, c.Operator,
                c.Multiplier, c.NativeResult, c.RawEntries, c.Flags, rawRows),
            new RulesJournalProbe(j.Status, 0, 0, j.Kind, j.Owner, j.NativeResult,
                j.NewOps, j.TakeOps, j.Distinct, sources));
    }

    /// <summary>Read-only, experimental raw input-list observation""")
edit(managed, """/// <summary>One unverified raw payer cost row""",
"""/// <summary>One matched original recipe payment call. Cost rows are
/// unverified and journal take ops are STAGED, never proof of commit.
/// Snapshot status: 0 unseen, 1 both readable, 2 invalid, 3 truncated.</summary>
public sealed record RulesMatchedPaymentProbe(
    uint Status, ulong Calls, ulong Rejected, ulong SampleId,
    RulesRawCostProbe Costs, RulesJournalProbe Journal);

/// <summary>One unverified raw payer cost row""")

print("PASS: same-invocation cost+journal pair published atomically, no payment changes.")
