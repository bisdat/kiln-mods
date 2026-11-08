#!/usr/bin/env python3
"""Experimental matching managed bindings; not for user's stock Kiln installation."""
from pathlib import Path
import subprocess
import sys

PIN = "eba9a5e1e0e0044a9146fdd53021a751e2957030"
if len(sys.argv) != 2:
    raise SystemExit("usage: managed.py <pinned-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
sha = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
if sha != PIN:
    raise SystemExit(f"Refusing unsupported upstream commit {sha}; expected {PIN}")

def replace(path, before, after):
    target = root / path
    text = target.read_text()
    if text.count(before) != 1:
        raise RuntimeError(f"Missing/ambiguous upstream anchor in {path}: {before[:80]}")
    target.write_text(text.replace(before, after, 1))
    print("PATCH", path)

replace("managed/services/Kiln.Gameplay/Native.cs",
"""    [StructLayout(LayoutKind.Sequential)]
    public struct Table
""",
"""    // Exactly the value snapshot added by the optional native tail.
    [StructLayout(LayoutKind.Sequential)]
    public struct RawRecipe
    {
        public uint Size, Id, InputCount, Stride;
        public fixed byte Inputs[384];  // at most 8 x 48
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Table
""")

replace("managed/services/Kiln.Gameplay/Native.cs",
"""    private static Table* _table;""",
"""    // Existing optional tail: EntityFilter, SpawnReplace, SpawnReplaceClear.
    // This is fourth. Older runtimes never call into an absent function.
    public static delegate* unmanaged[Cdecl]<uint, RawRecipe*, int> RecipeRawInputs(Table* t) =>
        t != null && t->Header.Size >= (uint)(sizeof(Table) + 4 * sizeof(nint))
            ? *(delegate* unmanaged[Cdecl]<uint, RawRecipe*, int>*)((byte*)t + sizeof(Table) + 3 * sizeof(nint))
            : null;

    private static Table* _table;""")

replace("managed/services/Kiln.Gameplay/Gameplay.cs",
"""/// <summary>Native status of kiln.gameplay.</summary>""",
"""/// <summary>A detached copy of an unparsed native 48-byte recipe input.
— No ingredient-category meaning is asserted by this research-only API.</summary>
public sealed record GameplayRawRecipeInput(int Index, ReadOnlyMemory<byte> Bytes);

/// <summary>Unparsed copies of all input records (up to eight) from one recipe.</summary>
public sealed record GameplayRawRecipe(uint Id, IReadOnlyList<GameplayRawRecipeInput> Inputs);

/// <summary>Native status of kiln.gameplay.</summary>""".replace("—", "///"))

replace("managed/services/Kiln.Gameplay/Gameplay.cs",
"""    /// <summary>The recipe ids a workshop (a station kind; 0 = hand crafting) offers. Empty when unknown. Tick thread.</summary>""",
"""    /// <summary>Experimental read-only copy of the game's 48-byte recipe input
    /// records. No category offsets or quantity semantics are claimed.
    /// Requires an explicitly matched patched native Kiln service. Tick thread.</summary>
    public static GameplayRawRecipe? RawRecipe(uint id)
    {
        var t = Require();
        Services.RequireTickThread("Gameplay.RawRecipe");
        var fn = GameplayNative.RecipeRawInputs(t);
        if (fn == null)
            throw new NotSupportedException("Raw recipe-input diagnostic API is not installed.");
        GameplayNative.RawRecipe native = default;
        native.Size = (uint)sizeof(GameplayNative.RawRecipe);
        var rc = fn(id, &native);
        if (rc == 2) return null;
        Check(rc, "Gameplay.RawRecipe");
        if (native.Stride != 48 || native.InputCount > 8)
            throw new InvalidOperationException("Unrecognized raw recipe-input layout.");
        var inputs = new GameplayRawRecipeInput[native.InputCount];
        for (var i = 0; i < inputs.Length; ++i)
        {
            var value = new byte[48];
            for (var b = 0; b < value.Length; ++b)
                value[b] = native.Inputs[i * 48 + b];
            inputs[i] = new GameplayRawRecipeInput(i, value);
        }
        return new GameplayRawRecipe(native.Id, inputs);
    }

    /// <summary>The recipe ids a workshop (a station kind; 0 = hand crafting) offers. Empty when unknown. Tick thread.</summary>""")
print("PASS managed optional API added (old-runtime fail-closed guard).")
