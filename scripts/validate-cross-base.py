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
assert project.findtext("./PropertyGroup/KilnServices") == "Items;World;Spawn;Saves;Chat;Gameplay"

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
assert project.findtext("./PropertyGroup/Version") == "0.1.0-alpha.2"

# This phase is explicitly non-mutating: no give, take, teleport, mutate,
# vanilla recipe injection, or raw component writes.
for token in (
    r"\bItems\s*\.\s*(?:Give|Take|TakeSlot|GiveRecipeResult)\s*\(",
    r"\bWorld\s*\.\s*(?:TryWrite|QueueAdd|QueueRemove)\s*\(",
    r"\bGameplay\s*\.\s*Inject\s*\.",
    r"\bSpawn\s*\.\s*(?:Destroy|Create)\s*\(",
):
    assert re.search(token, src) is None, f"Mutating operation in discovery phase: {token}"

for text in ("read-only", "not", "building", "crafting", "loaded", "magic"):
    assert text in guide.lower(), f"README must explain limitation: {text}"

print("PASS: Cross-base Storage alpha.2 has read-only chest/recipe diagnostics, " +
      "native craft/build event observation and no inventory mutation calls")
