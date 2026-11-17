// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.BaseStationMagicStorage;

/// <summary>
/// 26 global production-station TYPE switches, shown as checkboxes in the
/// in-game Kiln Mods -> Base Station Magic Storage configuration (like Survival QoL).
/// All instances of the same type share one switch, across every Flame Altar base.
/// </summary>
public sealed class BSMSStationConfig
{
    [Comment("Alchemy Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool AlchemyStation { get; set; } = true;

    [Comment("Kiln: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Kiln { get; set; } = true;

    [Comment("Carding Machine: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool CardingMachine { get; set; } = true;

    [Comment("Blast Furnace: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool BlastFurnace { get; set; } = true;

    [Comment("Coffee Brewer: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool CoffeeBrewer { get; set; } = true;

    [Comment("Seedling Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool SeedlingStation { get; set; } = true;

    [Comment("Improved Grinder: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool ImprovedGrinder { get; set; } = true;

    [Comment("Drying Rack: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool DryingRack { get; set; } = true;

    [Comment("Forge: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Forge { get; set; } = true;

    [Comment("Oven: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Oven { get; set; } = true;

    [Comment("Smelter: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Smelter { get; set; } = true;

    [Comment("Table Saw: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool TableSaw { get; set; } = true;

    [Comment("Hand Grinder: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool HandGrinder { get; set; } = true;

    [Comment("Spinning Wheel: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool SpinningWheel { get; set; } = true;

    [Comment("Loom: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Loom { get; set; } = true;

    [Comment("Hand Spindle: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool HandSpindle { get; set; } = true;

    [Comment("Honey Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool HoneyStation { get; set; } = true;

    [Comment("Laboratory: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Laboratory { get; set; } = true;

    [Comment("Infusion Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool InfusionStation { get; set; } = true;

    [Comment("Fireplace: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Fireplace { get; set; } = true;

    [Comment("Coloring Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool ColoringStation { get; set; } = true;

    [Comment("Coffee Roaster: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool CoffeeRoaster { get; set; } = true;

    [Comment("Ectoplasm Press: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool EctoplasmPress { get; set; } = true;

    [Comment("Charcoal Kiln: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool CharcoalKiln { get; set; } = true;

    [Comment("Tanning Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool TanningStation { get; set; } = true;

    [Comment("Medieval Grill: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool MedievalGrill { get; set; } = true;
}
