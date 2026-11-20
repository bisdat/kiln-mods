// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.SurvivalQoL;

/// <summary>Hot-reloadable options written to kiln/config/survival-qol.json.</summary>
public sealed class SurvivalQoLConfig
{
    [Comment("Keep player stamina at 100% via Kiln.Rules.")]
    public bool InfiniteStamina { get; set; } = true;

    [Comment("Stamina regeneration speed: 1x = normal game, 10x = up to ten times normal recovery. Only active when Infinite Stamina is off.")]
    [Range(1, 10)]
    public int StaminaRegenMultiplier { get; set; } = 1;

    [Comment("Staff spell charge-time multiplier: 1x = normal game; 2x-10x shortens spell charge time. Experimental: actual casting speed needs an in-game test.")]
    [Range(1, 10)]
    public int StaffCastingSpeedMultiplier { get; set; } = 1;

    [Comment("Keep player mana at 100% via Kiln.Rules.")]
    public bool InfiniteMana { get; set; } = true;

    [Comment("Apply optional bonus healing while injured. Zero additional HP/s leaves the game\'s normal regeneration unchanged. Does not prevent lethal damage.")]
    public bool FastHealthRegen { get; set; } = true;

    [Comment("Bonus health per second: 0 = normal game (no added healing); 1-100 adds HP/s while injured.")]
    [Range(0, 100)]
    public int HealthRegenPerSecond { get; set; } = 0;

    [Comment("Apply optional bonus mana regeneration. Zero additional mana/s leaves normal game recovery unchanged.")]
    public bool FastManaRegen { get; set; } = true;

    [Comment("Bonus mana per second: 0 = normal game (no added mana); 1-100 adds mana/s when below maximum.")]
    [Range(0, 100)]
    public int ManaRegenPerSecond { get; set; } = 0;

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
