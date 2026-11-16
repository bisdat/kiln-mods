"""Source contracts for a prototype that changes world door state."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = (ROOT / "AutomaticDoors" / "AutomaticDoorsMod.cs").read_text()
CONFIG = (ROOT / "AutomaticDoors" / "AutomaticDoorsConfig.cs").read_text()
PROJECT = (ROOT / "AutomaticDoors" / "AutomaticDoors.csproj").read_text()

class DoorContracts(unittest.TestCase):
    def test_not_a_generic_switch(self):
        self.assertIn('World.Has(door, "DirectionalInteraction")', SOURCE)
        self.assertIn('World.Has(door, "InteractionToggle")', SOURCE)
    def test_only_tracked_server_writes(self):
        self.assertIn("World.Update<Ecs.ServerToggleState>", SOURCE)
        self.assertNotIn("World.WriteRaw", SOURCE)
        self.assertNotIn("World.WriteAt", SOURCE)
        self.assertIn("<KilnSide>server</KilnSide>", PROJECT)
    def test_players_and_friendly_npcs(self):
        self.assertIn("Players.All", SOURCE)
        self.assertIn('World.Find("DynamicActiveNpcState")', SOURCE)
        self.assertIn("OpenForPlayers", CONFIG)
        self.assertIn("OpenForNpcs", CONFIG)
    def test_discovery_stage_counters(self):
        for text in ('World.Find("DirectionalInteraction")', 'switches.Count',
                     '_paired++', '_withPosition++', '_readable++'):
            self.assertIn(text, SOURCE)

    def test_close_only_own_after_hysteresis(self):
        self.assertIn("_owned.TryGetValue", SOURCE)
        self.assertIn("if (nearClose)", SOURCE)
        self.assertIn("now - lastNear < delay", SOURCE)
        self.assertIn("Math.Max(openRadius + 0.5", SOURCE)

if __name__ == "__main__":
    unittest.main()
