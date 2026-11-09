#!/usr/bin/env python3
"""CI safety gate: experimental crafting trace mod is an optional observer.

This scan is defense in depth; it does not claim native runtime safety.
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
mod = root / "CraftPaymentTrace"
csproj = ET.parse(mod / "CraftPaymentTrace.csproj").getroot()
get = lambda name: csproj.findtext(f"./PropertyGroup/{name}")
assert get("KilnModId") == "craft-payment-trace"
assert get("Version") == "0.1.0"
assert get("KilnSide") == "server"
assert get("KilnClients") == "none"
assert set((get("KilnServices") or "").split(";")) == {"Chat", "Rules"}
mod_src = (mod / "CraftPaymentTraceMod.cs").read_text(encoding="utf-8")
reader = (mod / "TraceReader.cs").read_text(encoding="utf-8")
assert 'Commands.Register(' in mod_src
assert '"cbtrace"' in mod_src and "CommandPermission.Admin" in mod_src
assert '"status"' in mod_src and '"cost"' in mod_src and '"journal"' in mod_src
assert 'typeof(Rules)' in mod_src
assert '"TraceRecipeCostRows"' in reader
assert '"TraceRecipeJournal"' in reader
assert '"TraceCraftPayments"' in reader
assert '"UNAVAILABLE:' in reader
assert "Status(ulong status)" in reader
assert 'status is 0 or 2' in reader
assert "status == 2 || status == 0" in reader
assert "if (output.Count >= 8) break;" in reader
assert (root / "tests/craft-payment-trace/Program.cs").is_file()
assert (root / "tests/craft-payment-trace/Trace.Tests.csproj").is_file()
# Neither original crafting engine, native memory, player inventory nor world
# state may be altered by these C# sources.
for snippet in (
    r"\bItems\s*\.\s*(?:Give|Take|TakeSlot|Move|MoveAll)\s*\(",
    r"\bGameplay\s*\.\s*Inject\s*\.",
    r"\bRules\s*\.\s*Apply\s*\(",
    r"\bSpawn\s*\.\s*(?:Create|Destroy)\s*\(",
    r"\bWorld\s*\.\s*(?:TryWrite|QueueAdd|QueueRemove)\s*\(",
    r"\b(?:Marshal|Unsafe|NativeMemory|DllImport|LibraryImport|Hooks|Detour)\b",
    r"\b(?:WriteAllText|WriteAllBytes|FileStream|Directory.CreateDirectory)\b",
):
    assert not re.search(snippet, mod_src + "\n" + reader), f"Unexpected native/mutating API: {snippet}"
print("PASS: optional cbtrace command is read-only, bounded, administrator-only and uses normal Kiln SDK.")
