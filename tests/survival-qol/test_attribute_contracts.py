"""Source-contract regressions for Survival QoL's Kiln attribute integration.

The CI SDK build checks API compatibility. These tests protect the wiring and
configuration semantics; actual oxygen/frost behaviour needs in-game testing.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
MOD = (ROOT / "SurvivalQoL" / "SurvivalQoLMod.cs").read_text(encoding="utf-8")
CONFIG = (ROOT / "SurvivalQoL" / "SurvivalQoLConfig.cs").read_text(encoding="utf-8")
PROJECT = (ROOT / "SurvivalQoL" / "SurvivalQoL.csproj").read_text(encoding="utf-8")
BUILD = (ROOT / "scripts" / "build.sh").read_text(encoding="utf-8")


class SurvivalAttributeContracts(unittest.TestCase):
    def test_new_options_are_independent_and_default_on(self):
        for name in ("InfiniteUnderwaterBreath", "InfiniteFrostProtection"):
            self.assertRegex(CONFIG, rf"public bool {name} \\{{ get; set; \\}} = true;")
            self.assertIn("options." + name, MOD)

    def test_legacy_disabled_toggles_remain_gates_without_world_writes(self):
        self.assertIn(
            "options.InfiniteUnderwaterBreath && options.DoubleUnderwaterBreath", MOD
        )
        self.assertIn(
            "options.InfiniteFrostProtection && options.DoubleFrostResistance", MOD
        )
        for setting in ("playerDivingTimeFactor", "playerBodyHeatFactor"):
            self.assertNotIn(setting, MOD)
        self.assertNotIn("_diving.Sync(", MOD)
        self.assertNotIn("_cold.Sync(", MOD)

    def test_player_refills_run_in_pre_before_regeneration_return(self):
        self.assertIn("Context.Tick.Pre += OnPre;", MOD)
        start = MOD.index("private void OnPre(in TickInfo tick)")
        end = MOD.index("[Subscribe]", start)
        tick = MOD[start:end]
        regen_return = tick.index(
            "if (!options.FastHealthRegen && !options.FastManaRegen) return;"
        )
        self.assertLess(tick.index("RefillAttribute(player.Entity, GameAttribute.Oxygen)"), regen_return)
        self.assertLess(tick.index("RefillAttribute(player.Entity, GameAttribute.BodyHeat)"), regen_return)
        self.assertIn("foreach (var player in Players.All)", tick)

    def test_refill_uses_current_calculated_max_and_nonthrowing_write(self):
        self.assertIn("Kiln.Attributes.Available", MOD)
        self.assertRegex(MOD, r"Kiln\\.Attributes\\.Get\\(entity, attribute\\)")
        self.assertIn("Max: int max", MOD)
        self.assertIn("reading.Current != max", MOD)
        self.assertIn("Kiln.Attributes.TrySet(entity, attribute, max, out _)", MOD)
        self.assertNotIn("int.MaxValue", MOD)

    def test_only_durability_remains_a_world_setting(self):
        self.assertIn('WorldSettingOverride _durability = new("enableDurability", false)', MOD)
        self.assertIn("_durability.Restore(Log.Warn)", MOD)
        self.assertEqual(MOD.count("new WorldSettingOverride("), 0)  # named field uses target-typed new

    def test_pinned_sdk_includes_attribute_api(self):
        self.assertIn('Kiln.Sdk/0.2.0-dev.local.20261110.62101', PROJECT)
        self.assertIn('default_sdk_version="0.2.0-dev.local.20261110.62101"', BUILD)
        self.assertIn(
            'default_kiln_sha="346a3ae5de436db1ba64bbbf0a27f4b8d0ed5be5"',
            BUILD,
        )


if __name__ == "__main__":
    unittest.main()
