#!/usr/bin/env python3
"""Safety smoke checks for the read-only Cross-base Storage SDK prototype.

This does NOT validate cross-base crafting (which is not yet implemented).
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
src = (root / "CrossBaseStorage/CrossBaseStorageMod.cs").read_text(encoding="utf-8")
guide = (root / "CrossBaseStorage/README.md").read_text(encoding="utf-8")
project = ET.parse(root / "CrossBaseStorage/CrossBaseStorage.csproj").getroot()

assert project.findtext("./PropertyGroup/KilnModId") == "cross-base-storage"
assert project.findtext("./PropertyGroup/KilnSide") == "server"
assert project.findtext("./PropertyGroup/KilnClients") == "none"
assert project.findtext("./PropertyGroup/KilnServices") == "Items;World;Spawn;Saves;Chat;Gameplay;Rules"

assert "CommandPermission.Admin" in src
assert "Items.FindInventories(InventoryKinds.Containers)" in src
assert "Items.Read(" in src
assert "Spawn.Inspect(" in src
assert "Saves.WorldId" in src
assert "Gameplay.Recipe(" in src
assert "Gameplay.WorkshopRecipes(0)" in src
assert "Gameplay.Enable()" in src
assert "[Subscribe(EventPriority.Monitor" in src
assert "CraftEvent e)" in src
assert "ItemCraftedEvent e)" in src
assert "BuildEvent e)" in src
assert "case \"plan\":" in src
assert "case \"lastcraft\":" in src
assert "case \"lastbuild\":" in src
assert "case \"quote\":" in src
assert 'case "handoff": Handoff(c);' in src
assert "CraftHandoffPlanner.Inspect(" in src
assert "report.NativePaymentPermitted" in src
assert "loadedStorage=" in src and "linkedInventoryIds=" in src
assert "Flame Altar/base entitlement UNVERIFIED" in src
assert "original RecipeApply atomic rollback/commit" in src
assert 'CraftPaymentTrace.TraceReader.Read(typeof(Rules), "pair")' in src
handoff_source = (root / "CrossBaseStorage/CraftHandoffPlanner.cs").read_text(encoding="utf-8")
assert "public bool NativePaymentPermitted => false;" in handoff_source
assert "HandoffStatus.UnknownIngredients" in handoff_source
assert "HandoffStatus.MissingMaterials" in handoff_source
assert "Array.Empty<QuoteDebit>()" in handoff_source
assert (root / "tests/craft-handoff/Handoff.Tests.csproj").is_file()

assert "case \"links\":" in src
assert "case \"resolve\":" in src
assert 'case "trace": Trace(c);' in src
assert 'CraftPaymentTrace.TraceReader.Read(typeof(Rules), action)' in src
assert 'CraftPaymentTrace.TraceReader.Read(typeof(Rules), mode)' in src
assert 'Do not replace your automatically updated production runtime.' in src
trace_lib = (root / "CraftPaymentTrace/TraceReader.cs").read_text(encoding="utf-8")
assert 'Compile Include="../CraftPaymentTrace/TraceReader.cs"' in (root / "CrossBaseStorage/CrossBaseStorage.csproj").read_text(encoding="utf-8")
assert '"pair" => "TraceRecipePayment"' in trace_lib
assert "FormatPair(object snapshot)" in trace_lib
assert "status is not (1 or 3)" in trace_lib
assert "Marshal" not in trace_lib and "DllImport" not in trace_lib
assert "Items.Take(" not in trace_lib and "Items.Give(" not in trace_lib
assert "LoadedLinkIndex(chests)" in src
assert "InventoryLinkIndex.Build(" in src
assert (root / "CrossBaseStorage/InventoryLinkIndex.cs").exists()
# Magic Chest recognition must use the exact _Magic suffix, independent of
# the number of slots. No broad Contains("Magic") or 72-slot upper bound.
classifier = (root / "CrossBaseStorage/StorageTemplate.cs").read_text(encoding="utf-8")
assert 'EndsWith("_Magic", StringComparison.OrdinalIgnoreCase)' in classifier
assert "Contains(\"Magic\"" not in classifier
assert "actualSlots > 0" in classifier
assert "actualSlots <= 72" not in classifier
assert "StorageTemplate.IsStorageCandidate(info?.TemplateName, inventory.GenericSlots)" in src
assert "StorageTemplate.IsMagic(template)" in src
assert "StorageTemplate.IsMagic(group.Key)" in src
assert "private static bool MagicByName" not in src
assert "slots is >= 16 and <= 72" not in src
assert "MaterialQuotePlanner.QuoteExact(" in src
assert (root / "CrossBaseStorage/MaterialQuotePlanner.cs").exists()
assert "No craft request was recorded" in src
assert "UNRESOLVED CATEGORY/PLACEHOLDER ingredient" in src
assert "A raw count of 0 does NOT establish zero material cost" in src
assert "INCOMPLETE RECIPE INPUT DATA" in src
assert "cbstore recipes [output name]" in src
assert project.findtext("./PropertyGroup/Version") == "0.1.9"
assert re.fullmatch(r"0\.1\.\d+", project.findtext("./PropertyGroup/Version") or "")

# This phase is explicitly non-mutating: no give, take, teleport, mutate,
# vanilla recipe injection, or raw component writes.
all_code = "\\n".join(p.read_text(encoding="utf-8") for p in (root / "CrossBaseStorage").glob("*.cs")) + "\\n" + trace_lib
for token in (
    r"\bItems\s*\.\s*(?:Give|Take|TakeSlot|GiveRecipeResult)\s*\(",
    r"\bWorld\s*\.\s*(?:TryWrite|QueueAdd|QueueRemove)\s*\(",
    r"\bGameplay\s*\.\s*Inject\s*\.",
    r"\bSpawn\s*\.\s*(?:Destroy|Create)\s*\(",
):
    assert re.search(token, all_code) is None, f"Mutating operation in discovery phase: {token}"

for text in ("read-only", "not", "building", "crafting", "loaded", "magic"):
    assert text in guide.lower(), f"README must explain limitation: {text}"

print("PASS: Cross-base Storage 0.1.9 has read-only chest/recipe/link diagnostics, " +
      "native craft/build event observation and no inventory mutation calls")
