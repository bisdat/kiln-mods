// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Kiln;

namespace Bisdat.BaseStationMagicStorage;

/// <summary>
/// Base-local, opt-in automation of FactoryStations via the game's own
/// Magic Chest fill, native output sharing and named-chest collection (Kiln API 0.2.9).
/// </summary>
public sealed class BaseStationMagicStorageMod : Mod
{
    private const double ScanSeconds = 5;
    private const int MaxTarget = 20;
    private const int MaxStationsPerScan = 128;

    // Only explicitly named, unbound Magic Chests are acceptable output buffers.
    // Endless Chests can automatically DELETE the contents of an "Empty" chest:
    // never send finished products to arbitrary storage chosen by FindRoomFor.
    private const string OutputSinkName = "crafting output";
    private const string OutputSinkKey = "craftingoutput";

    private sealed class Settings
    {
        public List<BasePolicy> Bases { get; set; } = [];
        // Remember which opt-ins BSMS owns, even over save/reload.
        public List<SharedSource> SharedSources { get; set; } = [];
    }

    private sealed class SharedSource
    {
        public string World { get; set; } = "";
        public string Station { get; set; } = "";
    }

    private sealed class BasePolicy
    {
        public string World { get; set; } = "";
        public string Altar { get; set; } = "";
        public bool Enabled { get; set; } // default OFF
        public bool AutoFill { get; set; } = true;
        public bool AutoCollect { get; set; } = true;
        // A base may contain dozens of stations: opt in individually until
        // an admin explicitly enables all station types for that base.
        public bool AllStations { get; set; } // default OFF
        public List<string> Included { get; set; } = [];
        public int TargetCrafts { get; set; } = 3;
        public List<string> Excluded { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Old 0.1.3 candidate keys -> displayed player-facing names. Read before
    // Config.Register rewrites the canonical JSONC file.
    private static readonly (string Old, string New)[] RenamedStationKeys =
    [
        ("cardingMachine", "spinningMachine"),
        ("coffeeBrewer", "coffeeBrewery"),
        ("seedlingStation", "seedbed"),
        ("improvedGrinder", "mill"),
        ("handGrinder", "grindingStones"),
        ("honeyStation", "beehive"),
        ("infusionStation", "infusionAltar"),
        ("fireplace", "cookingStation"),
        ("coffeeRoaster", "coffeeDrumRoaster"),
        ("medievalGrill", "seafoodGrill"),
    ];

    private static Dictionary<string, bool> LegacyStationValues()
    {
        var migrated = new Dictionary<string, bool>(StringComparer.Ordinal);
        var file = Path.Combine(Config.Directory, "base-station-magic-storage.json");
        if (!File.Exists(file)) return migrated;

        using var doc = JsonDocument.Parse(File.ReadAllText(file),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("BSMS station-type config root must be an object.");

        foreach (var (oldKey, newKey) in RenamedStationKeys)
        {
            // New user choices always win over stale legacy keys.
            if (doc.RootElement.TryGetProperty(newKey, out _)) continue;
            if (!doc.RootElement.TryGetProperty(oldKey, out var previous)) continue;
            if (previous.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException($"Invalid saved boolean for '{oldKey}'.");
            migrated[newKey] = previous.GetBoolean();
        }
        return migrated;
    }

    private ModConfig<BSMSStationConfig> _stationTypes = null!;
    private Settings _settings = new();
    private string _settingsFile = "";
    private CommandRegistration? _command;
    private bool _configOk = true, _stopping, _scanning;
    private double _elapsed;
    private readonly Dictionary<string, (string Message, DateTime When)> _warnings = new();

    public override void OnLoad()
    {
        // Uses Kiln's native in-game mod-config editor, not a custom UI or
        // per-physical-station JSON. This config is shared by all bases.
        Dictionary<string, bool> oldValues;
        bool migrationOk = true;
        try
        {
            oldValues = LegacyStationValues();
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            oldValues = new();
            migrationOk = false;
            Log.Error(e, "BSMS: station-type config migration check failed; automation disabled.");
        }
        _stationTypes = Config.Register<BSMSStationConfig>();
        // Import only existing values without a new key; no reset to defaults.
        foreach (var (name, saved) in oldValues)
        {
            if (_stationTypes.TrySet(name, saved, out var error)) continue;
            migrationOk = false;
            Log.Error($"BSMS: could not preserve saved station switch '{name}': {error}");
        }
        _stationTypes.Changed += OnStationConfigChanged;
        Directory.CreateDirectory(Context.DataDirectory);
        _settingsFile = Path.Combine(Context.DataDirectory, "base-station-magic-storage.json");
        LoadSettings();
        if (!migrationOk) _configOk = false;
        _command = Commands.Register("bstation", HandleCommand, CommandPermission.Admin,
            usage: "status|bases|list|sinks|stock|plan [n]|station on|off|all on|off|enable [n]|disable|target n|fill on|off|collect on|off|exclude|include",
            description: "Opt-in Magic Chest automation of base production stations.");
        Context.Tick.Pre += OnTick;
        Log.Info("Base Station Magic Storage v0.1.4 loaded: 26 station-type switches plus ShareOutputs / CollectOutputs in Kiln Configs; " +
                 "sharing ON and collection OFF by default; per-base automation OFF until bstation enable.");
    }

    private void OnStationConfigChanged(ConfigChangedEvent change)
    {
        Log.Info($"BSMS: station-type switches updated in Kiln Configs: {string.Join(", ", change.Changed)}");
        // Reconcile sharing and collection at the next five-second scan.
        // Kiln validates native payments on the server at transaction time.
    }

    public override void OnUnload()
    {
        _stationTypes.Changed -= OnStationConfigChanged;
        _stopping = true;
        Context.Tick.Pre -= OnTick;
        _command?.Dispose();
        _warnings.Clear();
    }

    private void LoadSettings()
    {
        try
        {
            _settings = File.Exists(_settingsFile)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsFile), Json)
                    ?? throw new InvalidDataException("Empty settings file.")
                : new Settings();
            if (_settings.SharedSources is null || _settings.SharedSources.Count > 4096 ||
                _settings.SharedSources.Any(x => x is null || string.IsNullOrWhiteSpace(x.World) ||
                    x.World.Length > 128 || string.IsNullOrWhiteSpace(x.Station) || x.Station.Length > 256) ||
                _settings.Bases is null || _settings.Bases.Count > 64 ||
                _settings.Bases.Any(p => p.World.Length is < 1 or > 128 ||
                    !Guid.TryParse(p.Altar, out _) || p.TargetCrafts is < 1 or > MaxTarget ||
                    p.Excluded is null || p.Excluded.Count > 256 ||
                    p.Excluded.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 256) ||
                    p.Included is null || p.Included.Count > 256 ||
                    p.Included.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 256)))
                throw new InvalidDataException("Invalid base policies.");
            _configOk = true;
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            _settings = new();
            _configOk = false;
            Log.Error(e, "BSMS: settings invalid; automation disabled, file left untouched.");
        }
    }

