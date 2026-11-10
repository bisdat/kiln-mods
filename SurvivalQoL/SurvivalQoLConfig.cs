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

    [Comment("Underwater oxygen duration multiplier: 1x = normal game, 10x = ten times longer. Integer steps.")]
    [Range(1, 10)]
    public int UnderwaterBreathMultiplier { get; set; } = 10;

    [Comment("Body heat/frost duration multiplier: 1x = normal game, 10x = ten times longer. Not the Shroud timer.")]
    [Range(1, 10)]
    public int FrostResistanceMultiplier { get; set; } = 10;
}
