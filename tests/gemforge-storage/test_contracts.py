"""Source/API contract checks; in-game inventory movement needs a real Enshrouded world."""
import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = (ROOT / "GemForgeStorage/GemForgeStorageMod.cs").read_text()
PROJECT = (ROOT / "GemForgeStorage/GemForgeStorage.csproj").read_text()


def returnable(carried, originally_owned, remaining_loan):
    return min(max(0, remaining_loan), max(0, carried - originally_owned))


class ConservationPolicy(unittest.TestCase):
    def test_loan_baseline_and_partial_spend(self):
        self.assertEqual(returnable(55, 5, 50), 50)
        self.assertEqual(returnable(43, 5, 50), 38)
        self.assertEqual(returnable(5, 5, 50), 0)
        self.assertEqual(returnable(2, 5, 50), 0)

    def test_do_not_return_more_than_loan(self):
        self.assertEqual(returnable(200, 10, 8), 8)
        self.assertEqual(returnable(0, 0, 12), 0)

    def test_source_uses_kiln_item_transfer_and_no_creation(self):
        self.assertIn("Items.Move(", SRC)
        self.assertNotIn("Items.Give(", SRC)
        self.assertNotIn("Items.Take(", SRC)
        self.assertNotIn("Spawn.Item(", SRC)
        self.assertIn('ItemCategory.WeaponGem', SRC)
        self.assertIn('"_Fognest_Ressource"', SRC)
        self.assertIn('EndsWith("_Magic"', SRC)
        self.assertIn('Nearby.Entities(player, ReachMeters, "ShroudForge")', SRC)
        self.assertIn('Spawn.FindByName("GemForge"', SRC)
        self.assertIn('EndsWith("_Player_GemForge"', SRC)
        self.assertIn('World.Find("UsedItem")', SRC)
        self.assertIn('World.TryRead<uint>(entity, "UsedItem"', SRC)
        self.assertIn("3250908839u", SRC)
        self.assertIn("gforge probe", SRC)
        self.assertIn("private const double ReachMeters = 8", SRC)
        self.assertIn("s.ItemEntity == loan.Plan.Instance", SRC)
        self.assertIn("session.Original.GetValueOrDefault", SRC)

    def test_only_server_uses_items_move_api(self):
        self.assertRegex(PROJECT, r"<KilnSide>server</KilnSide>")
        self.assertIn("<KilnServices>Items;World;Spawn;Players;Chat;Saves</KilnServices>", PROJECT)
        self.assertIn('Kiln.Sdk/0.2.0-dev.28', PROJECT)
        self.assertRegex(SRC, r"internal static int Returnable\(")


if __name__ == "__main__":
    unittest.main()
