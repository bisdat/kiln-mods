// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Kiln;

namespace Bisdat.BaseStationMagicStorage;

/// <summary>
/// Base-local, opt-in automation of FactoryStations via the game's own
/// Magic Chest fill and output collection transactions (Kiln API 0.2.6).
/// </summary>
public sealed class BaseStationMagicStorageMod : Mod
{
    private const double ScanSeconds = 5;
    private const int MaxTarget = 20;
    private const int MaxStationsPerScan = 128;

    private sealed class Settings
    {
        public List<BasePolicy> Bases { get; set; } = [];
    }

    private sealed class BasePolicy
    {
        public string World { get; set; } = "";
        public string Altar { get; set; } = "";
        public bool Enabled { get; set; } // default OFF
        public bool AutoFill { get; set; } = true;
        public bool AutoCollect { get; set; } = true;
        public int TargetCrafts { get; set; } = 3;
        public List<string> Excluded { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private Settings _settings = new();
    private string _settingsFile = "";
    private CommandRegistration? _command;
    private bool _configOk = true, _stopping, _scanning;
    private double _elapsed;
    private readonly Dictionary<string, (string Message, DateTime When)> _warnings = new();

    public override void OnLoad()
    {
        Directory.CreateDirectory(Context.DataDirectory);
        _settingsFile = Path.Combine(Context.DataDirectory, "base-station-magic-storage.json");
        LoadSettings();
        _command = Commands.Register("bstation", HandleCommand, CommandPermission.Admin,
            usage: "status|bases|list|plan [n]|enable [n]|disable|target n|fill on|off|collect on|off|exclude|include",
            description: "Opt-in Magic Chest automation of base production stations.");
        Context.Tick.Pre += OnTick;
        Log.Info("Base Station Magic Storage v0.1.0 loaded: automation OFF until bstation enable; " +
                 "FactoryStations only, base-local Magic Chests only.");
    }

    public override void OnUnload()
    {
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
            if (_settings.Bases is null || _settings.Bases.Count > 64 ||
                _settings.Bases.Any(p => p.World.Length is < 1 or > 128 ||
                    !Guid.TryParse(p.Altar, out _) || p.TargetCrafts is < 1 or > MaxTarget ||
                    p.Excluded is null || p.Excluded.Count > 256 ||
                    p.Excluded.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 256)))
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

    private static EntityId Requester(CommandContext c)
    {
        var player = c.Source.Player;
        if (player.IsNone && Players.All is [var only]) player = only.Entity;
        return player;
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
                Say($"v0.1.0 config={(_configOk ? "OK" : "INVALID/OFF")}, " +
                    $"world={world ?? "none"}, stations={Stations.Available}, bases={Bases.Available}, " +
                    $"scanned={Stations.Scanned}, read={Capabilities.Is(Stations.ReadCapability)}, " +
                    $"fill={Capabilities.Is(Stations.FillCapability)}, items.move={Capabilities.Is("items.move")}, " +
                    $"enabled bases here={_settings.Bases.Count(p => p.World == world && p.Enabled)}. " +
                    "All bases are disabled by default.");
                return;
            }
            if (!_configOk) throw new CommandException("Invalid settings: automation disabled; restore the settings file.");
            if (!Stations.Available || !Bases.Available || !Capabilities.Is(Stations.ReadCapability))
                throw new CommandException("Requires Kiln dev.29.14 / SDK API 0.2.6 and an active server world.");
            var w = WorldId() ?? throw new CommandException("Enter a saved world.");
            if (verb == "bases")
            {
                foreach (var b in Bases.All.Take(16))
                {
                    var p = Policy(b, w);
                    Say($"{b.Id}: stations={b.StationCount}, Magic stock={b.StorageCount}, " +
                        $"enabled={p?.Enabled == true}, target={p?.TargetCrafts}, collect={p?.AutoCollect}; " +
                        $"altars={string.Join(",", b.Altars.Select(a => a.Id))}");
                }
                return;
            }

            var who = Requester(c);
            if (who.IsNone) throw new CommandException("Run as a player; file console requires exactly one player.");
            var home = Bases.Of(who) ?? throw new CommandException("Stand inside a Flame Altar base.");
            var existing = Policy(home, w);

            if (verb == "list")
            {
                Say($"{home.Id}: stations={home.StationCount}, Magic stock={home.StorageCount}, " +
                    $"water={home.WaterCount}, enabled={existing?.Enabled == true}.");
                foreach (var id in Stations.InBase(home.Id).Take(24))
                {
                    var s = Stations.Read(id);
                    if (s is null) continue;
                    Say($"{id}: {s.Template ?? $"workshop 0x{s.Workshop:x8}"} " +
                        $"state={s.State}, recipe=0x{s.SelectedRecipe:x8}, loaded={s.CraftsLoaded}, " +
                        $"outputs={s.Outputs.Count}, stopped={s.StoppedByPlayer}, " +
                        $"excluded={existing?.Excluded.Contains(Key(s), StringComparer.Ordinal) == true}");
                }
                if (home.StationCount > 24) Say("Only first 24 stations shown.");
                return;
            }

            if (verb == "plan")
            {
                var id = Stations.Nearest(who, null, 12) ?? EntityId.None;
                var s = id.IsNone ? null : Stations.Read(id);
                if (s is null || s.Base != home.Id) throw new CommandException("No production station within 12 metres in this base.");
                var target = c.Count > 1 ? Crafts(c.Args[1]) : existing?.TargetCrafts ?? 3;
                var plan = Stations.Plan(id, new FillOptions { Crafts = target, Mode = FillMode.UpTo, Water = true });
                Say($"{id} {s.Template}: plan target={target}, {plan.Status}, " +
                    $"affordable={plan.CraftsAffordable}, planned={plan.CraftsPlanned}, " +
                    $"short={plan.LimitedBy} {plan.ShortItem}, station-water={plan.WaterHave}, base-water={plan.WaterStock}. " +
                    "READ ONLY: no inventory changes.");
                foreach (var i in plan.Inputs.Take(12))
                    Say($"  item {i.Item}: per craft={i.PerCraft}, station={i.Have}, Magic stock={i.Stock}");
                return;
            }

            if (verb is not ("enable" or "disable" or "target" or "fill" or "collect" or "exclude" or "include"))
                throw new CommandException("Use status|bases|list|plan [n]|enable [n]|disable|target n|fill on|off|collect on|off|exclude|include.");
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
                case "exclude":
                case "include":
                {
                    var id = Stations.Nearest(who, null, 12) ?? EntityId.None;
                    var s = id.IsNone ? null : Stations.Read(id);
                    if (s is null || s.Base != home.Id)
                        throw new CommandException("Stand within 12 metres of a production station.");
                    var key = Key(s);
                    p2.Excluded.RemoveAll(k => string.Equals(k, key, StringComparison.Ordinal));
                    if (verb == "exclude") p2.Excluded.Add(key);
                    break;
                }
            }
            if (!Save()) throw new CommandException("Could not save settings; automation disabled.");
            Say($"Base {home.Id}: enabled={p2.Enabled}, fill={p2.AutoFill}, collect={p2.AutoCollect}, " +
                $"target={p2.TargetCrafts}, excluded={p2.Excluded.Count}. " +
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
        if (_stopping || !_configOk || _scanning || !_settings.Bases.Any(p => p.Enabled)) return;
        _elapsed += Math.Clamp(tick.WallDelta.TotalSeconds, 0, 1);
        if (_elapsed < ScanSeconds) return;
        _elapsed = 0;
        if (!Stations.Available || !Bases.Available || !Stations.Scanned ||
            !Capabilities.Is(Stations.FillCapability) || !Capabilities.Is("items.move")) return;
        if (WorldId() is { } world) _ = Scan(world);
    }

