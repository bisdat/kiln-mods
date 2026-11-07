namespace EndlessChests;

// Per-world chest assignments and scheduling. Item groups, aliases, and global
// filters come from endless-chests.ini, not from this state file.
public sealed class ChestSettings
{
    public bool Enabled { get; set; } = true;
    // When enabled, unbound chests named with an exact default group/alias/item
    // replenish automatically using their ECS CustomPlayerString hash.
    public bool AutoNames { get; set; } = true;
    public int RecheckSeconds { get; set; } = 4;
    public double PositionToleranceMeters { get; set; } = 0.75;
    public List<ChestBinding> Chests { get; set; } = [];
}

public sealed class ChestBinding
{
    public string WorldId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    // Exact name of an item or group in the supplied INI file. This is a mod
    // binding, not the in-game chest name (Kiln has no name reader yet).
    public string Selection { get; set; } = "";
}