    private bool Save()
    {
        try
        {
            var temp = _settingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_settings, Json));
            File.Move(temp, _settingsFile, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _configOk = false;
            Log.Error(e, "BSMS: cannot save settings; automation disabled.");
            return false;
        }
    }

    private static string? WorldId() => Saves.WorldId;

    // The base id can change when altar zones merge; the altar's own id persists.
    // An ambiguous merge of two configured bases fails closed.
    private BasePolicy? Policy(BaseInfo b, string world)
    {
        var candidates = _settings.Bases.Where(p => p.World == world &&
            b.Altars.Any(a => string.Equals(a.Id.ToString(), p.Altar, StringComparison.OrdinalIgnoreCase)))
            .Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private BasePolicy AddOrGetPolicy(BaseInfo b, string world)
    {
        if (Policy(b, world) is { } existing) return existing;
        if (b.Altars.Count == 0) throw new CommandException("No persistent Flame Altar ID.");
        if (_settings.Bases.Any(p => p.World == world &&
            b.Altars.Any(a => string.Equals(a.Id.ToString(), p.Altar, StringComparison.OrdinalIgnoreCase))))
            throw new CommandException("Multiple policies match this merged base; resolve its settings first.");
        var added = new BasePolicy { World = world, Altar = b.Altars[0].Id.ToString() };
        _settings.Bases.Add(added);
        return added;
    }

    // Station entity ids change after restarts: key per template and decimetre-rounded position.
    private static string Key(StationInfo s) =>
        (s.Template ?? $"workshop-{s.Workshop:x8}") + "@" +
        string.Join(",", new[] { s.Position.X, s.Position.Y, s.Position.Z }
            .Select(n => Math.Round(n, 1, MidpointRounding.AwayFromZero)
                .ToString("F1", CultureInfo.InvariantCulture)));

    // Each bool applies to every instance of this template, regardless of
    // world, base, entity ID or world position. Unknown templates fail closed.
    private bool StationTypeEnabled(StationInfo s)
    {
        var enabled = _stationTypes.Value;
        return s.Template switch
        {
            "T3_Alchemy_Station_Factory" => enabled.AlchemyStation,
            "T2_Kiln_Factory" => enabled.Kiln,
            "T5_cardingMachine_Factory" => enabled.SpinningMachine,
            "T5_BlastFurnace_Factory" => enabled.BlastFurnace,
            "T6_coffeemaker_brewer_Factory" => enabled.CoffeeBrewery,
            "T1_Seedling_Factory" => enabled.Seedbed,
            "T6_Improved_Grinder_Factory" => enabled.Mill,
            "T1_Drying_Rack_Factory" => enabled.DryingRack,
            "T1_Forge_Factory" => enabled.Forge,
            "T3_Oven_Factory" => enabled.Oven,
            "T2_Smelter_Factory" => enabled.Smelter,
            "T2_Table_Saw_Factory" => enabled.TableSaw,
            "T1_HandGrinder_Factory" => enabled.GrindingStones,
            "T4_SpinningWheel_Factory" => enabled.SpinningWheel,
            "T4_Loom_Factory" => enabled.Loom,
            "T2_HandSpindle_Factory" => enabled.HandSpindle,
            "T4_Honey_Factory" => enabled.Beehive,
            "T4_Laboratory_Factory" => enabled.Laboratory,
            "T7_Infusion_Station_Factory" => enabled.InfusionAltar,
            "T2_Fireplace_Factory" => enabled.CookingStation,
            "T2_ColoringStation_Factory" => enabled.ColoringStation,
            "T6_coffemaker_roaster_Factory" => enabled.CoffeeDrumRoaster,
            "T2_EctoplasmPress_Factory" => enabled.EctoplasmPress,
            "T1_Charcoal_Kiln_Factory" => enabled.CharcoalKiln,
            "T3_Tanning_Station_Factory" => enabled.TanningStation,
            "T6_Medieval_Grill_Factory" => enabled.SeafoodGrill,
            _ => false,
        };
    }

    private bool Eligible(StationInfo s, BasePolicy? p) =>
        p is { Enabled: true } && StationTypeEnabled(s) &&
        !p.Excluded.Contains(Key(s), StringComparer.Ordinal) &&
        (p.AllStations || p.Included.Contains(Key(s), StringComparer.Ordinal));

    // Do not opt out a station owned by another mod: only touch registrations
    // that BSMS recorded for its own world/station identity.
    private bool SyncOutputSource(EntityId id, StationInfo s, string world, bool wanted,
        List<EntityId> justEnabled)
    {
        var key = Key(s);
        var ours = _settings.SharedSources.FirstOrDefault(x => x.World == world && x.Station == key);
        if (!Capabilities.Is(Stations.OutputSourcesCapability))
        {
            if (wanted) Warn(id, "share", "stations.output_sources unavailable; sharing is off.");
            return false;
        }

        var active = Stations.IsOutputSource(id);
        if (!wanted)
        {
            if (ours is null) return false;
            if (active) Stations.SetOutputSource(id, on: false);
            _settings.SharedSources.Remove(ours);
            return true;
        }
        if (ours is not null)
        {
            // Reapply if an opt-in was session-only and not restored on reload.
            if (!active) Stations.SetOutputSource(id);
            return false;
        }
        if (active)
        {
            // An unrelated mod already opted this station in.
            return false;
        }
        bool persisted = Stations.SetOutputSource(id);
        _settings.SharedSources.Add(new SharedSource { World = world, Station = key });
        justEnabled.Add(id);
        if (!persisted) Warn(id, "share", "Kiln could not persist this opt-in; BSMS will reapply on the next session.");
        return true;
    }

    private static EntityId Requester(CommandContext c)
    {
        var player = c.Source.Player;
        if (player.IsNone && Players.All is [var only]) player = only.Entity;
        return player;
    }

    // This name is deliberately not an Endless Chests catalogue name.
    // It is still the owner's responsibility to leave the chest unbound from
    // other automatic clearing/refilling mods.
    // Case-insensitive: ignore whitespace anywhere in the name and accept any suffix.
    // A chest must still be deposit-enabled Magic storage in the station's own base.
    private static bool OutputName(string? name) =>
        name is not null && new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray())
            .StartsWith(OutputSinkKey, StringComparison.OrdinalIgnoreCase);

    private static bool CanAccept(EntityId chest, ItemId item)
    {
        var inv = Items.Read(chest);
        if (inv is not { Initialized: true } || inv.IsPlayer || inv.IsNpc) return false;
        var max = Items.Get(item)?.MaxStack ?? 1;
        return inv.Slots.Any(slot => slot.IsEmpty || (slot.Item == item && slot.Count < max));
    }

    private static IReadOnlyList<EntityId> OutputSinks(BaseId b)
    {
        if (!Items.ContainerNamesAvailable) return [];
        return Bases.Storage(b, deposit: true)
            .Where(c => OutputName(Items.ContainerName(c)))
            .ToArray();
    }

    private static int Crafts(string value) =>
        int.TryParse(value, out var n) && n is >= 1 and <= MaxTarget
            ? n : throw new CommandException($"Crafts must be between 1 and {MaxTarget}.");

    private void HandleCommand(CommandContext c)
    {
        void Say(string message) => c.Reply("BSMS: " + message);
        try
        {
            var verb = c.ArgOrDefault(0, "status")!.ToLowerInvariant();
            if (verb == "status")
            {
                var world = WorldId();
                Say($"v0.1.4 config={(_configOk ? "OK" : "INVALID/OFF")}, " +
                    $"world={world ?? "none"}, stations={Stations.Available}, bases={Bases.Available}, " +
                    $"scanned={Stations.Scanned}, read={Capabilities.Is(Stations.ReadCapability)}, " +
                    $"fill={Capabilities.Is(Stations.FillCapability)}, items.move={Capabilities.Is("items.move")}, " +
                    $"items.names={Items.ContainerNamesAvailable}, " +
                    $"share={_stationTypes.Value.ShareOutputs}, collect={_stationTypes.Value.CollectOutputs}, " +
                    $"outputSources={Capabilities.Is(Stations.OutputSourcesCapability)}, " +
                    $"craftSources={Capabilities.Is(Stations.CraftSourcesCapability)}, " +
                    $"craftUi={Capabilities.Is(Stations.CraftUiCapability)}, " +
                    $"owned sources here={_settings.SharedSources.Count(x => x.World == world)}, " +
                    $"enabled bases here={_settings.Bases.Count(p => p.World == world && p.Enabled)}. " +
                    "All bases are disabled by default.");
                return;
            }
            if (!_configOk) throw new CommandException("Invalid settings: automation disabled; restore the settings file.");
            if (!Stations.Available || !Bases.Available || !Capabilities.Is(Stations.ReadCapability))
                throw new CommandException("Requires Kiln dev.29.17 / SDK API 0.2.9 and an active server world.");
            var w = WorldId() ?? throw new CommandException("Enter a saved world.");
            if (verb == "bases")
            {
                foreach (var b in Bases.All.Take(16))
                {
                    var p = Policy(b, w);
                    Say($"{b.Id}: stations={b.StationCount}, Magic stock={b.StorageCount}, " +
                        $"enabled={p?.Enabled == true}, all={p?.AllStations == true}, selected={p?.Included.Count ?? 0}, " +
                        $"target={p?.TargetCrafts}, fill={p?.AutoFill}, baseCollect={p?.AutoCollect}, " +
                        $"share={_stationTypes.Value.ShareOutputs}, collect={_stationTypes.Value.CollectOutputs}; " +
                        $"altars={string.Join(",", b.Altars.Select(a => a.Id))}");
                }
                return;
            }

            var who = Requester(c);
            if (who.IsNone) throw new CommandException("Run as a player; file console requires exactly one player.");
            var home = Bases.Of(who) ?? throw new CommandException("Stand inside a Flame Altar base.");
            var existing = Policy(home, w);

            if (verb == "sinks")
            {
                if (!Items.ContainerNamesAvailable)
                    throw new CommandException("Kiln items.names is unavailable; output collection is disabled.");
                var sinks = OutputSinks(home.Id);
                Say($"Base {home.Id}: {sinks.Count} dedicated output chests (name starts with '{OutputSinkName}'; ignore case/spaces, any suffix).");
                foreach (var chest in sinks.Take(20))
                {
                    var inv = Items.Read(chest);
                    Say($"  {chest}: '{Items.ContainerName(chest)}', {inv?.UsedSlots ?? 0} used slot(s), deposit enabled.");
                }
                if (sinks.Count > 20) Say($"  plus {sinks.Count - 20} additional output chests.");
                return;
            }

            if (verb == "stock")
            {
                var stock = Bases.Stock(home.Id);
                var produced = Stations.InBase(home.Id).Select(id => Stations.Read(id))
                    .Where(si => si is { SelectedRecipe: not 0 })
                    .SelectMany(si => Stations.Recipe(si!.SelectedRecipe)?.Outputs ?? [])
                    .Select(output => output.Item)
                    .Distinct()
                    .Take(48)
                    .ToArray();
                Say($"Base {home.Id}: {stock.Count} item types in the base's Magic Chest stock; " +
                    $"{produced.Length} distinct types from selected production recipes.");
                foreach (var item in produced)
                    Say($"  produced item {item}: Magic Chest stock now {stock.GetValueOrDefault(item)}");
                return;
            }

            if (verb == "list")
            {
                Say($"{home.Id}: stations={home.StationCount}, Magic stock={home.StorageCount}, " +
                    $"water={home.WaterCount}, enabled={existing?.Enabled == true}, all={existing?.AllStations == true}, " +
                    $"selected={existing?.Included.Count ?? 0}.");
                foreach (var id in Stations.InBase(home.Id).Take(64))
                {
                    var s = Stations.Read(id);
                    if (s is null) continue;
                    Say($"{id}: {s.Template ?? $"workshop 0x{s.Workshop:x8}"} " +
                        $"state={s.State}, recipe=0x{s.SelectedRecipe:x8}, loaded={s.CraftsLoaded}, " +
                        $"outputs={s.Outputs.Count} stacks ({s.Outputs.Sum(x => x.Count)} items), stopped={s.StoppedByPlayer}, " +
                        $"excluded={existing?.Excluded.Contains(Key(s), StringComparer.Ordinal) == true}, " +
                        $"opted-in={existing?.Included.Contains(Key(s), StringComparer.Ordinal) == true}, " +
                        $"type-enabled={StationTypeEnabled(s)}, output-source={Stations.IsOutputSource(id)}");
                }
                if (home.StationCount > 64) Say("Only first 64 stations shown.");
                return;
            }

            if (verb == "plan")
            {
                var id = Stations.Nearest(who, null, 12) ?? EntityId.None;
                var s = id.IsNone ? null : Stations.Read(id);
                if (s is null || s.Base != home.Id) throw new CommandException("No production station within 12 metres in this base.");
                var target = c.Count > 1 ? Crafts(c.Args[1]) : existing?.TargetCrafts ?? 3;
                var plan = Stations.Plan(id, new FillOptions { Crafts = target, Mode = FillMode.UpTo,
                    Water = true, UseStationOutputs = _stationTypes.Value.ShareOutputs });
                Say($"{id} {s.Template}: plan target={target}, {plan.Status}, " +
                    $"affordable={plan.CraftsAffordable}, planned={plan.CraftsPlanned}, " +
                    $"short={plan.LimitedBy} {plan.ShortItem}, station-water={plan.WaterHave}, base-water={plan.WaterStock}. " +
                    $"shareMode={_stationTypes.Value.ShareOutputs}, sourcesAvailable={Capabilities.Is(Stations.OutputSourcesCapability)}. " +
                    "READ ONLY: no inventory changes.");
                foreach (var i in plan.Inputs.Take(12))
                    Say($"  item {i.Item}: per craft={i.PerCraft}, station={i.Have}, Magic stock={i.Stock}, shared station outputs={i.Outputs}");
                Say("Note: native station Fill button can remain disabled even when shared outputs cover the recipe. " +
                    "This read-only plan checks the mod API, not the vanilla button.");
                return;
            }

            if (verb is not ("enable" or "disable" or "target" or "fill" or "collect" or "exclude" or "include" or "station" or "all"))
                throw new CommandException("Use status|bases|list|sinks|stock|plan [n]|station on|off|all on|off|enable [n]|disable|target n|fill on|off|collect on|off|exclude|include.");
            var p2 = AddOrGetPolicy(home, w);
            switch (verb)
            {
                case "enable":
                    if (!Capabilities.Is(Stations.FillCapability) || !Capabilities.Is("items.move"))
                        throw new CommandException("stations.fill or items.move unavailable.");
                    if (c.Count > 1) p2.TargetCrafts = Crafts(c.Args[1]);
                    p2.Enabled = true;
                    break;
                case "disable":
                    p2.Enabled = false;
                    break;
                case "target":
                    if (c.Count < 2) throw new CommandException("target <1..20>");
                    p2.TargetCrafts = Crafts(c.Args[1]);
                    break;
                case "fill":
                    if (c.Count < 2 || (c.Args[1] != "on" && c.Args[1] != "off"))
                        throw new CommandException("fill on|off");
                    p2.AutoFill = c.Args[1] == "on";
                    break;
                case "collect":
                    if (c.Count < 2 || (c.Args[1] != "on" && c.Args[1] != "off"))
                        throw new CommandException("collect on|off");
                    p2.AutoCollect = c.Args[1] == "on";
                    break;
                case "all":
                    if (c.Count < 2 || (c.Args[1] != "on" && c.Args[1] != "off"))
                        throw new CommandException("all on|off");
                    p2.AllStations = c.Args[1] == "on";
                    break;
                case "station":
                case "exclude":
                case "include":
                {
                    var id = Stations.Nearest(who, null, 12) ?? EntityId.None;
                    var s = id.IsNone ? null : Stations.Read(id);
                    if (s is null || s.Base != home.Id)
                        throw new CommandException("Stand within 12 metres of a production station.");
                    var key = Key(s);
                    if (verb == "station")
                    {
                        if (c.Count < 2 || (c.Args[1] != "on" && c.Args[1] != "off"))
                            throw new CommandException("station on|off");
                        p2.Included.RemoveAll(k => string.Equals(k, key, StringComparison.Ordinal));
                        if (c.Args[1] == "on")
                        {
                            p2.Included.Add(key);
                            p2.Excluded.RemoveAll(k => string.Equals(k, key, StringComparison.Ordinal));
                        }
                    }
                    else
                    {
                        p2.Excluded.RemoveAll(k => string.Equals(k, key, StringComparison.Ordinal));
                        if (verb == "exclude") p2.Excluded.Add(key);
                    }
                    break;
                }
            }
            if (!Save()) throw new CommandException("Could not save settings; automation disabled.");
            Say($"Base {home.Id}: enabled={p2.Enabled}, fill={p2.AutoFill}, collect={p2.AutoCollect}, " +
                $"target={p2.TargetCrafts}, all={p2.AllStations}, selected={p2.Included.Count}, " +
                $"excluded={p2.Excluded.Count}. " +
                "No recipe or stop state changed.");
        }
        catch (Exception e) when (e is CommandException or KilnUnavailableException or
                                   IOException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            Say(e.Message);
        }
    }

    private void OnTick(in TickInfo tick)
    {
        if (_stopping || _scanning ||
            (!_settings.Bases.Any(p => p.Enabled) && _settings.SharedSources.Count == 0)) return;
        _elapsed += Math.Clamp(tick.WallDelta.TotalSeconds, 0, 1);
        if (_elapsed < ScanSeconds) return;
        _elapsed = 0;
        if (!Stations.Available || !Bases.Available || !Capabilities.Is(Stations.ReadCapability)) return;
        _ = Stations.All();
        if (!Stations.Scanned) return;
        if (WorldId() is { } world) _ = Scan(world);
    }

    private async Task Scan(string world)
    {
        if (_scanning) return;
        _scanning = true;
        var justEnabled = new List<EntityId>();
        var targets = new List<(EntityId Id, BaseId Base, BasePolicy Policy)>();
        bool sourcesChanged = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            // Do ALL source registration/revocation before any await: Kiln's
            // SetOutputSource is tick-thread-only. The later async collection
            // / fill service must not resume and call it from a worker thread.
            foreach (var b in Bases.All)
            {
                if (_stopping || WorldId() != world) return;
                var p = Policy(b, world);
                foreach (var id in Stations.InBase(b.Id))
                {
                    if (_stopping || WorldId() != world) return;
                    var s = Stations.Read(id);
                    if (s is null) continue;
                    seen.Add(Key(s));
                    var wantShare = _configOk && _stationTypes.Value.ShareOutputs && Eligible(s, p);
                    try
                    {
                        if (SyncOutputSource(id, s, world, wantShare, justEnabled)) sourcesChanged = true;
                    }
                    catch (Exception e) { Warn(id, "share", $"{e.GetType().Name}: {e.Message}"); }
                    // Only the costly auto-service is limited to 128 stations.
                    // Output-source revocation must cover every opted-in station.
                    if (_configOk && p is { Enabled: true } && Eligible(s, p) &&
                        ((p.AutoCollect && _stationTypes.Value.CollectOutputs) || p.AutoFill) &&
                        targets.Count < MaxStationsPerScan)
                        targets.Add((id, b.Id, p));
                }
            }
            if (_settings.SharedSources.RemoveAll(x => x.World == world && !seen.Contains(x.Station)) > 0)
                sourcesChanged = true;

            if (sourcesChanged && !Save())
            {
                // Do not leave newly enabled sources untracked on save failure.
                foreach (var id in justEnabled)
                {
                    try { Stations.SetOutputSource(id, on: false); }
                    catch (Exception e) { Warn(id, "share-rollback", e.Message); }
                }
                return;
            }

            foreach (var (id, b, p) in targets)
            {
                if (_stopping || WorldId() != world || !_configOk) return;
                await Service(id, b, p, world);
            }
        }
        catch (Exception e) { Log.Warn($"BSMS: scan interrupted: {e.GetType().Name}: {e.Message}"); }
        finally { _scanning = false; }
    }

    private async Task Service(EntityId id, BaseId @base, BasePolicy p, string world)
    {
        try
        {
            var s = Stations.Read(id);
            if (s is null || s.Base != @base || !Eligible(s, p)) return;
            if (_stationTypes.Value.CollectOutputs && p.AutoCollect && s.Outputs.Count > 0)
            {
                if (!Items.ContainerNamesAvailable)
                    Warn(id, "collect", "items.names unavailable; output remains safely at station.");
                else
                {
                    var sinks = OutputSinks(@base);
                    if (sinks.Count == 0)
                        Warn(id, "collect", "No dedicated output Magic Chest. Rename an unbound, deposit-enabled Magic Chest 'crafting output' (case/spaces ignored, any suffix).");
                    else
                    {
                        foreach (var item in s.Outputs.Select(slot => slot.Item).Distinct().ToArray())
                        {
                            foreach (var chest in sinks)
                            {
                                if (_stopping || !_configOk || WorldId() != world || !p.Enabled || !p.AutoCollect ||
                                    !_stationTypes.Value.CollectOutputs ||
                                    Stations.Read(id) is not { } current || !Eligible(current, p))
                                    return;
                                // A renamed, moved or deposit-disabled chest must never
                                // receive anything after the previous snapshot.
                                if (!Bases.Storage(@base, deposit: true).Contains(chest) ||
                                    !OutputName(Items.ContainerName(chest)) || !CanAccept(chest, item))
                                    continue;
                                var result = await Stations.Collect(id, chest, item);
                                Report(id, "collect", result);
                                if (Stations.Read(id)?.Outputs.All(slot => slot.Item != item) == true)
                                    break;
                            }
                        }
                    }
                }
            }
            if (_stopping || !_configOk || WorldId() != world || !p.Enabled || !p.AutoFill) return;
            s = Stations.Read(id); // refresh after async collection
            if (s is null || s.Base != @base || !Eligible(s, p) || s.SelectedRecipe == 0 ||
                s.StoppedByPlayer || s.StopPending || s.State == StationState.Stopped ||
                s.CraftsLoaded >= p.TargetCrafts) return;

            var options = new FillOptions
            {
                Crafts = p.TargetCrafts,
                Mode = FillMode.UpTo,
                Water = true,
                UseStationOutputs = _stationTypes.Value.ShareOutputs &&
                    Capabilities.Is(Stations.OutputSourcesCapability),
                // Player is intentionally absent: no backpack sourcing.
            };
            var plan = Stations.Plan(id, options);
            if (!plan.Ok || plan.CraftsPlanned <= 0) return;
            var check = Stations.Read(id);
            if (check is null || check.Base != @base || check.SelectedRecipe != s.SelectedRecipe ||
                check.StoppedByPlayer || check.StopPending || !Eligible(check, p)) return;

            var fill = await Stations.Fill(id, options);
            Report(id, "fill", fill);
        }
        catch (Exception e)
        {
            Warn(id, "exception", $"{e.GetType().Name}: {e.Message}");
        }
    }

    private void Report(EntityId id, string action, StationResult r)
    {
        if (r.Changed)
            Log.Info($"BSMS: {action} station {id}: {r}");
        else if (r.Status != StationActionStatus.Unchanged)
            Warn(id, action, r.Reason ?? r.Status.ToString());
    }

    private void Warn(EntityId id, string action, string message)
    {
        var key = id + ":" + action;
        var now = DateTime.UtcNow;
        if (_warnings.TryGetValue(key, out var prior) &&
            prior.Message == message && now - prior.When < TimeSpan.FromMinutes(2)) return;
        if (_warnings.Count > 256) _warnings.Clear();
        _warnings[key] = (message, now);
        Log.Warn($"BSMS: {action} station {id}: {message}");
    }
}
