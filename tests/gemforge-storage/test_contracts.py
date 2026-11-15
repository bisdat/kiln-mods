"""Contract tests for the read-only Gem Forge research package.

These checks cannot establish which native routine the Gem Forge uses; in-game
observation is required before any write-capable implementation.
"""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = (ROOT / "GemForgeStorage/GemForgeStorageMod.cs").read_text()
PROJECT = (ROOT / "GemForgeStorage/GemForgeStorage.csproj").read_text()

class ResearchMode(unittest.TestCase):
    def test_no_loans_or_item_changes(self):
        for forbidden in (
            "Items.Move(", "Items.Give(", "Items.Take(", "Items.TakeSlot(",
            "Spawn.Item(", "World.Write(", "e.Cancel(", "Hooks.Detour(",
            "Hooks.Mid(", "Memory.Write(", "Memory.Patch(", "BorrowNext(",
            "ReturnNext(", "return-any"
        ):
            self.assertNotIn(forbidden, SRC)

    def test_observe_native_gem_action_passively(self):
        self.assertIn('e.Name != "weaponGemAction"', SRC)
        self.assertIn("PlayerClientActionEvent", SRC)
        self.assertIn("Events.On<PlayerClientActionEvent>(OnGemAction)", SRC)
        self.assertIn("_actionSubscription?.Dispose()", SRC)
        self.assertIn("BinaryPrimitives.ReadUInt32LittleEndian", SRC)
        self.assertIn("Convert.ToHexString(bytes)", SRC)
        self.assertIn("private const int MaxHistory = 40", SRC)

    def test_verified_forge_and_loaded_magic_storage(self):
        self.assertIn("3250908839u", SRC)
        self.assertIn('World.Find("UsedItem")', SRC)
        self.assertIn('World.TryRead<uint>(entity, "UsedItem"', SRC)
        self.assertIn('InventoryKinds.Containers | InventoryKinds.NonEmpty', SRC)
        self.assertIn('EndsWith("_Magic"', SRC)
        self.assertIn("ItemCategory.WeaponGem", SRC)

    def test_server_only_and_no_advanced_hooking(self):
        self.assertRegex(PROJECT, r"<KilnSide>server</KilnSide>")
        self.assertIn("<KilnServices>Items;World;Spawn;Players;Chat</KilnServices>", PROJECT)
        self.assertIn('Kiln.Sdk/0.2.0-dev.28', PROJECT)
        self.assertIn("<Version>0.2.0</Version>", PROJECT)
        self.assertNotIn("<KilnAdvanced>true</KilnAdvanced>", PROJECT)

if __name__ == "__main__":
    unittest.main()
