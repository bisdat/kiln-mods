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
        self.assertIn('"crafting output"', SRC)
        self.assertIn('"craftingoutput"', SRC)
        self.assertIn("name.Where(c => !char.IsWhiteSpace(c)).ToArray()", SRC)
        self.assertIn("StartsWith(OutputSinkKey, StringComparison.OrdinalIgnoreCase)", SRC)
        self.assertNotIn('"BSMS Output"', SRC)
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
    def test_output_chest_name_matching_contract(self):
        # Mirror the normalization rule used by OutputName, including suffixes.
        def matches(value):
            return "".join(c for c in value if not c.isspace()).casefold().startswith("craftingoutput")
        for accepted in (
            "crafting output", "CRAFTING OUTPUT", " Crafting  Output ",
            "craftingoutput2", "CRAFTING OUTPUT - Smelter",
            "cra ft ing out put extras", "c\tR a f t i n g\nO u t p u t#7",
        ):
            with self.subTest(accepted=accepted):
                self.assertTrue(matches(accepted))
        for rejected in ("BSMS Output", "ordinary chest", "output crafting", "crafting-output"):
            with self.subTest(rejected=rejected):
                self.assertFalse(matches(rejected))
    def test_sdk(self):
        self.assertIn("Kiln.Sdk/0.2.0-dev.29.14", PROJ)
        self.assertIn("<Version>0.1.2</Version>", PROJ)
        self.assertIn("<KilnSide>server</KilnSide>", PROJ)
        self.assertIn("**Default OFF.**", README)
        self.assertIn("not cross-base", README)
        self.assertIn("FactoryStation", README)
        self.assertIn("crafting output", README)
        self.assertIn("ignores case and all whitespace", README)
