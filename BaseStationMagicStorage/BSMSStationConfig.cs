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
    // Upgrade defaults preserve 0.1.3 behaviour until a user opts into sharing.
    [Comment("Share outputs: allow finished products to be crafted from directly while they remain in their producing station. Same-base only, no queued inputs.")]
    public bool ShareOutputs { get; set; } = false;

    [Comment("Collect outputs: automatically move finished products to explicitly named crafting output Magic Chests, as in v0.1.3. Disable to keep products in their producing stations.")]
    public bool CollectOutputs { get; set; } = true;

    [Comment("Alchemy Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool AlchemyStation { get; set; } = true;

    [Comment("Kiln: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Kiln { get; set; } = true;

    [Comment("Spinning Machine: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool SpinningMachine { get; set; } = true;

    [Comment("Blast Furnace: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool BlastFurnace { get; set; } = true;

    [Comment("Coffee Brewery: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool CoffeeBrewery { get; set; } = true;

    [Comment("Seedbed: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Seedbed { get; set; } = true;

    [Comment("Mill: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Mill { get; set; } = true;

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

    [Comment("Grinding Stones: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool GrindingStones { get; set; } = true;

    [Comment("Spinning Wheel: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool SpinningWheel { get; set; } = true;

    [Comment("Loom: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Loom { get; set; } = true;

    [Comment("Hand Spindle: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool HandSpindle { get; set; } = true;

    [Comment("Beehive: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Beehive { get; set; } = true;

    [Comment("Laboratory: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool Laboratory { get; set; } = true;

    [Comment("Infusion Altar: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool InfusionAltar { get; set; } = true;

    [Comment("Cooking Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool CookingStation { get; set; } = true;

    [Comment("Coloring Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool ColoringStation { get; set; } = true;

    [Comment("Coffee Drum Roaster: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool CoffeeDrumRoaster { get; set; } = true;

    [Comment("Ectoplasm Press: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool EctoplasmPress { get; set; } = true;

    [Comment("Charcoal Kiln: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool CharcoalKiln { get; set; } = true;

    [Comment("Tanning Station: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool TanningStation { get; set; } = true;

    [Comment("Seafood Grill: allow BSMS to fill ingredients (if base auto-fill is enabled) and collect finished goods (if base auto-collection is enabled).")]
    public bool SeafoodGrill { get; set; } = true;
}
