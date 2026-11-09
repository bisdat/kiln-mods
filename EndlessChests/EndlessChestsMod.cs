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
    private readonly HashSet<EntityId> _busy = [];
    private readonly Dictionary<EntityId, ObservedChest> _nameSeen = [];

    private sealed class ObservedChest(WorldPosition at, ulong hash)
    {
        public WorldPosition At { get; } = at;
        public ulong Hash { get; set; } = hash;
        public bool NeedsClear { get; set; }
    }

    private string? _lastWorldId;
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
            usage: "<bind|name|empty|unbind|status|list|groups|nameprobe|refill|reload> [selection]",
            description: "Bind a nearby chest to a name from the original Endless Chests catalogue.");
        Context.Tick.Pre += Tick;
        Log.Warn("Endless Chests: CLEAR ON RENAME and automatic Empty are enabled by default. Chest contents are permanently deleted on game-UI renames.");
    }

    public override void OnUnload()
    {
        _stopping = true;
        _generation++;
        _pendingEmpty = null;
        _command?.Dispose();
        _busy.Clear();
        _nameSeen.Clear();
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
            _nameSeen.Clear();
            _generation++;
            Log.Info($"Endless Chests: {catalogue.Groups.Count} default/custom groups, {catalogue.Aliases.Count} aliases, " +
                $"{catalogue.DisabledItems.Count} disabled items, {state.Chests.Count} persisted chests; " +
                $"hashNames={catalogue.RecognizedNames}, autoNames={state.AutoNames}, " +
                $"clearOnRename={state.ClearOnRename}, autoEmpty={state.AutoEmpty}, magic={catalogue.AllowMagicChests}");
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

    private bool TryGameSelection(EntityId chest, out string selection, out ulong fingerprint)
    {
        selection = "";
        fingerprint = 0;
        if (!ChestNames.TryReadHash(chest, out fingerprint)) return false;
        if (ChestNames.IsEmptyHash(fingerprint))
        {
            selection = "Empty";
            return true;
        }
        return _catalog.TryGameName(fingerprint, out selection);
    }

    private static string Fingerprint(InventoryContents inv) =>
        string.Join(";", inv.Slots.Where(s => !s.IsEmpty).OrderBy(s => s.Slot.Value)
            .Select(s => $"{s.Slot.Value:x}/{s.Item.Value:x}/{s.Count}/{s.ItemEntity.Value:x}"));

    private void HandleCommand(CommandContext c)
    {
        var verb = c.ArgOrDefault(0, "help")!.ToLowerInvariant();
        switch (verb)
        {
            case "help":
                c.Reply("echest bind <group/item> | bind Empty | empty confirm | unbind | groups [search] | list | status | nameprobe | refill | reload. " +
                    "Game-UI rename deletes existing chest contents before any new refill. Renaming to Empty clears without confirmation. " +
                    "For a manual console clear, 'echest empty' still previews and 'echest empty confirm' confirms.");
                break;
            case "reload":
                LoadSettings();
                c.Reply(_settings.Enabled ? $"INI loaded: {_catalog.Groups.Count} groups." :
                    "Refills disabled; inspect chests.json, endless-chests.ini and kiln.log.");
                break;
            case "status":
                c.Reply($"Endless Chests: enabled={_settings.Enabled}, groups={_catalog.Groups.Count}, " +
                    $"aliases={_catalog.Aliases.Count}, known hashes={_catalog.RecognizedNames}, " +
                    $"autoNames={_settings.AutoNames}, clearOnRename={_settings.ClearOnRename}, " +
                    $"autoEmpty={_settings.AutoEmpty}, blocked items={_catalog.DisabledItems.Count}, " +
                    $"items={Items.Available}, world={World.Available}, spawn={Spawn.Available}, " +
                    $"GUID={Saves.WorldId ?? "not ready"}, bindings={_settings.Chests.Count}. " +
                    "Original-name matching available (exact FNV-1a UTF-8 hash).");
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
            case "nameprobe":
            case "probe":
                ProbeChestName(c);
                break;
            case "refill":
                _elapsed = 999;
                c.Reply("Refill check requested for the next world step.");
                break;
            default:
                throw c.Usage($"Unknown action '{verb}'");
        }
    }

    // Read-only investigation of the CustomPlayerString ECS component identified
    // in Greeneye0/enshrouded-quick-deposit. The server-side component may be
    // missing, or its 8-byte value may not resolve against a client-only store.
    // Never decode arbitrary pointers, walk private string tables or change
    // game state here. This command is restricted to Kiln admins.
    private void ProbeChestName(CommandContext c)
    {
        var (chest, at) = Nearest(c);
        const string component = "CustomPlayerString";
        var bytes = World.ComponentSize(chest, component);
        string candidate;
        if (bytes == 0)
            candidate = "component absent on this server-world entity";
        else if (bytes != sizeof(ulong))
            candidate = "not interpreted: unexpected component layout";
        else if (World.TryRead<ulong>(chest, component, out var rawId))
            candidate = rawId == 0
                ? "0 (no string reference observed)"
                : $"0x{rawId:X16} (candidate string ID, NOT decoded text)";
        else
            candidate = "component found but read failed";

        var template = Spawn.Inspect(chest)?.TemplateName ?? "unknown";
        var world = Saves.WorldId;
        var binding = world is null ? null : BoundAt(world, at)?.Selection;
        var known = TryGameSelection(chest, out var matched, out _) ? matched : "(unrecognized)";
        c.Reply($"NAME PROBE [read-only, server ECS]: chest={chest}, " +
            $"template={template}, position={at.X:F2},{at.Y:F2},{at.Z:F2}, " +
            $"CustomPlayerString.size={bytes}, reference={candidate}; " +
            $"known-name={known}, Kiln binding={binding ?? "(none)"}. " +
            "Known names matched by verified FNV-1a hashes; game string store not accessed. No inventory or binding changes.");
    }

    // Legacy console preview remains an explicitly confirmed operation.
    // In-game Rename UI has separate, automatic clear-on-rename/Empty handling
    // in Tick, as requested by the world owner.
    private void PreviewEmpty(CommandContext c)
    {
        if (!_settings.Enabled) c.Fail("Endless Chests is disabled.");
        if (Items.Status() is not { Transactions: true }) c.Fail("Kiln.Items cannot transact yet.");
        var (chest, at) = Nearest(c);
        var world = WorldId();
        var bound = BoundAt(world, at);
        if (_busy.Contains(chest))
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
        if (_busy.Contains(chest))
            c.Fail("A refill is still in progress; preview again after it finishes.");
        _pendingEmpty = null;
        // Stop future refills BEFORE any asynchronous item removal is submitted.
        if (bound != null)
        {
            _settings.Chests.Remove(bound);
            SaveSettings();
        }
        var generation = _generation;
        _busy.Add(chest);
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
        _busy.Remove(chest);
    }

    // World-scoped, runtime-only rename observation. First observation of a
    // normal named chest is a baseline, not a rename: this prevents destructive
    // clears merely because the world or new mod version just loaded.
    private void ObserveNames(string world, List<(EntityId Id, WorldPosition At)> candidates)
    {
        if (!_settings.AutoNames) return;
        foreach (var (id, at) in candidates)
        {
            if (!ChestNames.TryReadHash(id, out var hash))
                continue; // Never mistake a temporarily unavailable component for a rename.

            if (!_nameSeen.TryGetValue(id, out var seen) || Distance2(seen.At, at) > 0.01)
            {
                _nameSeen[id] = new ObservedChest(at, hash);
                if (ChestNames.IsEmptyHash(hash))
                    Log.Info($"Endless Chests: chest {id} is named Empty; automatic deletion is enabled.");
                // An existing Empty label is intentional and always has clearing semantics.
            }
            else if (seen.Hash != hash)
            {
                var previous = seen.Hash;
                seen.Hash = hash;
                // Only a recognised destination is permitted to destroy
                // inventory. An unknown label means STOP refilling and LEAVE
                // every existing item untouched, including after a prior
                // clear that was interrupted by another rename.
                var recognized = ChestNames.IsEmptyHash(hash) ||
                    _catalog.TryGameName(hash, out _);
                seen.NeedsClear = _settings.ClearOnRename && recognized;
                _pendingEmpty = null;
                var from = _catalog.TryGameName(previous, out var priorName) ? priorName : $"0x{previous:X16}";
                var to = ChestNames.IsEmptyHash(hash) ? "Empty" :
                    _catalog.TryGameName(hash, out var nextName) ? nextName : "(unrecognized)";
                Log.Info($"Endless Chests: game-UI rename on chest {id}: '{from}' -> '{to}'; " +
                    (!recognized ? "unknown destination; contents preserved and no auto refill." :
                    seen.NeedsClear ? "old contents scheduled for permanent deletion." :
                    "clearOnRename disabled; existing contents preserved."));

                // A manual binding would refill its old selection following the
                // rename. Drop it when game-UI rename control takes over.
                var binding = BoundAt(world, at);
                if (binding is not null)
                {
                    _settings.Chests.Remove(binding);
                    SaveSettings();
                    Log.Info($"Endless Chests: detached manual binding '{binding.Selection}' after in-game rename.");
                }
            }

            // Empty always overrides any existing manual assignment, even if
            // it was already named Empty before this process began.
            if (ChestNames.IsEmptyHash(hash))
            {
                var binding = BoundAt(world, at);
                if (binding is not null)
                {
                    _settings.Chests.Remove(binding);
                    SaveSettings();
                    Log.Info($"Endless Chests: removed manual binding because chest {id} is named Empty.");
                }
            }
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
            if (!string.Equals(_lastWorldId, world, StringComparison.Ordinal))
            {
                _lastWorldId = world;
                _nameSeen.Clear();
            }

            var candidates = StorageChests();
            ObserveNames(world, candidates);
            var tolerance2 = _settings.PositionToleranceMeters * _settings.PositionToleranceMeters;
            var generation = _generation;

            foreach (var (id, at) in candidates)
            {
                if (_busy.Contains(id)) continue;

                var binding = _settings.Chests.FirstOrDefault(b => b.WorldId == world &&
                    Distance2(At(b), at) <= tolerance2);

                var observed = _settings.AutoNames && _nameSeen.TryGetValue(id, out var o) ? o : null;
                var isEmpty = observed is not null && ChestNames.IsEmptyHash(observed.Hash);

                // A hash change requires clearing before ANY new refill.
                // The pending state is retained while another transaction is busy.
                // The Empty name also clears contents observed on startup and any
                // items deposited into it later, without a second command.
                if (observed is not null &&
                    (observed.NeedsClear || (isEmpty && _settings.AutoEmpty)))
                {
                    var inventory = Items.Read(id);
                    if (inventory is not { Initialized: true }) continue;
                    if (inventory.UsedSlots > 0)
                    {
                        _busy.Add(id);
                        _ = ClearOnName(id, at, world, observed, generation);
                        continue;
                    }

                    observed.NeedsClear = false;
                    // The chest is already empty. It can receive its renamed
                    // selection immediately if the name is not "Empty".
                }

                if (isEmpty) continue; // No refill while named Empty.

                if (binding is not null &&
                    !string.Equals(binding.Selection, "Empty", StringComparison.OrdinalIgnoreCase))
                {
                    _busy.Add(id);
                    _ = Refill(binding.Selection, id, generation, expectedHash: observed?.Hash);
                    continue;
                }

                if (!_settings.AutoNames || observed is null ||
                    !_catalog.TryGameName(observed.Hash, out var selection) ||
                    string.Equals(selection, "Empty", StringComparison.OrdinalIgnoreCase))
                    continue;

                _busy.Add(id);
                _ = Refill(selection, id, generation, expectedHash: observed.Hash);
            }
        }
        catch (Exception ex) { Log.Warn($"Endless Chests: chest scan failed: {ex.Message}"); }
    }

    private bool SameChest(EntityId chest, WorldPosition at, string world, ulong expectedHash)
    {
        return !_stopping && _settings.Enabled && string.Equals(Saves.WorldId, world, StringComparison.Ordinal) &&
            ChestNames.TryReadHash(chest, out var hash) && hash == expectedHash &&
            World.Position(chest) is { } current && Distance2(current, at) < 0.01;
    }

    // Exactly the inventory that was present when this rename was observed is
    // removed, slot by slot, using the same vetted Kiln transaction path as
    // the previous confirmed Empty command. If the name, entity identity,
    // world or slot contents change while asynchronous operations are running,
    // stop; a future scan will retry any pending clearing.
    private async Task ClearOnName(
        EntityId chest, WorldPosition at, string world, ObservedChest seen, int generation)
    {
        var hash = seen.Hash;
        var cleared = 0;
        try
        {
            if (!SameChest(chest, at, world, hash)) return;
            var inventory = Items.Read(chest);
            if (inventory is not { Initialized: true }) return;
            var snapshot = inventory.Slots.Where(s => !s.IsEmpty).ToArray();
            foreach (var slot in snapshot)
            {
                if (generation != _generation || !SameChest(chest, at, world, hash))
                    return;
                var now = Items.Read(chest)?.Slots.FirstOrDefault(s => s.Slot == slot.Slot);
                if (now is null || now.IsEmpty || now.Item != slot.Item ||
                    now.Count != slot.Count || now.ItemEntity != slot.ItemEntity)
                {
                    Log.Warn($"Endless Chests: rename clear paused for chest {chest}: slot {slot.Slot} changed during deletion.");
                    return;
                }
                var result = await Items.TakeSlot(chest, slot);
                if (result.Status != ItemsStatus.Done)
                {
                    Log.Warn($"Endless Chests: rename clear failed for chest {chest}: {result}");
                    return;
                }
                cleared++;
            }
            if (generation != _generation || !SameChest(chest, at, world, hash) || seen.Hash != hash)
                return;

            seen.NeedsClear = false;
            var label = _catalog.TryGameName(hash, out var name) ? name : "(unknown name)";
            Log.Warn($"Endless Chests: auto-cleared {cleared}/{snapshot.Length} slots in chest {chest} after name '{label}'.");

            if (!ChestNames.IsEmptyHash(hash) && _catalog.TryGameName(hash, out var selection))
                await GiveSelection(selection, chest, generation, expectedHash: hash);
        }
        catch (Exception ex) { Log.Warn($"Endless Chests: automatic clearing on chest {chest} failed: {ex.Message}"); }
        finally { _busy.Remove(chest); }
    }

    private async Task Refill(string selection, EntityId chest, int generation, ulong? expectedHash)
    {
        try { await GiveSelection(selection, chest, generation, expectedHash); }
        finally { _busy.Remove(chest); }
    }

    private async Task GiveSelection(string selection, EntityId chest, int generation, ulong? expectedHash)
    {
        try
        {
            var first = Items.Read(chest);
            if (first is not { Initialized: true }) return;
            var plan = _catalog.Resolve(selection, Math.Min(first.GenericSlots, 72));
            if ((plan.Unknown.Count != 0 || plan.Blocked.Count != 0 || plan.Ambiguous.Count != 0) &&
                _warned.Add(selection))
                Log.Warn($"Endless Chests: '{selection}': {plan.Unknown.Count} unavailable names" +
                    (plan.Unknown.Count != 0 ? $" ({string.Join(", ", plan.Unknown.Take(3))})" : "") +
                    $", {plan.Blocked.Count} blocked, {plan.Items.Count} valid types, " +
                    $"{plan.Ambiguous.Count} unpinned duplicate display names. " +
                    (plan.Ambiguous.Count != 0
                        ? $"Examples: {string.Join("; ", plan.Ambiguous.Take(3))}. " +
                          "Use exact numeric item IDs in your INI to disambiguate."
                        : ""));
            foreach (var item in plan.Items)
            {
                if (_stopping || generation != _generation || !_settings.Enabled) break;
                if (expectedHash is ulong wantedHash)
                {
                    if (!ChestNames.TryReadHash(chest, out var currentHash) || currentHash != wantedHash)
                        break;
                }
                else if (TryGameSelection(chest, out var changedName, out _) &&
                    string.Equals(changedName, "Empty", StringComparison.OrdinalIgnoreCase))
                    break;
                var inv = Items.Read(chest);
                if (inv is not { Initialized: true }) break;
                var wanted = item.IsGear ? 1 : Math.Clamp(item.MaxStack, 1, 250);
                var missing = wanted - inv.Count(item.Id);
                if (missing <= 0) continue;
                var result = await Items.Give(chest, item.Id, missing, options: GiveOptions.AllOrNothing);
                if (!result.Succeeded)
                {
                    if (result.Status is not (ItemsStatus.NoRoom or ItemsStatus.NoInventory))
                        Log.Warn($"Endless Chests: '{selection}' item {item.Label}: {result}");
                    break;
                }
            }
        }
        catch (Exception ex) { Log.Warn($"Endless Chests: refill of '{selection}' failed: {ex.Message}"); }
    }
}
