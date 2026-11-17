"""Source contract checks. Compilation and in-game acceptance remain separate."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = (ROOT / "BaseStationMagicStorage" / "BaseStationMagicStorageMod.cs").read_text()
PROJ = (ROOT / "BaseStationMagicStorage" / "BaseStationMagicStorage.csproj").read_text()
README = (ROOT / "BaseStationMagicStorage" / "README.md").read_text()

class Contracts(unittest.TestCase):
    def test_disabled_default_and_admin_commands(self):
        self.assertIn("public bool Enabled { get; set; }", SRC)
        self.assertIn('case "enable":', SRC)
        self.assertIn('case "disable":', SRC)
        self.assertIn('case "station":', SRC)
        self.assertIn('case "all":', SRC)
        self.assertIn("public bool AllStations { get; set; }", SRC)
        self.assertIn("public List<string> Included { get; set; } = [];", SRC)
        self.assertIn("!p.AllStations && !p.Included.Contains", SRC)
        self.assertIn("CommandPermission.Admin", SRC)
        self.assertIn("Items.ContainerNamesAvailable", SRC)
        self.assertIn("OutputName(Items.ContainerName(chest))", SRC)
        self.assertIn('"BSMS Output"', SRC)
        self.assertIn("Stations.All()", SRC)
        self.assertIn("Bases.Storage(@base, deposit: true)", SRC)
    def test_only_supported_native_actions(self):
        for name in ("Stations.Plan(id, options)", "Stations.Fill(id, options)", "Stations.Collect(id, chest, item)", "Player is intentionally absent"):
            self.assertIn(name, SRC)
        for forbidden in ("Stations.CollectToBase(", "Items.Move(", "Items.Take(", "Items.Give(", "Gems.Socket(", "Stations.Select(", "Stations.Stop(", "Stations.Resume("):
            self.assertNotIn(forbidden, SRC)
    def test_stop_world_and_capacity_safety(self):
        for token in ("s.StoppedByPlayer", "s.StopPending", "s.SelectedRecipe == 0",
                      "s.CraftsLoaded >= p.TargetCrafts", "WorldId() != world",
                      "s.Base != @base", "MaxStationsPerScan = 128",
                      "Take(2).ToArray()", "File.Move(temp", "Saves.WorldId"):
            self.assertIn(token, SRC)
    def test_sdk(self):
        self.assertIn("Kiln.Sdk/0.2.0-dev.29.14", PROJ)
        self.assertIn("<KilnSide>server</KilnSide>", PROJ)
        self.assertIn("**Default OFF.**", README)
        self.assertIn("not cross-base", README)
        self.assertIn("FactoryStation", README)
        self.assertIn("BSMS Output", README)
