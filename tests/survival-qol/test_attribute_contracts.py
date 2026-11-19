"""Static integration contracts for Survival QoL duration multiplier sliders.

The separate DepletionMathTests console app executes the *same C# arithmetic*
used by the mod; the CI SDK/KilnPack build checks the actual managed API.
The real Enshrouded client still needs acceptance testing.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
MOD = (ROOT / "SurvivalQoL" / "SurvivalQoLMod.cs").read_text(encoding="utf-8")
CONFIG = (ROOT / "SurvivalQoL" / "SurvivalQoLConfig.cs").read_text(encoding="utf-8")
PROJECT = (ROOT / "SurvivalQoL" / "SurvivalQoL.csproj").read_text(encoding="utf-8")
BUILD = (ROOT / "scripts" / "build.sh").read_text(encoding="utf-8")
MATH = (ROOT / "SurvivalQoL" / "DepletionMath.cs").read_text(encoding="utf-8")
STAMINA = (ROOT / "SurvivalQoL" / "StaminaRegenScale.cs").read_text(encoding="utf-8")


class SurvivalAttributeContracts(unittest.TestCase):
    def test_two_independent_sliders_and_no_legacy_ui_options(self):
        for name in ("UnderwaterBreathMultiplier", "FrostResistanceMultiplier"):
            self.assertIn(f"public int {name} {{ get; set; }} = 10;", CONFIG)
            self.assertIn("options." + name, MOD)
        self.assertEqual(CONFIG.count("[Range(1, 10)]"), 3)
        for obsolete in (
            "DoubleUnderwaterBreath",
            "DoubleFrostResistance",
            "InfiniteUnderwaterBreath",
            "InfiniteFrostProtection",
        ):
            self.assertNotIn(obsolete, CONFIG)
            self.assertNotIn(obsolete, MOD)

    def test_global_2x_world_setting_overrides_are_removed(self):
        for old_setting in ("playerDivingTimeFactor", "playerBodyHeatFactor"):
            self.assertNotIn(old_setting, MOD)
        self.assertNotIn("_diving.Sync(", MOD)
        self.assertNotIn("_cold.Sync(", MOD)

    def test_server_tick_scaling_is_independent_of_regeneration(self):
        self.assertIn("Context.Tick.Pre += OnPre;", MOD)
        start = MOD.index("private void OnPre(in TickInfo tick)")
        end = MOD.index("private static void ScaleAttribute(", start)
        tick = MOD[start:end]
        self.assertLess(
            tick.index("ScaleAttribute(player.Entity, GameAttribute.Oxygen,"),
            tick.index("if (options.FastHealthRegen &&"),
        )
        self.assertLess(
            tick.index("ScaleAttribute(player.Entity, GameAttribute.BodyHeat,"),
            tick.index("if (options.FastHealthRegen &&"),
        )
        self.assertIn("foreach (var player in Players.All)", tick)
        self.assertIn("state.JoinedStep != player.JoinedStep", tick)
        self.assertIn("_recovery.Remove(entity)", tick)

    def test_scaler_uses_computed_maximum_and_never_writes_at_1x(self):
        self.assertIn("Kiln.Attributes.Available", MOD)
        self.assertIn("Kiln.Attributes.Get(entity, attribute)", MOD)
        self.assertIn("if (multiplier <= 1)", MOD)
        self.assertIn("state.Reset(); // 1x is strictly vanilla", MOD)
        self.assertIn("Kiln.Attributes.TrySet(entity, attribute, current + requested", MOD)
        self.assertIn("DepletionMath.Refund(lost, multiplier, state.Remainder)", MOD)
        self.assertIn("state.PreviousMax == max", MOD)
        self.assertIn("state.Multiplier == multiplier", MOD)
        self.assertIn("state.Remainder = 0;", MOD)
        self.assertNotIn("int.MaxValue", MOD)

    def test_integer_accounting_uses_64_bit_intermediate(self):
        self.assertIn("(long)lost * (multiplier - 1)", MATH)
        self.assertIn("numerator / multiplier", MATH)
        self.assertIn("numerator % multiplier", MATH)

    def test_only_durability_remains_world_setting_override(self):
        self.assertIn(
            'WorldSettingOverride _durability = new("enableDurability", false)',
            MOD,
        )
        self.assertIn("_durability.Restore(Log.Warn)", MOD)
        self.assertEqual(MOD.count("private readonly WorldSettingOverride"), 1)

    def test_stamina_regeneration_slider_is_separate_from_infinite_stamina(self):
        self.assertIn("public bool InfiniteStamina { get; set; } = true;", CONFIG)
        self.assertIn("public int StaminaRegenMultiplier { get; set; } = 1;", CONFIG)
        self.assertIn("options.StaminaRegenMultiplier > 1", MOD)
        self.assertIn("!options.InfiniteStamina &&", MOD)
        self.assertIn("if (!options.InfiniteStamina)", MOD)
        self.assertIn("state.Stamina.Reset();", MOD)
        self.assertIn("ScaleStaminaRegen(player.Entity, options.StaminaRegenMultiplier, state.Stamina)", MOD)
        self.assertIn("Kiln.Attributes.Get(entity, GameAttribute.Stamina)", MOD)
        self.assertIn("Kiln.Attributes.TrySet(entity, GameAttribute.Stamina, current + extra", MOD)
        self.assertIn("state.Extra(current, max, multiplier)", MOD)
        self.assertIn("state.Sample(current, max, multiplier)", MOD)
        self.assertIn("if (multiplier <= 1)", MOD)
        self.assertIn("current <= previous", STAMINA)
        self.assertIn("_previousMax != maximum || _previousMultiplier != multiplier", STAMINA)
        self.assertIn("(long)(current - previous) * (multiplier - 1)", STAMINA)
        self.assertIn("Math.Min(maximum - current, extra)", STAMINA)

    def test_health_and_mana_bonus_rates_default_to_unmodified_game(self):
        # The zero defaults are not native regeneration rates: they disable
        # only our *additional* HP and mana grants. Both remain opt-in sliders.
        self.assertIn("public int HealthRegenPerSecond { get; set; } = 0;", CONFIG)
        self.assertIn("public int ManaRegenPerSecond { get; set; } = 0;", CONFIG)
        self.assertEqual(CONFIG.count("[Range(0, 100)]"), 2)
        self.assertIn("public bool FastHealthRegen { get; set; } = true;", CONFIG)
        self.assertIn("public bool FastManaRegen { get; set; } = true;", CONFIG)
        self.assertIn(
            "if (options.FastHealthRegen && options.HealthRegenPerSecond > 0 && Combat.Available)",
            MOD,
        )
        self.assertIn(
            "if (options.FastManaRegen && options.ManaRegenPerSecond > 0)",
            MOD,
        )
        self.assertIn("else state.HealthRemainder = 0;", MOD)
        self.assertIn("else state.ManaRemainder = 0;", MOD)
        # Positive slider values must still add points to resources.
        self.assertIn("Combat.Heal(player.Entity, requested)", MOD)
        self.assertIn("Abilities.Attributes.Add(player.Entity, AbilityResource.Mana, requested)", MOD)

    def test_health_and_mana_default_change_has_distinct_library_version(self):
        self.assertIn("<Version>0.2.3</Version>", PROJECT)

    def test_pinned_sdk_includes_attribute_api(self):
        self.assertIn("Kiln.Sdk/0.2.0-dev.local.20261110.62101", PROJECT)
        self.assertIn('default_sdk_version="0.2.0-dev.local.20261110.62101"', BUILD)
        self.assertIn(
            'default_kiln_sha="346a3ae5de436db1ba64bbbf0a27f4b8d0ed5be5"',
            BUILD,
        )


if __name__ == "__main__":
    unittest.main()