    private async Task Scan(string world)
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            var count = 0;
            foreach (var b in Bases.All)
            {
                if (_stopping || WorldId() != world) return;
                var p = Policy(b, world);
                if (p is not { Enabled: true }) continue;
                foreach (var id in Stations.InBase(b.Id))
                {
                    if (++count > MaxStationsPerScan) return;
                    if (_stopping || WorldId() != world || !p.Enabled) return;
                    await Service(id, b.Id, p, world);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"BSMS: scan interrupted: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            _scanning = false;
        }
    }

    private async Task Service(EntityId id, BaseId @base, BasePolicy p, string world)
    {
        try
        {
            var s = Stations.Read(id);
            if (s is null || s.Base != @base || p.Excluded.Contains(Key(s), StringComparer.Ordinal)) return;
            if (p.AutoCollect && s.Outputs.Count > 0)
            {
                var collect = await Stations.CollectToBase(id);
                Report(id, "collect", collect);
            }
            if (_stopping || WorldId() != world || !p.Enabled || !p.AutoFill) return;
            s = Stations.Read(id); // refresh after async collection
            if (s is null || s.Base != @base || s.SelectedRecipe == 0 ||
                s.StoppedByPlayer || s.StopPending || s.State == StationState.Stopped ||
                s.CraftsLoaded >= p.TargetCrafts) return;

            var options = new FillOptions
            {
                Crafts = p.TargetCrafts,
                Mode = FillMode.UpTo,
                Water = true,
                // Player is intentionally absent: only native base Magic Chest stock.
            };
            var plan = Stations.Plan(id, options);
            if (!plan.Ok || plan.CraftsPlanned <= 0) return;
            var check = Stations.Read(id);
            if (check is null || check.Base != @base || check.SelectedRecipe != s.SelectedRecipe ||
                check.StoppedByPlayer || check.StopPending) return;

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
