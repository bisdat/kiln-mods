#!/usr/bin/env python3
"""Optional managed read-only wrapper for the experimental kiln.rules native trace."""
from pathlib import Path
import subprocess
import sys

PIN = "77e525888ac0d5f4f6b16103e945ed7ac049b50e"
if len(sys.argv) != 2:
    raise SystemExit("Usage: managed.py <disposable-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
actual = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
if actual != PIN:
    raise SystemExit(f"Refusing upstream revision {actual}; expected {PIN}")

def edit(path, before, after):
    file = root / path
    s = file.read_text(encoding="utf8")
    if s.count(before) != 1:
        raise RuntimeError(f"Upstream anchor missing or ambiguous in {path}: {before[:100]!r}")
    file.write_text(s.replace(before, after, 1), encoding="utf8")
    print("PATCH", path)

native = "managed/services/Kiln.Rules/Native.cs"
edit(native,
"""    [StructLayout(LayoutKind.Sequential)]
    public struct Player
""",
"""    // The appended, optional native kiln.rules passive craft/payment trace.
    [StructLayout(LayoutKind.Sequential)]
    public struct CraftTrace
    {
        public uint Size, Reserved;
        public ulong PrimaryCalls, AlternativeCalls, UiCalls;
        public uint LastPrimaryOwner, LastPrimaryResult;
        public uint LastAlternativeOwner, LastAlternativeResult;
        public uint LastUiRecipe, LastUiVanillaAmount, LastUiCap, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Player
""")
edit(native,
"""    private static CreativeTable* _c;""",
"""    // An older kiln.rules advertises a smaller table, so the optional
    // experiment is unavailable rather than indexing past its API tail.
    [StructLayout(LayoutKind.Sequential)]
    public struct CraftTraceTable
    {
        public CreativeTable V1;
        public delegate* unmanaged[Cdecl]<CraftTrace*, int> Snapshot;
    }

    private static CraftTraceTable* _p;
    public static bool CraftTraceSnapshot(out CraftTrace trace)
    {
        trace = default;
        var t = _p != null ? _p : (_p = Services.Get<CraftTraceTable>(Service, Version));
        if (t == null) return false;
        CraftTrace c = default;
        c.Size = (uint)sizeof(CraftTrace);
        if (t->Snapshot(&c) != 0) return false;
        trace = c;
        return true;
    }

    private static CreativeTable* _c;""")
rules="managed/services/Kiln.Rules/Rules.cs"
edit(rules,
"""    /// <summary>The native hooks' state and counters (null when kiln.rules is not loaded).</summary>
    public static RulesStatus? Status()""",
"""    /// <summary>Read-only original crafting payment/UI trace from a *matched
    /// experimental* kiln.rules native service. Null on standard/older Kiln.
    /// Counts are observations, not proof remote sources are eligible or paid.
    /// No transaction pointers or inventory mutations are exposed.</summary>
    public static RulesCraftProbe? TraceCraftPayments()
    {
        if (!RulesNative.CraftTraceSnapshot(out var v)) return null;
        return new RulesCraftProbe(
            v.PrimaryCalls, v.AlternativeCalls, v.UiCalls,
            v.LastPrimaryOwner, v.LastPrimaryResult,
            v.LastAlternativeOwner, v.LastAlternativeResult,
            v.LastUiRecipe, v.LastUiVanillaAmount, v.LastUiCap, v.Flags);
    }

    /// <summary>The native hooks' state and counters (null when kiln.rules is not loaded).</summary>
    public static RulesStatus? Status()""")
edit(rules,
"""/// <summary>One mod's rules for a scope. Dispose it to take them away.</summary>""",
"""/// <summary>Experimental passive trace of original recipe payer and client
/// craftability calls. Counters are process-local since the last world start.
/// Last-value fields may represent different threads/calls and must not be
/// treated as a transactional snapshot. Contains no source stock amounts.</summary>
public sealed record RulesCraftProbe(
    ulong PrimaryCalls, ulong AlternativeCalls, ulong UiCalls,
    uint LastPrimaryOwner, uint LastPrimaryResult,
    uint LastAlternativeOwner, uint LastAlternativeResult,
    uint LastUiRecipe, uint LastUiVanillaAmount, uint LastUiCap,
    uint InstalledFlags);

/// <summary>One mod's rules for a scope. Dispose it to take them away.</summary>""")
print("PASS: optional managed craft/payment trace API added; older Kiln falls back to null.")
