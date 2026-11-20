// SPDX-License-Identifier: MIT
namespace Bisdat.SurvivalQoL;

/// <summary>
/// Pure calculations for the experimental staff-spell charge-time control.
/// Spell ammunition is separate from wands' own attack sequences.
/// </summary>
internal static class StaffChargeScale
{
    // Kiln's Enshrouded spell-data research names this Impact config "Charge Time".
    internal const string ChargeTimeConfigId = "0x579b2657";

    internal static bool IsSpellAmmunition(string name) =>
        name.StartsWith("Ammo_", StringComparison.OrdinalIgnoreCase)
        && name.Contains("_Spell_", StringComparison.OrdinalIgnoreCase);

    internal static double ScaledSeconds(double vanillaSeconds, int multiplier)
    {
        if (multiplier is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(multiplier));
        if (!double.IsFinite(vanillaSeconds) || vanillaSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(vanillaSeconds));
        return vanillaSeconds / multiplier;
    }
}
