# SPDX-License-Identifier: MIT
"""Source-level safety checks; compiling and live-client testing are separate."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


class DebugToolsContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mod = (ROOT / "DebugToolsMod.cs").read_text()
        cls.config = (ROOT / "DebugToolsConfig.cs").read_text()
        cls.project = (ROOT / "DebugTools.csproj").read_text()

    def test_admin_only_and_player_scoped(self):
        self.assertIn("CommandPermission.Admin", self.mod)
        self.assertIn("context.Source.IsPlayer", self.mod)
        self.assertIn("Rules.Apply(player, rules", self.mod)
        self.assertNotIn("RuleScope.Everyone", self.mod)

    def test_defaults_off_by_command_only(self):
        self.assertIn("DebugBuildAction action = DebugBuildAction.Status", self.mod)
        self.assertIn("OFF until an admin uses /debugbuild on", self.mod)

    def test_separate_build_cursor_rules(self):
        for field in ("BuildOutsideBases", "BuildInShroud", "IgnoreNoBuildZones",
                      "IgnoreEnemiesNearby", "KeepBuildsOutsideBases"):
            self.assertIn(field + " =", self.mod)
        self.assertIn("IgnoreBasePermissions = null", self.mod)

    def test_no_material_duplication(self):
        self.assertIn("BuildCost = options.FreeBuilding ? 0.0 : null", self.mod)
        self.assertIn("BuildRefund = options.FreeBuilding ? 0.0 : null", self.mod)
        self.assertIn("public bool FreeBuilding { get; set; } = false", self.config)

    def test_explicit_client_and_rules_dependency(self):
        self.assertIn("<KilnClients>kiln</KilnClients>", self.project)
        self.assertIn("<KilnServices>Rules;Chat</KilnServices>", self.project)
        self.assertIn("Rules.ClientOf(player)", self.mod)
        self.assertIn("hooks.BuildingZone", self.mod)
        self.assertIn("hooks.Placement", self.mod)


if __name__ == "__main__":
    unittest.main()
