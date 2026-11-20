"""Staff charge-time prototype: gameplay acceptance is still required.

Compilation against the official Kiln SDK is the authoritative API check;
unit tests exercise the exact arithmetic and selector used by the mod.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
MOD = (ROOT / "SurvivalQoL" / "SurvivalQoLMod.cs").read_text(encoding="utf-8")
CONFIG = (ROOT / "SurvivalQoL" / "SurvivalQoLConfig.cs").read_text(encoding="utf-8")
SCALE = (ROOT / "SurvivalQoL" / "StaffChargeScale.cs").read_text(encoding="utf-8")
PROJECT = (ROOT / "SurvivalQoL" / "SurvivalQoL.csproj").read_text(encoding="utf-8")
TEST_PROJECT = (ROOT / "tests" / "survival-qol" / "DepletionMathTests" / "DepletionMathTests.csproj").read_text(encoding="utf-8")


class StaffCastingContracts(unittest.TestCase):
    def test_slider_defaults_to_vanilla_and_is_independent(self):
        self.assertIn("public int StaffCastingSpeedMultiplier { get; set; } = 1;", CONFIG)
        self.assertIn("[Range(1, 10)]", CONFIG)
        self.assertIn("options.InfiniteMana", MOD)
        self.assertNotIn("options.InfiniteMana && options.StaffCastingSpeedMultiplier", MOD)
        self.assertIn("0.2.4", PROJECT)

    def test_uses_the_supported_gamedata_api_and_restores_ownership(self):
        self.assertIn("Content", PROJECT)
        self.assertIn("Content.OnReady", MOD)
        self.assertIn("Content.GameDataAvailable", MOD)
        self.assertIn("Content.ItemData", MOD)
        self.assertIn("item.Config(StaffChargeScale.ChargeTimeConfigId)", MOD)
        self.assertIn("charge.Original().AsDouble", MOD)
        self.assertIn("charge.Reset()", MOD)
        self.assertIn("charge.Set(StaffChargeScale.ScaledSeconds(original, multiplier))", MOD)
        self.assertIn("ApplyStaffCastingSpeed();", MOD)

    def test_scope_and_implementation_are_conservative(self):
        self.assertIn("0x579b2657", SCALE)
        self.assertIn('name.StartsWith("Ammo_"', SCALE)
        self.assertIn('name.Contains("_Spell_"', SCALE)
        self.assertNotIn("Content.Sequences", MOD)
        self.assertNotIn("Content.Templates", MOD)
        self.assertNotIn("Kiln.Attributes.TrySet", SCALE)
        self.assertIn("StaffChargeScale.cs", TEST_PROJECT)


if __name__ == "__main__":
    unittest.main()
