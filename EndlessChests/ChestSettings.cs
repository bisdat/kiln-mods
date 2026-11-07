using System.Text.Json.Serialization;

namespace EndlessChests;

public sealed class ChestSettings
{
    public bool Enabled { get; set; } = true;
    public int RecheckSeconds { get; set; } = 4;
    public double PositionToleranceMeters { get; set; } = 0.75;

    // Only a small, verified starter subset of the original ShroudForge groups is
    // included. Add your own exact Kiln item-catalog labels here.
    public Dictionary<string, List<string>> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Starter Blocks"] = ["Rough Stone Block", "Rough Wood Block", "Rough Flintstone Block"],
        ["Stone Blocks"] = ["Rough Stone Block", "Regular Stone Block", "Granite Block", "Limestone Block"],
        ["Wood Blocks"] = ["Rough Wood Block", "Refined Wood Block", "Conifer Wood Block", "Bamboo Wood Block"],
        ["Metal Blocks"] = ["Metal Block", "Copper Block", "Bronze Block", "Iron Block"],
        ["Arrows"] = ["Wooden Arrow", "Flint Arrow", "Copper Arrow", "Iron Arrow"],
        ["Tools"] = ["Iron Pickaxe", "Iron Axe", "Improved Construction Hammer"],
    };

    public List<ChestBinding> Chests { get; set; } = [];
}

public sealed class ChestBinding
{
    public string WorldId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string Selection { get; set; } = "";
}
