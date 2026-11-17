"""Source-level safety and API-contract checks for the experimental Gem Forge mod.

These do not replace compilation against Kiln's official SDK or in-game
inventory conservation testing. The native transaction belongs to Kiln.
"""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = (ROOT / "GemForgeStorage/GemForgeStorageMod.cs").read_text()
PROJECT = (ROOT / "GemForgeStorage/GemForgeStorage.csproj").read_text()
WORKFLOW = (ROOT / ".github/workflows/gemforge-storage.yml").read_text()


class NativeForgeIntegration(unittest.TestCase):
    def test_no_manual_item_movement_or_native_patching(self):
        for forbidden in (
            "Items.Move(", "Items.Give(", "Items.Take(", "Items.TakeSlot(",
            "Spawn.Item(", "World.Write(", "e.Cancel(", "Hooks.Detour(",
            "Hooks.Mid(", "Memory.Write(", "Memory.Patch(", "BorrowNext(",
            "ReturnNext(", "GemForgeCandidates(", "SubscribeSlots(",
        ):
            with self.subTest(operation=forbidden):
                self.assertNotIn(forbidden, SRC)

    def test_official_sdk_and_server_only_manifest(self):
        self.assertIn('Sdk/0.2.0-dev.29.15', PROJECT)
        self.assertIn("<Version>0.3.0</Version>", PROJECT)
        self.assertIn("<KilnSide>server</KilnSide>", PROJECT)
        self.assertIn("<KilnClients>none</KilnClients>", PROJECT)
        self.assertIn("Abilities;Stations;Items;Spawn;Players;Chat", PROJECT)

    def test_real_native_forge_event_and_offer(self):
        self.assertIn("Events.On<GemForgeOpenedEvent>(OnForgeOpened)", SRC)
        self.assertIn("Gems.OfferChestGems(e.Player, chest,", SRC)
        self.assertIn("GemSourceOptions", SRC)
        self.assertIn("Gems.SourcesCapability", SRC)
        self.assertIn("Gems.PayersCapability", SRC)

    def test_opt_in_toggle_and_withdraw(self):
        self.assertIn("private bool _enabled;", SRC)
        self.assertIn("if (!_enabled) return;", SRC)
        self.assertIn('case "on":', SRC)
        self.assertIn('case "off":', SRC)
        self.assertIn("Gems.WithdrawChestGems(player)", SRC)
        self.assertIn("Gems.WithdrawChestGems(e.Player, chest)", SRC)
        self.assertIn("_offered.Clear()", SRC)

    def test_permission_base_and_forge_policy(self):
        self.assertIn("Players.Find(player)", SRC)
        self.assertIn("PlayerPermissions.AccessInventories", SRC)
        self.assertIn("Bases.Of(player)", SRC)
        self.assertIn("Bases.Of(e.Forge)?.Id != baseId", SRC)
        self.assertIn("baseInfo.Id.IsNone", SRC)
        self.assertIn("Bases.Storage(baseId)", SRC)
        self.assertIn('EndsWith("_Magic"', SRC)
        self.assertIn("ItemCategory.WeaponGem", SRC)

    def test_reoffer_limits_and_expiry(self):
        self.assertIn("MaxGemOffersPerPlayer = 256", SRC)
        self.assertIn("MaxGemOffersPerPlayer - count", SRC)
        self.assertIn("Lifetime = TimeSpan.FromSeconds(10)", SRC)
        self.assertIn("_offered.TryGetValue(e.Player", SRC)
        self.assertIn("next.Contains(chest)", SRC)

    def test_payment_and_no_unsafe_recycle_return(self):
        self.assertIn("PayFrom = GemPayer.PlayerThenBaseStorage", SRC)
        self.assertNotIn("ReturnTo = chest", SRC)
        self.assertNotIn("GemCraftOptions.Cost", SRC)
        self.assertNotIn("GemPayer.From(chest)", SRC)

    def test_test_only_artifact_no_release_without_tag(self):
        self.assertIn("gemforge-magic-storage-0.3.0-TEST", WORKFLOW)
        self.assertIn("refs/tags/gemforge-magic-storage-v", WORKFLOW)
        self.assertIn("gemforge-magic-storage-0.3.0.zip", WORKFLOW)


if __name__ == "__main__":
    unittest.main()
