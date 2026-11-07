// Kiln-native implementation. Never loads the ShroudForge DLL or writes raw game memory.
using System.Text.Json;
using Kiln;

namespace EndlessChests;

public sealed class EndlessChestsMod : Mod
{
    private ChestSettings _settings = new();
    private string _settingsFile = "";
    private CommandRegistration? _command;
    private readonly HashSet<ChestBinding> _busy = [];
    private double _elapsed;
    private int _generation;
    private bool _stopping;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public override void OnLoad()
    {
        _settingsFile = Path.Combine(Context.DataDirectory, "chests.json");
        Directory.CreateDirectory(Context.DataDirectory);
        LoadSettings();
        // The client may be in its menu here. Every world operation is performed
        // later on the server world's tick thread, never in OnLoad.
        _command = Commands.Register("echest", HandleCommand, CommandPermission.Admin,
            usage: "<bind|unbind|status|list|refill|reload> [item or group]",
            description: "Bind the nearest ordinary chest to a self-restocking Kiln item selection.");
        Context.Tick.Pre += Tick;
        Log.Info("Endless Chests (Kiln) registered. In a hosted world use the console-folder command: echest bind Starter Blocks");
    }

    public override void OnUnload()
    {
        _stopping = true;
        _generation++;
        _command?.Dispose();
        _busy.Clear();
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFile))
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(new ChestSettings(), Json));
            var value = JsonSerializer.Deserialize<ChestSettings>(File.ReadAllText(_settingsFile), Json)
                ?? throw new InvalidDataException("empty chests.json");
            if (value.Chests == null || value.Groups == null || value.PositionToleranceMeters is < 0.25 or > 2.0
                || value.RecheckSeconds is < 2 or > 120)
                throw new InvalidDataException("invalid fields: position tolerance must be 0.25..2 m; recheck 2..120 s");
            _settings = value;
            _generation++;
            Log.Info($"Endless Chests: settings loaded ({value.Chests.Count} chest bindings)");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            // Never overwrite an invalid owner's settings, or continue generating with old policy.
            _settings = new ChestSettings { Enabled = false };
            _generation++;
            Log.Error(ex, "Endless Chests: invalid chests.json; generation disabled, repair it and run 'echest reload'");
        }
    }

    private void SaveSettings()
    {
        File.WriteAllText(_settingsFile, JsonSerializer.Serialize(_settings, Json));
        _generation++;
    }

    private static double Distance2(WorldPosition a, WorldPosition b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private static WorldPosition Position(ChestBinding b) => new(b.X, b.Y, b.Z);

    // Keep non-storage inventories such as crafting stations, NPCs and players out.
    // The item service reports every container, not just player-built chests.
    private static List<(EntityId Id, WorldPosition At)> OrdinaryChests()
    {
        var result = new List<(EntityId, WorldPosition)>();
        if (!Items.Available || !World.Available || !Spawn.Available) return result;
        foreach (var id in Items.FindInventories(InventoryKinds.Containers))
        {
            var inv = Items.Read(id);
            if (inv is not { Initialized: true, GenericSlots: >= 24 and <= 56 } || inv.IsPlayer || inv.IsNpc)
                continue;
            var tpl = Spawn.Inspect(id);
            if (tpl?.TemplateName is not { } name ||
                !name.StartsWith("Prop_", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Magic", StringComparison.OrdinalIgnoreCase))
                continue;
            if (World.Position(id) is { } at)
                result.Add((id, at));
        }
        return result;
    }

    private static (EntityId Id, WorldPosition At) Nearest(CommandContext c)
    {
        if (c.Source.Player.IsNone)
            throw new CommandException("Need a player position. In offline play use kiln/control/console/*.cmd after entering the world.");
        if (!World.Available || !Items.Available || !Spawn.Available)
            throw new CommandException("Kiln.World, Kiln.Items or Kiln.Spawn not ready in this game world.");
        var playerAt = World.Position(c.Source.Player)
            ?? throw new CommandException("Your character is not yet in a running server world.");
        var candidates = OrdinaryChests().OrderBy(x => Distance2(x.At, playerAt)).Take(2).ToArray();
        if (candidates.Length == 0 || Distance2(candidates[0].At, playerAt) > 16)
            throw new CommandException("No ordinary 24/32/40/56-slot storage chest within 4 metres.");
        if (candidates.Length > 1 && Distance2(candidates[1].At, playerAt) - Distance2(candidates[0].At, playerAt) < 0.25)
            throw new CommandException("Two chests are equally close. Move beside the intended chest and retry.");
        return candidates[0];
    }

    private static string WorldId() => Saves.WorldId
        ?? throw new CommandException("The world's save GUID is not available yet; enter a saved world before binding.");

    private ChestBinding? BoundAt(string world, WorldPosition at) =>
        _settings.Chests.FirstOrDefault(b => b.WorldId == world &&
            Distance2(Position(b), at) <= Math.Pow(_settings.PositionToleranceMeters, 2));

    private List<ItemInfo> Resolve(string selection)
    {
        List<string> names;
        var group = _settings.Groups.FirstOrDefault(x => string.Equals(x.Key, selection, StringComparison.OrdinalIgnoreCase));
        if (group.Key != null)
            names = group.Value;
        else
            names = [selection];

        if (names.Count is < 1 or > 16)
            throw new CommandException("A selection needs 1..16 distinct, verified item names.");
        var found = new List<ItemInfo>();
        foreach (var name in names)
        {
            var item = Items.Lookup(name.Trim())
                ?? throw new CommandException($"Kiln does not know the item '{name}'. Check the item's exact in-game label.");
            if (item.IsBlueprint)
                throw new CommandException($"'{name}' is an unlock blueprint, not a supply item.");
            if (found.All(i => i.Id != item.Id)) found.Add(item);
        }
        return found;
    }

    private void HandleCommand(CommandContext c)
    {
        var verb = c.ArgOrDefault(0, "help")!.ToLowerInvariant();
        switch (verb)
        {
            case "help":
                c.Reply("echest bind <item or group> | unbind | status | list | refill | reload. Bind an EMPTY ordinary chest while standing within 4 metres. No deletion or automatic rename handling.");
                break;
            case "reload":
                LoadSettings();
                c.Reply(_settings.Enabled ? "chests.json loaded." : "Generation disabled; inspect chests.json and kiln.log.");
                break;
            case "status":
                c.Reply($"Endless Chests: enabled={_settings.Enabled}, items={Items.Available}, world={World.Available}, spawn={Spawn.Available}, GUID={Saves.WorldId ?? "not ready"}, bindings={_settings.Chests.Count}.");
                break;
            case "list":
            {
                var world = WorldId();
                var list = _settings.Chests.Where(b => b.WorldId == world).ToList();
                c.Reply(list.Count == 0 ? "No chest bindings in this world." :
                    string.Join("; ", list.Take(12).Select(b => $"{b.Selection} @ {b.X:F1},{b.Y:F1},{b.Z:F1}")));
                break;
            }
            case "bind":
            {
                if (c.Count < 2) throw c.Usage("item/group name required");
                if (!_settings.Enabled) c.Fail("Generation is disabled in chests.json.");
                if (Items.Status() is not { Catalog: true, Transactions: true })
                    c.Fail("Kiln.Items has not finished starting; retry after loading the world.");
                var selection = string.Join(" ", c.Args.Skip(1)).Trim();
                var items = Resolve(selection);
                var world = WorldId();
                var (id, at) = Nearest(c);
                if (BoundAt(world, at) != null)
                    c.Fail("That chest is already bound. Use echest unbind first.");
                var inv = Items.Read(id);
                if (inv is null || inv.UsedSlots != 0)
                    c.Fail("Bind only an EMPTY chest. Move any valuables out first; the mod never clears items.");
                _settings.Chests.Add(new ChestBinding { WorldId = world, X = at.X, Y = at.Y, Z = at.Z, Selection = selection });
                SaveSettings();
                c.Reply($"Bound chest {id} to {selection} ({items.Count} item types). First refill occurs shortly.");
                Log.Info($"Endless Chests: bound {selection} in world {world} at {at}");
                break;
            }
            case "unbind":
            {
                var world = WorldId();
                var (_, at) = Nearest(c);
                var b = BoundAt(world, at) ?? throw new CommandException("That chest is not bound.");
                _settings.Chests.Remove(b);
                SaveSettings();
                c.Reply($"Unbound {b.Selection}. Existing chest contents are untouched.");
                break;
            }
            case "refill":
                _elapsed = 999;
                c.Reply("Refill check requested; it will run at the next world step.");
                break;
            default:
                throw c.Usage($"unknown action '{verb}'");
        }
    }

    private void Tick(in TickInfo tick)
    {
        if (_stopping || !_settings.Enabled) return;
        _elapsed += tick.WallDelta.TotalSeconds;
        if (_elapsed < _settings.RecheckSeconds) return;
        _elapsed = 0;
        if (!Items.Available || !World.Available || !Spawn.Available || Saves.WorldId is not { } world)
            return;
        if (Items.Status() is not { Transactions: true, Catalog: true }) return;
        try
        {
            var candidates = OrdinaryChests();
            var tolerance2 = _settings.PositionToleranceMeters * _settings.PositionToleranceMeters;
            var generation = _generation;
            foreach (var b in _settings.Chests)
            {
                if (b.WorldId != world || _busy.Contains(b)) continue;
                var matches = candidates.Where(c => Distance2(c.At, Position(b)) <= tolerance2)
                    .OrderBy(c => Distance2(c.At, Position(b))).Take(2).ToArray();
                if (matches.Length != 1) continue; // No match or ambiguous after building edits.
                _busy.Add(b);
                _ = Refill(b, matches[0].Id, generation);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Endless Chests: inventory scan failed: {ex.Message}");
        }
    }

    private async Task Refill(ChestBinding b, EntityId chest, int generation)
    {
        try
        {
            var items = Resolve(b.Selection);
            foreach (var item in items)
            {
                if (_stopping || generation != _generation || !_settings.Enabled) break;
                var inv = Items.Read(chest);
                if (inv is not { Initialized: true }) break;
                // One vanilla-sized stack per selection. Never remove or replace unrelated
                // contents, and do not produce extra copies while a stack is merely moved.
                var target = item.IsGear ? 1 : Math.Clamp(item.MaxStack, 1, 250);
                var missing = target - inv.Count(item.Id);
                if (missing <= 0) continue;
                var result = await Items.Give(chest, item.Id, missing, options: GiveOptions.AllOrNothing);
                if (!result.Succeeded)
                {
                    // A full chest is ordinary; avoid spamming logs for it every few seconds.
                    if (result.Status is not (ItemsStatus.NoRoom or ItemsStatus.NoInventory))
                        Log.Warn($"Endless Chests: {b.Selection}: {result}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Endless Chests: refill of {b.Selection} failed: {ex.Message}");
        }
        finally
        {
            _busy.Remove(b);
        }
    }
}
