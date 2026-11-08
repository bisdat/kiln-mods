// Cross-base Storage (Kiln) — read-only foundation.
// No inventory writes, no recipe bypass, and no claim to extend Enshrouded's
// vanilla crafting/building source resolver. See README.md for API blockers.
using Kiln;

namespace CrossBaseStorage;

public sealed class CrossBaseStorageMod : Mod
{
    private CommandRegistration? _commands;
    private const int MaxShown = 12;

    public override void OnLoad()
    {
        _commands = Commands.Register(
            "cbstore", HandleCommand, CommandPermission.Admin,
            usage: "<status|scan|templates|near|stock item name|top>",
            description: "Inspect loaded storage and possible magic chests across the world (read-only).");
        Log.Warn("Cross-base Storage: discovery prototype loaded. Vanilla cross-base crafting/building is NOT enabled; Kiln has no storage-source integration API yet.");
    }

    public override void OnUnload() => _commands?.Dispose();

    private sealed record Chest(
        EntityId Entity, string Template, WorldPosition Position,
        int Slots, int UsedSlots, InventoryContents Contents, bool MagicByName);

    // Template-name matching is deliberately a HEURISTIC. It is not an engine
    // MagicChest flag; an upgraded chest could retain an ordinary template.
    // The 'templates'/'near' diagnostics let the host identify false negatives.
    private static bool IsStorage(string? template, int slots) =>
        template is not null && template.StartsWith("Prop_", StringComparison.OrdinalIgnoreCase) &&
        (template.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
         template.Contains("Chest", StringComparison.OrdinalIgnoreCase)) &&
        slots is >= 16 and <= 72;

    private static bool MagicByName(string template) =>
        template.Contains("Magic", StringComparison.OrdinalIgnoreCase);

    private static List<Chest> ScanLoaded()
    {
        if (!Items.Available || !Spawn.Available || !World.Available ||
            Items.Status() is not { Read: true })
            throw new CommandException("Kiln.Items, Kiln.Spawn and Kiln.World must be ready inside a loaded world.");

        if (Saves.WorldId is null)
            throw new CommandException("Enter a saved Enshrouded world before scanning chest inventories.");

        var chests = new List<Chest>();
        foreach (var id in Items.FindInventories(InventoryKinds.Containers))
        {
            var inventory = Items.Read(id);
            if (inventory is not { Initialized: true } || inventory.IsPlayer || inventory.IsNpc)
                continue;

            var info = Spawn.Inspect(id);
            if (!IsStorage(info?.TemplateName, inventory.GenericSlots))
                continue;

            var position = info!.Position;
            var template = info.TemplateName!;
            chests.Add(new Chest(id, template, position, inventory.GenericSlots,
                inventory.UsedSlots, inventory, MagicByName(template)));
        }
        return chests;
    }

    private static string Where(Chest chest) =>
        $"{chest.Position.X:F1},{chest.Position.Y:F1},{chest.Position.Z:F1}";

    private static string Describe(Chest chest) =>
        $"entity={chest.Entity}, template={chest.Template}, slots={chest.UsedSlots}/{chest.Slots}, " +
        $"magicByTemplate={chest.MagicByName}, pos={Where(chest)}";

    private static void Status(CommandContext c)
    {
        c.Reply($"Cross-base Storage (Kiln) discovery-only prototype; world={Saves.WorldId ?? "(not loaded)"}, " +
                $"items={Items.Available}, spawn={Spawn.Available}, world={World.Available}. " +
                "Cross-base build/craft integration=NOT AVAILABLE in this Kiln SDK. " +
                "No item transfers, crafting injection or inventory changes are performed.");
    }

    private static void Scan(CommandContext c)
    {
        var chests = ScanLoaded();
        var probable = chests.Where(x => x.MagicByName).ToArray();
        var unique = probable.SelectMany(x => x.Contents.Slots.Where(s => !s.IsEmpty))
            .Select(s => s.Item).Distinct().Count();
        var units = probable.Sum(x => x.Contents.Slots.Where(s => !s.IsEmpty).Sum(s => (long)s.Count));

        c.Reply($"LOADED world only: {chests.Count} candidate storage chests, " +
                $"{probable.Length} magic-by-template candidates; {unique} item types, " +
                $"{units} units across those candidates. " +
                "This is a READ-ONLY sample, NOT cross-base crafting access. " +
                "Unloaded bases and magic upgrades without 'Magic' in their template may be absent.");

        foreach (var chest in probable.Take(MaxShown))
            c.Reply("MAGIC CANDIDATE " + Describe(chest));
        if (probable.Length > MaxShown)
            c.Reply($"... and {probable.Length - MaxShown} additional candidate magic chests.");
    }

