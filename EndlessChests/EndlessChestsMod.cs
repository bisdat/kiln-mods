// Kiln-native inventory refills. No game-memory layout guesses and no ShroudForge DLL.
using System.Text.Json;
using Kiln;

namespace EndlessChests;

public sealed class EndlessChestsMod : Mod
{
    private ChestSettings _settings = new();
    private IniCatalog _catalog = new();
    private string _settingsFile = "";
    private string _catalogFile = "";
    private CommandRegistration? _command;
    private readonly HashSet<ChestBinding> _busy = [];
    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);
    private PendingEmpty? _pendingEmpty;
    private double _elapsed;
    private int _generation;
    private bool _stopping;

    private sealed record PendingEmpty(
        string WorldId, EntityId Chest, WorldPosition At, EntityId Operator,
        string Fingerprint, InventorySlot[] Slots, DateTimeOffset ExpiresAt);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public override void OnLoad()
    {
        Directory.CreateDirectory(Context.DataDirectory);
        _settingsFile = Path.Combine(Context.DataDirectory, "chests.json");
        _catalogFile = Path.Combine(Context.DataDirectory, "endless-chests.ini");
        LoadSettings();
        _command = Commands.Register("echest", HandleCommand, CommandPermission.Admin,
            usage: "<bind|name|empty|unbind|status|list|groups|refill|reload> [selection]",
            description: "Bind a nearby chest to a name from the original Endless Chests catalogue.");
        Context.Tick.Pre += Tick;
        Log.Info("Endless Chests (Kiln) loaded: name catalog available. In-world chest name detection is not supported yet.");
    }

    public override void OnUnload()
    {
        _stopping = true;
        _generation++;
        _pendingEmpty = null;
        _command?.Dispose();
        _busy.Clear();
    }

    private void LoadSettings()
    {
        try
        {
            var catalogue = IniCatalog.LoadOrCreate(_catalogFile);
            if (!File.Exists(_settingsFile))
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(new ChestSettings(), Json));
            var state = JsonSerializer.Deserialize<ChestSettings>(File.ReadAllText(_settingsFile), Json)
                ?? throw new InvalidDataException("chests.json is empty");
            if (state.Chests is null || state.PositionToleranceMeters is < 0.25 or > 2.0
                || !double.IsFinite(state.PositionToleranceMeters) || state.RecheckSeconds is < 2 or > 120)
                throw new InvalidDataException("positionToleranceMeters must be 0.25..2.0; recheckSeconds must be 2..120");
            if (state.Chests.Any(c => string.IsNullOrWhiteSpace(c.WorldId) ||
                string.IsNullOrWhiteSpace(c.Selection) || !double.IsFinite(c.X) ||
                !double.IsFinite(c.Y) || !double.IsFinite(c.Z)))
                throw new InvalidDataException("A chest binding has an invalid world, position, or selection");
            _catalog = catalogue;
            _settings = state;
            _pendingEmpty = null;
            _warned.Clear();
            _generation++;
            Log.Info($"Endless Chests: {catalogue.Groups.Count} default/custom groups, {catalogue.Aliases.Count} aliases, " +
                $"{catalogue.DisabledItems.Count} disabled items, {state.Chests.Count} persisted chests; magic={catalogue.AllowMagicChests}");
            if (catalogue.EnableGemLevels || catalogue.FullyUpgradeGear)
                Log.Info("Endless Chests: original EnableGemLevels/FullyUpgradeGear options are retained in INI but unsupported by Kiln.Items");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException ||
            ex is InvalidOperationException)
        {
            _settings = new ChestSettings { Enabled = false };
            _pendingEmpty = null;
            _generation++;
            Log.Error(ex, "Endless Chests: config invalid; disabled until repaired and 'echest reload'");
        }
    }

    private void SaveSettings()
    {
        // A temporary file avoids losing every chest assignment on an interrupted write.
        var tmp = _settingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_settings, Json));
        File.Move(tmp, _settingsFile, overwrite: true);
        _generation++;
    }

    private static double Distance2(WorldPosition a, WorldPosition b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private static WorldPosition At(ChestBinding b) => new(b.X, b.Y, b.Z);

    // Exclude production stations, NPCs and players. The item service's
    // "Containers" kind is broader than player-placed storage chests.
    private List<(EntityId Id, WorldPosition At)> StorageChests()
    {
        var result = new List<(EntityId, WorldPosition)>();
        if (!Items.Available || !World.Available || !Spawn.Available) return result;
        foreach (var id in Items.FindInventories(InventoryKinds.Containers))
        {
            var inventory = Items.Read(id);
            if (inventory is not { Initialized: true } || inventory.IsPlayer || inventory.IsNpc)
                continue;
            var name = Spawn.Inspect(id)?.TemplateName;
            if (name is null || !name.StartsWith("Prop_", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("Storage", StringComparison.OrdinalIgnoreCase)) continue;
            var magic = name.Contains("Magic", StringComparison.OrdinalIgnoreCase);
            if (magic && !_catalog.AllowMagicChests) continue;
            if (inventory.GenericSlots < 24 || inventory.GenericSlots > (magic ? 72 : 56))
                continue;
            if (World.Position(id) is { } position)
                result.Add((id, position));
        }
        return result;
    }

    private (EntityId Id, WorldPosition At) Nearest(CommandContext c)
    {
        if (c.Source.Player.IsNone)
            throw new CommandException("Need a player position: in offline mode use kiln/control/console/*.cmd from inside a world.");
        if (!World.Available || !Items.Available || !Spawn.Available)
            throw new CommandException("Kiln.World, Kiln.Items or Kiln.Spawn is not available yet.");
        var playerAt = World.Position(c.Source.Player)
            ?? throw new CommandException("Your character is not in a running world yet.");
        var nearest = StorageChests().OrderBy(x => Distance2(x.At, playerAt)).Take(2).ToArray();
        if (nearest.Length == 0 || Distance2(nearest[0].At, playerAt) > 16)
            throw new CommandException("No compatible storage chest within 4 metres.");
        if (nearest.Length > 1 &&
            Math.Abs(Distance2(nearest[1].At, playerAt) - Distance2(nearest[0].At, playerAt)) < 0.25)
            throw new CommandException("Two storage chests are equally close; move nearer to the one you intend to change.");
        return nearest[0];
    }

    private static string WorldId() => Saves.WorldId
        ?? throw new CommandException("World save GUID unavailable; enter a saved world first.");

    private ChestBinding? BoundAt(string world, WorldPosition at) =>
        _settings.Chests.FirstOrDefault(b => b.WorldId == world &&
            Distance2(At(b), at) <= _settings.PositionToleranceMeters * _settings.PositionToleranceMeters);

    private static string Fingerprint(InventoryContents inv) =>
        string.Join(";", inv.Slots.Where(s => !s.IsEmpty).OrderBy(s => s.Slot.Value)
            .Select(s => $"{s.Slot.Value:x}/{s.Item.Value:x}/{s.Count}/{s.ItemEntity.Value:x}"));

    private void HandleCommand(CommandContext c)
    {
        var verb = c.ArgOrDefault(0, "help")!.ToLowerInvariant();
        switch (verb)
        {
            case "help":
                c.Reply("echest bind <group/item> | bind Empty | empty confirm | unbind | groups [search] | list | status | refill | reload. " +
                    "The name is a Kiln binding, not the chest's game UI name. Empty is destructive only after a preview and confirmation.");
                break;
            case "reload":
                LoadSettings();
                c.Reply(_settings.Enabled ? $"INI loaded: {_catalog.Groups.Count} groups." :
                    "Refills disabled; inspect chests.json, endless-chests.ini and kiln.log.");
                break;
            case "status":
                c.Reply($"Endless Chests: enabled={_settings.Enabled}, groups={_catalog.Groups.Count}, " +
                    $"aliases={_catalog.Aliases.Count}, blocked items={_catalog.DisabledItems.Count}, " +
                    $"items={Items.Available}, world={World.Available}, spawn={Spawn.Available}, " +
                    $"GUID={Saves.WorldId ?? "not ready"}, bindings={_settings.Chests.Count}. In-game rename reading: unavailable.");
                break;
            case "groups":
            {
                var search = string.Join(" ", c.Args.Skip(1)).Trim();
                var names = _catalog.Groups.Keys.Where(n => n.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
                c.Reply($"Groups matching '{search}' ({names.Length}): " +
                    string.Join(", ", names.Take(18)) + (names.Length > 18 ? " ... refine your search." : ""));
                break;
            }
            case "list":
            {
                var world = WorldId();
                var binds = _settings.Chests.Where(b => b.WorldId == world).ToArray();
                c.Reply(binds.Length == 0 ? "No bound chests in this world." :
                    string.Join("; ", binds.Take(12).Select(b => $"{b.Selection} @ {b.X:F1},{b.Y:F1},{b.Z:F1}")) +
                    (binds.Length > 12 ? $" (+{binds.Length - 12} more)" : ""));
                break;
            }
            case "bind":
            case "name":
            {
                if (c.Count < 2) throw c.Usage("enter an exact item/group name from endless-chests.ini");
                if (!_settings.Enabled) c.Fail("Endless Chests is disabled in chests.json.");
                if (Items.Status() is not { Transactions: true, Catalog: true })
                    c.Fail("Kiln.Items is not ready.");
                var requested = string.Join(" ", c.Args.Skip(1)).Trim();
                var selection = _catalog.ExpandAlias(requested);
                if (string.Equals(selection, "Empty", StringComparison.OrdinalIgnoreCase))
                {
                    PreviewEmpty(c);
                    break;
                }
                var (id, at) = Nearest(c);
                var world = WorldId();
                if (BoundAt(world, at) is not null) c.Fail("Chest already bound; use echest unbind first.");
                var inv = Items.Read(id);
                if (inv is not { Initialized: true } || inv.UsedSlots != 0)
                    c.Fail("Only EMPTY chests can be bound. Remove any valuables first.");
                var plan = _catalog.Resolve(selection, Math.Min(inv!.GenericSlots, 72));
                if (plan.Items.Count == 0)
                    c.Fail($"No allowed, catalogued items in '{selection}'. Check endless-chests.ini and the game item catalog.");
                _settings.Chests.Add(new ChestBinding { WorldId = world, X = at.X, Y = at.Y, Z = at.Z,
                    Selection = selection });
                SaveSettings();
                c.Reply($"Bound chest {id} to '{selection}' ({plan.Items.Count} supported item types). " +
                    (plan.Unknown.Count > 0 ? $"{plan.Unknown.Count} game-unknown labels will be skipped." : "Refill will begin shortly."));
                Log.Info($"Endless Chests: bound '{selection}' @ {at} ({world})");
                break;
            }
            case "unbind":
            {
                var world = WorldId();
                var (_, at) = Nearest(c);
                var bind = BoundAt(world, at) ?? throw new CommandException("The nearest chest is not bound.");
                _settings.Chests.Remove(bind);
                SaveSettings();
                _pendingEmpty = null;
                c.Reply($"Unbound '{bind.Selection}'. All existing chest contents preserved.");
                break;
            }
            case "empty":
                if (c.Count > 1 && c.Arg(1).Equals("confirm", StringComparison.OrdinalIgnoreCase))
                    ConfirmEmpty(c);
                else
                    PreviewEmpty(c);
                break;
            case "refill":
                _elapsed = 999;
                c.Reply("Refill check requested for the next world step.");
                break;
            default:
                throw c.Usage($"Unknown action '{verb}'");
        }
    }

    // Empty is NEVER activated automatically by old metadata, or merely because
    // a real chest is renamed in the game UI. Confirm against a fresh inventory
    // snapshot of the exact nearby chest before deleting anything.
    private void PreviewEmpty(CommandContext c)
    {
        if (!_settings.Enabled) c.Fail("Endless Chests is disabled.");
        if (Items.Status() is not { Transactions: true }) c.Fail("Kiln.Items cannot transact yet.");
        var (chest, at) = Nearest(c);
        var world = WorldId();
        var bound = BoundAt(world, at);
        if (bound != null && _busy.Contains(bound))
            c.Fail("A refill is in progress. Wait a moment, then preview again.");
        var inv = Items.Read(chest) ?? throw new CommandException("The chest's inventory is unavailable.");
        var slots = inv.Slots.Where(s => !s.IsEmpty).ToArray();
        _pendingEmpty = new PendingEmpty(world, chest, at, c.Source.Player,
            Fingerprint(inv), slots, DateTimeOffset.UtcNow.AddSeconds(60));
        c.Reply($"EMPTY PREVIEW: chest {chest}, {slots.Length} occupied slots, {slots.Sum(s => s.Count)} units. " +
            "This cannot be undone. To delete exactly these items and unbind this chest, run: echest empty confirm " +
            "(within 60 seconds, while standing at the same chest).");
    }

    private void ConfirmEmpty(CommandContext c)
    {
        var pending = _pendingEmpty ?? throw new CommandException("No pending Empty preview. Use echest bind Empty first.");
        if (DateTimeOffset.UtcNow > pending.ExpiresAt) { _pendingEmpty = null; c.Fail("Empty preview expired; run it again."); }
        var world = WorldId();
        var (chest, at) = Nearest(c);
        if (world != pending.WorldId || chest != pending.Chest ||
            pending.Operator != c.Source.Player || Distance2(at, pending.At) > 0.0625)
            c.Fail("Different chest, character, or world; preview again.");
        var current = Items.Read(chest) ?? throw new CommandException("The chest is no longer available.");
        if (Fingerprint(current) != pending.Fingerprint)
        {
            _pendingEmpty = null;
            c.Fail("Chest contents changed since preview. Nothing was deleted; preview again.");
        }
        var bound = BoundAt(world, at);
        if (bound != null && _busy.Contains(bound))
            c.Fail("A refill is still in progress; preview again after it finishes.");
        _pendingEmpty = null;
        // Stop future refills BEFORE any asynchronous item removal is submitted.
        if (bound != null)
        {
            _settings.Chests.Remove(bound);
            SaveSettings();
        }
        var generation = _generation;
        _ = ClearSnapshot(chest, pending.Slots, generation);
        c.Reply($"Clearing {pending.Slots.Length} previously previewed chest slots using Kiln.Items, then leaving chest unbound. " +
            "See kiln.log for the final count. Items added after this confirmation are NOT automatically deleted.");
    }

    private async Task ClearSnapshot(EntityId chest, InventorySlot[] expected, int generation)
    {
        var removed = 0;
        try
        {
            foreach (var slot in expected)
            {
                if (_stopping || generation != _generation) break;
                var inv = Items.Read(chest);
                var now = inv?.Slots.FirstOrDefault(s => s.Slot == slot.Slot);
                if (now is null || now.IsEmpty || now.Item != slot.Item ||
                    now.Count != slot.Count || now.ItemEntity != slot.ItemEntity)
                {
                    Log.Warn($"Endless Chests: empty stopped: slot {slot.Slot} no longer matches its confirmed snapshot.");
                    break;
                }
                var result = await Items.TakeSlot(chest, slot);
                if (result.Status != ItemsStatus.Done)
                {
                    Log.Warn($"Endless Chests: empty stopped: {result}");
                    break;
                }
                removed++;
            }
        }
        catch (Exception ex) { Log.Warn($"Endless Chests: empty failed after {removed} slots: {ex.Message}"); }
        Log.Info($"Endless Chests: empty chest {chest}: {removed}/{expected.Length} previously confirmed slots cleared.");
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
            var candidates = StorageChests();
            var tolerance2 = _settings.PositionToleranceMeters * _settings.PositionToleranceMeters;
            var generation = _generation;
            foreach (var b in _settings.Chests)
            {
                if (b.WorldId != world || _busy.Contains(b)) continue;
                // "Empty" is only an explicit, confirmed one-shot command.
                if (string.Equals(b.Selection, "Empty", StringComparison.OrdinalIgnoreCase)) continue;
                var matched = candidates.Where(chest => Distance2(chest.At, At(b)) <= tolerance2).Take(2).ToArray();
                if (matched.Length != 1) continue;
                _busy.Add(b);
                _ = Refill(b, matched[0].Id, generation);
            }
        }
        catch (Exception ex) { Log.Warn($"Endless Chests: chest scan failed: {ex.Message}"); }
    }

    private async Task Refill(ChestBinding b, EntityId chest, int generation)
    {
        try
        {
            var first = Items.Read(chest);
            if (first is not { Initialized: true }) return;
            var plan = _catalog.Resolve(b.Selection, Math.Min(first.GenericSlots, 72));
            if ((plan.Unknown.Count != 0 || plan.Blocked.Count != 0) && _warned.Add(b.Selection))
                Log.Warn($"Endless Chests: '{b.Selection}': {plan.Unknown.Count} unavailable item names, " +
                    $"{plan.Blocked.Count} items blocked by owner settings; {plan.Items.Count} valid types.");
            foreach (var item in plan.Items)
            {
                if (_stopping || generation != _generation || !_settings.Enabled) break;
                var inv = Items.Read(chest);
                if (inv is not { Initialized: true }) break;
                var wanted = item.IsGear ? 1 : Math.Clamp(item.MaxStack, 1, 250);
                var missing = wanted - inv.Count(item.Id);
                if (missing <= 0) continue;
                var result = await Items.Give(chest, item.Id, missing, options: GiveOptions.AllOrNothing);
                if (!result.Succeeded)
                {
                    if (result.Status is not (ItemsStatus.NoRoom or ItemsStatus.NoInventory))
                        Log.Warn($"Endless Chests: '{b.Selection}' item {item.Label}: {result}");
                    break;
                }
            }
        }
        catch (Exception ex) { Log.Warn($"Endless Chests: refill of '{b.Selection}' failed: {ex.Message}"); }
        finally { _busy.Remove(b); }
    }
}
