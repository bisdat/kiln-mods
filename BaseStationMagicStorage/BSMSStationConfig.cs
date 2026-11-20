// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.BaseStationMagicStorage;

/// <summary>
/// 26 global production-station TYPE switches, shown as checkboxes in the
/// in-game Kiln Mods -> Base Station Magic Storage configuration (like Survival QoL).
/// All instances of the same type share one switch, across every Flame Altar base.
/// Type switches govern in-place output sharing by default, and optional automation.
/// </summary>
public sealed class BSMSStationConfig
{
    // Requested defaults: finished products stay at the station and are shared directly.
    [Comment("Share outputs: allow finished products to be crafted from directly while they remain in their producing station. Same-base only, no queued inputs.")]
    public bool ShareOutputs { get; set; } = true;

    [Comment("Collect outputs: automatically move finished products to explicitly named crafting output Magic Chests, as in v0.1.3. Disable to keep products in their producing stations.")]
    public bool CollectOutputs { get; set; } = false;

    [Comment("Alchemy Station: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool AlchemyStation { get; set; } = true;

    [Comment("Kiln: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool Kiln { get; set; } = true;

    [Comment("Spinning Machine: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool SpinningMachine { get; set; } = true;

    [Comment("Blast Furnace: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool BlastFurnace { get; set; } = true;

    [Comment("Coffee Brewery: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool CoffeeBrewery { get; set; } = true;

    [Comment("Seedbed: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool Seedbed { get; set; } = true;

    [Comment("Mill: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool Mill { get; set; } = true;

    [Comment("Drying Rack: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool DryingRack { get; set; } = true;

    [Comment("Forge: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool Forge { get; set; } = true;

    [Comment("Oven: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool Oven { get; set; } = true;

    [Comment("Smelter: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool Smelter { get; set; } = true;

    [Comment("Table Saw: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool TableSaw { get; set; } = true;

    [Comment("Grinding Stones: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool GrindingStones { get; set; } = true;

    [Comment("Spinning Wheel: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool SpinningWheel { get; set; } = true;

    [Comment("Loom: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool Loom { get; set; } = true;

    [Comment("Hand Spindle: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool HandSpindle { get; set; } = true;

    [Comment("Beehive: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool Beehive { get; set; } = true;

    [Comment("Laboratory: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool Laboratory { get; set; } = true;

    [Comment("Infusion Altar: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool InfusionAltar { get; set; } = true;

    [Comment("Cooking Station: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool CookingStation { get; set; } = true;

    [Comment("Coloring Station: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool ColoringStation { get; set; } = true;

    [Comment("Coffee Drum Roaster: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool CoffeeDrumRoaster { get; set; } = true;

    [Comment("Ectoplasm Press: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool EctoplasmPress { get; set; } = true;

    [Comment("Charcoal Kiln: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool CharcoalKiln { get; set; } = true;

    [Comment("Tanning Station: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool TanningStation { get; set; } = true;

    [Comment("Seafood Grill: share finished outputs and permit optional auto-fill/collection when separately enabled.")]
    public bool SeafoodGrill { get; set; } = true;
}