    private static void Templates(CommandContext c)
    {
        var chests = ScanLoaded();
        var groups = chests.GroupBy(x => x.Template, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        c.Reply($"Loaded chest templates ({groups.Length} types). 'Magic' classification is provisional:");
        foreach (var group in groups.Take(24))
            c.Reply($"{group.Key}: {group.Count()} loaded chests; " +
                    $"magicByTemplate={MagicByName(group.Key)}");
        if (groups.Length > 24)
            c.Reply($"... {groups.Length - 24} more templates.");
    }

    private static void Nearest(CommandContext c)
    {
        if (c.Source.Player.IsNone || World.Position(c.Source.Player) is not { } at)
            throw new CommandException("Enter a world as a player and stand next to a chest.");
        var nearest = ScanLoaded()
            .OrderBy(x => DistanceSquared(x.Position, at))
            .FirstOrDefault();
        if (nearest is null || DistanceSquared(nearest.Position, at) > 25)
            throw new CommandException("No compatible storage chest found within 5 metres.");
        c.Reply("NEAREST READ-ONLY: " + Describe(nearest));
        c.Reply("If the game treats this as a Magic Chest but magicByTemplate=false, " +
                "the name-based detector needs an engine-supported magic-chest flag.");
    }

    private static double DistanceSquared(WorldPosition a, WorldPosition b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);

    private static void Stock(CommandContext c)
    {
        if (c.Count < 2)
            throw c.Usage("cbstore stock <exact item name or item ID>");
        var query = string.Join(" ", c.Args.Skip(1)).Trim();
        var item = Items.Lookup(query) ??
            throw new CommandException($"Unknown catalogued item '{query}'.");

        var chests = ScanLoaded().Where(x => x.MagicByName)
            .Select(x => (Chest: x, Count: x.Contents.Count(item.Id)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ToArray();
        c.Reply($"READ-ONLY loaded magic-by-template stock: {item.Label} = " +
                $"{chests.Sum(x => (long)x.Count)} units in {chests.Length} candidate chests.");
        foreach (var (chest, count) in chests.Take(MaxShown))
            c.Reply($"{count} x {item.Label} @ {Where(chest)}, entity={chest.Entity}");
        if (chests.Length > MaxShown)
            c.Reply($"... plus {chests.Length - MaxShown} other candidate chests.");
    }

    private static void Top(CommandContext c)
    {
        var chests = ScanLoaded().Where(x => x.MagicByName);
        var totals = chests.SelectMany(x => x.Contents.Slots.Where(s => !s.IsEmpty))
            .GroupBy(s => s.Item)
            .Select(g => (Id: g.Key, Count: g.Sum(s => (long)s.Count)))
            .OrderByDescending(g => g.Count)
            .Take(MaxShown)
            .ToArray();
        if (totals.Length == 0)
        {
            c.Reply("No stock detected in loaded magic-by-template candidates.");
            return;
        }
        c.Reply("Top items in LOADED candidate magic chests (read-only):");
        foreach (var (id, count) in totals)
            c.Reply($"{count} x {Items.Get(id)?.Label ?? id.ToString()}");
    }

    private static void HandleCommand(CommandContext c)
    {
        var verb = c.ArgOrDefault(0, "help")!.ToLowerInvariant();
        switch (verb)
        {
            case "help":
                c.Reply("cbstore status | scan | templates | near | stock <item> | top. " +
                        "Read-only diagnostics; no cross-base crafting/building yet.");
                break;
            case "status": Status(c); break;
            case "scan": Scan(c); break;
            case "templates": Templates(c); break;
            case "near": Nearest(c); break;
            case "stock": Stock(c); break;
            case "top": Top(c); break;
            default: throw c.Usage($"Unknown Cross-base Storage action '{verb}'.");
        }
    }
}
