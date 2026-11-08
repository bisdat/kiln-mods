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
assert project.findtext("./PropertyGroup/KilnServices") == "Items;World;Spawn;Saves;Chat"

assert "CommandPermission.Admin" in src
assert "Items.FindInventories(InventoryKinds.Containers)" in src
assert "Items.Read(" in src
assert "Spawn.Inspect(" in src
assert "Saves.WorldId" in src

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

print("PASS: Cross-base Storage alpha has admin-only, read-only chest diagnostics and no stock mutation calls")
