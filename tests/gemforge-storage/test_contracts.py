"""Contract and production-safety checks for native Gem Forge Magic Storage.

Compiler and managed API correctness are verified by the official Kiln SDK
build in CI. The authoritative item transaction is Kiln's implementation.
"""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = (ROOT / "GemForgeStorage/GemForgeStorageMod.cs").read_text()
PROJECT = (ROOT / "GemForgeStorage/GemForgeStorage.csproj").read_text()
WORKFLOW = (ROOT / ".github/workflows/gemforge-storage.yml").read_text()
README = (ROOT / "GemForgeStorage/README.md").read_text()


class NativeForgeProduction(unittest.TestCase):
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
        self.assertIn("<Version>1.0.0</Version>", PROJECT)
        self.assertIn("<KilnSide>server</KilnSide>", PROJECT)
        self.assertIn("<KilnClients>none</KilnClients>", PROJECT)
        self.assertIn("Abilities;Stations;Items;Spawn;Players", PROJECT)
        self.assertNotIn(";Chat", PROJECT)

    def test_no_console_commands_or_manual_switch(self):
        for forbidden in (
            "Commands.Register(", "CommandRegistration", "CommandPermission",
            "CommandContext", "CommandException", "command.Reply(", "gforge",
            "_enabled", "_lastGemCount", "_events", "ui-watch",
        ):
            with self.subTest(token=forbidden):
                self.assertNotIn(forbidden, SRC)
        self.assertIn("Events.On<GemForgeOpenedEvent>(OnForgeOpened)", SRC)
        self.assertNotIn("gforge on", README)

    def test_official_native_event_and_offer(self):
        self.assertIn("Gems.OfferChestGems(e.Player, chest, SourceOptions)", SRC)
        self.assertIn("GemSourceOptions", SRC)
        self.assertIn("Gems.SourcesCapability", SRC)
        self.assertIn("Gems.PayersCapability", SRC)

    def test_permission_base_and_forge_policy(self):
        for token in (
            "Players.Find(player)", "PlayerPermissions.AccessInventories",
            "Bases.Of(player)", "Bases.Of(e.Forge)?.Id != baseId",
            "baseInfo.Id.IsNone", "Bases.Storage(baseId)",
            'EndsWith("_Magic"', "ItemCategory.WeaponGem",
        ):
            with self.subTest(token=token):
                self.assertIn(token, SRC)

    def test_reoffer_limits_and_cleanup(self):
        for token in (
            "MaxGemOffersPerPlayer = 256", "MaxGemOffersPerPlayer - count",
            "Lifetime = TimeSpan.FromSeconds(10)",
            "_offered.TryGetValue(e.Player", "next.Contains(chest)",
            "Gems.WithdrawChestGems(player)", "Gems.WithdrawChestGems(e.Player, chest)",
            "_offered.Clear()",
        ):
            with self.subTest(token=token):
                self.assertIn(token, SRC)

    def test_payment_and_no_unsafe_recycle_return(self):
        self.assertIn("PayFrom = GemPayer.PlayerThenBaseStorage", SRC)
        self.assertNotIn("ReturnTo = chest", SRC)
        self.assertNotIn("GemCraftOptions.Cost", SRC)
        self.assertNotIn("GemPayer.From(chest)", SRC)

    def test_production_artifact_and_one_time_release(self):
        self.assertIn("gemforge-magic-storage-1.0.0", WORKFLOW)
        self.assertIn("gemforge-magic-storage-v1.0.0", WORKFLOW)
        self.assertIn("gh release view", WORKFLOW)
        self.assertIn("gh release create", WORKFLOW)
        self.assertIn("github.ref == 'refs/heads/main'", WORKFLOW)


if __name__ == "__main__":
    unittest.main()
