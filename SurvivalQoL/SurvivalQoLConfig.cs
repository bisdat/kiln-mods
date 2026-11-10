// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.SurvivalQoL;

/// <summary>Hot-reloadable options written to kiln/config/survival-qol.json.</summary>
public sealed class SurvivalQoLConfig
{
    [Comment("Keep player stamina at 100% via Kiln.Rules.")]
    public bool InfiniteStamina { get; set; } = true;

    [Comment("Keep player mana at 100% via Kiln.Rules.")]
    public bool InfiniteMana { get; set; } = true;

    [Comment("Heal players at a fixed rate while injured. Does not prevent lethal damage.")]
    public bool FastHealthRegen { get; set; } = true;

    [Comment("Additional health points per second.")]
    [Range(0, 500)]
    public int HealthRegenPerSecond { get; set; } = 25;

    [Comment("Regenerate mana over time; useful if infinite mana is disabled.")]
    public bool FastManaRegen { get; set; } = true;

    [Comment("Additional mana points per second.")]
    [Range(0, 500)]
    public int ManaRegenPerSecond { get; set; } = 35;

    [Comment("Cancel fall damage for players, including the hard-landing stagger.")]
    public bool NoFallDamage { get; set; } = true;

    [Comment("Disable ALL equipment durability loss in the game world, not weapons only. This is a global game setting.")]
    public bool InfiniteWeaponDurability { get; set; } = true;

    [Comment("Hold the player's oxygen at its computed maximum using Kiln.Attributes (requires Kiln dev.20+).")]
    public bool InfiniteUnderwaterBreath { get; set; } = true;

    [Comment("Hold the player's body heat at its computed maximum; this is NOT the Shroud timer.")]
    public bool InfiniteFrostProtection { get; set; } = true;

    // Keep the old switches as compatibility gates: a user who explicitly set either
    // v0.1.x 2x option to false must not get infinite protection enabled on upgrade.
    // They no longer modify global world settings.
    [Comment("LEGACY v0.1.x compatibility gate: false also disables infinite breath. No longer sets diving time.")]
    public bool DoubleUnderwaterBreath { get; set; } = true;

    [Comment("LEGACY v0.1.x compatibility gate: false also disables frost protection. No longer sets body-heat factor.")]
    public bool DoubleFrostResistance { get; set; } = true;
}
