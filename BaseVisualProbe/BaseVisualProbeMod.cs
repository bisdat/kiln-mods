// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiln;

namespace Bisdat.BaseVisualProbe;

// Messages must register in identical order on host and joined clients.
// No network request accepts coordinates, material values, or write actions.
public sealed record VisualRequest(byte Operation) : INetMessage<VisualRequest>
{
    public void Encode(NetWriter w) => w.Byte(Operation);
    public static VisualRequest Decode(ref NetReader r) => new(r.Byte());
}

public sealed record VisualProgress(string Phase, string Message, long Read, long Total)
    : INetMessage<VisualProgress>
{
    public void Encode(NetWriter w) => w.String(Phase).String(Message).Int64(Read).Int64(Total);
    public static VisualProgress Decode(ref NetReader r) =>
        new(r.String(40) ?? "", r.String(512) ?? "", r.Int64(), r.Int64());
}

public sealed record VisualCensus(
    long Total, long Buildings, long ZeroPaint, long Growth, long Weather, long Both,
    long OtherPaint, long[] Levels, long[] Materials) : INetMessage<VisualCensus>
{
    public void Encode(NetWriter w)
    {
        w.Int64(Total).Int64(Buildings).Int64(ZeroPaint).Int64(Growth)
            .Int64(Weather).Int64(Both).Int64(OtherPaint);
        for (var i = 0; i < 8; i++) w.Int64(Levels[i]);
        for (var i = 0; i < 128; i++) w.Int64(Materials[i]);
    }

    public static VisualCensus Decode(ref NetReader r)
    {
        var total = r.Int64(); var buildings = r.Int64(); var zero = r.Int64();
        var growth = r.Int64(); var weather = r.Int64();
        var both = r.Int64(); var other = r.Int64();
        var levels = new long[8]; var materials = new long[128];
        for (var i = 0; i < levels.Length; i++) levels[i] = r.Int64();
        for (var i = 0; i < materials.Length; i++) materials[i] = r.Int64();
        return new(total, buildings, zero, growth, weather, both, other, levels, materials);
    }
}

public sealed record VisualDifference(
    long Compared, long Changed, long MaterialChanged,
    long GrowthAdded, long GrowthCleared, long GrowthChanged,
    long WeatherAdded, long WeatherCleared, long TintChanged,
    string[] Samples) : INetMessage<VisualDifference>
{
    public void Encode(NetWriter w)
    {
        w.Int64(Compared).Int64(Changed).Int64(MaterialChanged)
            .Int64(GrowthAdded).Int64(GrowthCleared).Int64(GrowthChanged)
            .Int64(WeatherAdded).Int64(WeatherCleared).Int64(TintChanged)
            .Byte(checked((byte)Samples.Length));
        foreach (var s in Samples) w.String(s);
    }

    public static VisualDifference Decode(ref NetReader r)
    {
        var compared = r.Int64(); var changed = r.Int64(); var material = r.Int64();
        var growthAdded = r.Int64(); var growthCleared = r.Int64(); var growthChanged = r.Int64();
        var weatherAdded = r.Int64(); var weatherCleared = r.Int64(); var tint = r.Int64();
        var n = Math.Min(24, r.Byte());
        var samples = new string[n];
        for (var i = 0; i < n; i++) samples[i] = r.String(256) ?? "";
        return new(compared, changed, material,
            growthAdded, growthCleared, growthChanged,
            weatherAdded, weatherCleared, tint, samples);
    }
}

/// <summary>
/// Experiment only: reads the raw building voxel paint high byte in a
/// whole altar, or compares a 12 m local cube against a RAM snapshot.
/// No world edits, no persistent data writes, no entity removals.
/// </summary>
public sealed class BaseVisualProbeMod : Mod
{
    private const string Channel = "base-visual-probe:read";
    private const string Permission = "base-visual-probe.command.vprobe";
    private NetChannel? _network;
    private KeyBinding? _key;
    private UiWindow? _window;

    private VisualProgress? _statusView;
    private VisualCensus? _censusView;
    private VisualDifference? _diffView;
    private VisualCensus? _lastCensus;
    private VisualDifference? _lastDiff;
    private VisualSnapshot? _baseline;
    private bool _busy;
    private bool _cancel;
    private int _epoch;

    public override void OnLoad()
    {
        _network = Network.Channel(Channel, "1", required: false)
            .Register<VisualRequest>(OnRequest, NetDirection.ToServer)
            .Register<VisualProgress>((m, _) => Receive(m), NetDirection.ToClient)
            .Register<VisualCensus>((m, _) => Receive(m), NetDirection.ToClient)
            .Register<VisualDifference>((m, _) => Receive(m), NetDirection.ToClient);

        if (Client.Available && Client.IsClient && Ui.Available)
        {
            _window = Ui.Window("Base Visual Metadata Probe - READ ONLY", Draw, new UiWindowOptions
            {
                Width = 690, Height = 590, StartOpen = false,
            });
            _key = KeyBindings.Register("base-visual-probe.open", "Ctrl+Shift+F11", () =>
            {
                _window.Show();
                Request(4);
            }, "Open read-only overgrowth / decay probe", "Base Visual Probe");
        }
        Log.Info("base-visual-probe 0.1.0: READ-ONLY metadata census and paired tool experiment; " +
                 "no voxels or props can be changed by this mod.");
    }

    private void Request(byte operation)
    {
        if (!Client.HasLocalPlayer || Client.LocalPlayer.IsNone)
        {
            Hud.Toast("Visual Probe: enter a hosted or joined game.", ToastKind.Warning);
            return;
        }
        var p = Client.LocalPlayer;
        if (Client.IsHosting)
            Context.Scheduler.Post(() => Dispatch(p, operation, true));
        else if (_network?.SendToServer(new VisualRequest(operation)) != true)
            Hud.Toast("Visual Probe: server has no compatible probe channel.", ToastKind.Warning);
    }

    private void OnRequest(VisualRequest message, NetContext context) =>
        Dispatch(context.Player, message.Operation, false);

    private void Dispatch(EntityId player, byte operation, bool localHost)
    {
        if (player.IsNone || (!localHost && !Permissions.Has(player, Permission)))
        {
            Send(player, new VisualProgress("failed", "Admin permission is required for visual probe.", 0, 0));
            return;
        }
        switch (operation)
        {
            case 1: StartCensus(player); break;
            case 2: StartCapture(player); break;
            case 3: StartCompare(player); break;
            case 4:
                Send(player, new VisualProgress("status", _busy ? "Read-only probe in progress." :
                    _baseline is null ? "No before snapshot. Capture beside a wall first." :
                    $"Baseline captured {_baseline.CapturedUtc:HH:mm:ss} UTC; compare the same area after using a game tool.", 0, 0));
                if (_lastCensus is { } v) Send(player, v);
                if (_lastDiff is { } d) Send(player, d);
                break;
            case 5:
                _cancel = true;
                Send(player, new VisualProgress("cancel", "Cancellation requested; no changes were made.", 0, 0));
                break;
            default: Send(player, new VisualProgress("failed", "Unknown read-only command.", 0, 0)); break;
        }
    }

    private static double DistanceSquared(WorldPosition a, WorldPosition b)
    {
        var x = a.X - b.X;
        var y = a.Y - b.Y;
        var z = a.Z - b.Z;
        return x * x + y * y + z * z;
    }

    private bool TryArea(EntityId player, out PlayerBase? altar, out CellBox area, out WorldPosition here,
        out string problem)
    {
        altar = null; area = default; here = default;
        if (!Build.Available || !Capabilities.Is("build.read") || Build.Status is not { Read: true } ||
            Build.Grid is null)
        {
            problem = "Kiln.Build voxel reads are unavailable: " + Capabilities.Why("build.read");
            return false;
        }
        if (!Saves.Available || !Saves.WorldDataReady || Saves.WorldId is null)
        {
            problem = "Wait until the hosted world's identity and save state are ready.";
            return false;
        }
        if (World.Position(player) is not { } pos)
        {
            problem = "Cannot locate the requesting player in the server world.";
            return false;
        }
        here = pos;
        altar = Build.Bases.Where(z => z.Contains(pos))
            .OrderBy(z => DistanceSquared(z.Center, pos))
            .ThenBy(z => z.Altar.Value)
            .FirstOrDefault();
        if (altar is null)
        {
            problem = "Stand inside a Flame Altar's rectangular build zone.";
            return false;
        }
        area = VisualBits.ZoneCells(altar, Build.Grid);
        if (area.IsEmpty)
        {
            problem = "This Flame Altar has no readable voxel volume.";
            return false;
        }
        problem = "";
        return true;
    }

    private bool Ready(EntityId player)
    {
        if (!_busy) return true;
        Send(player, new VisualProgress("busy", "A read-only probe is already running; use Cancel to stop it.", 0, 0));
        return false;
    }

    private bool SameSession(PlayerBase altar, string? world, int epoch) =>
        !_cancel && epoch == _epoch && Saves.WorldId == world &&
        Build.Available && Build.Grid is not null &&
        Build.Bases.Any(z => z.Altar == altar.Altar &&
            z.Center == altar.Center && z.Half == altar.Half);

    private void StartCensus(EntityId player)
    {
        if (!Ready(player)) return;
        if (!TryArea(player, out var altar, out var area, out _, out var reason))
        {
            Send(player, new VisualProgress("failed", reason, 0, 0));
            return;
        }
        _cancel = false; _busy = true; _lastCensus = null;
        _ = CensusAsync(player, altar!, area, Saves.WorldId, _epoch);
    }

    private async Task CensusAsync(EntityId player, PlayerBase altar, CellBox area, string? world, int epoch)
    {
        var inv = new VisualInventory();
        var batches = 0;
        var nextNotice = 10;
        try
        {
            Send(player, new VisualProgress("census", $"Reading every building voxel in altar {altar.Altar.Value}. NO EDITS.",
                0, area.Count));
            foreach (var box in VisualBits.Batches(area))
            {
                if (!SameSession(altar, world, epoch)) throw new OperationCanceledException("world/altar changed or scan cancelled");
                if (!await Build.Load(box, TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException($"unreadable voxel tile at {box}; results withheld");
                if (!SameSession(altar, world, epoch)) throw new OperationCanceledException("scan cancelled");
                inv.Add(Build.Read(box));
                var percent = (int)(100 * inv.Scanned / area.Count);
                if (percent >= nextNotice)
                {
                    Send(player, new VisualProgress("census",
                        $"Altar metadata census {percent}% — buildings {inv.Buildings:N0}, overgrowth {inv.Overgrown:N0}, weather {inv.Weathered:N0}.",
                        inv.Scanned, area.Count));
                    nextNotice = (percent / 10 + 1) * 10;
                }
                if (++batches % 2 == 0) await Context.Scheduler.NextTick();
            }
            if (!SameSession(altar, world, epoch)) throw new OperationCanceledException("scan cancelled");
            var report = new VisualCensus(inv.Scanned, inv.Buildings, inv.ZeroPaint, inv.Overgrown,
                inv.Weathered, inv.Both, inv.OtherPainted,
                (long[])inv.GrowthLevel.Clone(), (long[])inv.ByMaterial.Clone());
            _lastCensus = report;
            Log.Info($"base-visual-probe CENSUS world={world} altar={altar.Altar.Value} " +
                $"read={inv.Scanned}, building={inv.Buildings}, zeroPaint={inv.ZeroPaint}, " +
                $"overgrowth={inv.Overgrown}, weather={inv.Weathered}, both={inv.Both}, tint={inv.OtherPainted}; READ-ONLY");
            for (var i = 0; i < 8; i++)
                Log.Info($"base-visual-probe growthLevel {i}: {inv.GrowthLevel[i]} cells");
            for (var i = 0; i < 128; i++)
                if (inv.ByMaterial[i] != 0)
                    Log.Info($"base-visual-probe building material {i + 128}: {inv.ByMaterial[i]} cells (including zero paint)");
            Send(player, report);
            Send(player, new VisualProgress("complete", "Full altar visual-metadata census complete. Zero voxels edited.",
                inv.Scanned, area.Count));
        }
        catch (Exception ex)
        {
            Log.Warn("base-visual-probe census stopped (no edits): " + ex);
            if (epoch == _epoch)
                Send(player, new VisualProgress(_cancel ? "cancelled" : "failed", ex.Message,
                    inv.Scanned, area.Count));
        }
        finally { if (epoch == _epoch) _busy = false; }
    }

    private void StartCapture(EntityId player)
    {
        if (!Ready(player)) return;
        if (!TryArea(player, out var altar, out var area, out var here, out var reason))
        {
            Send(player, new VisualProgress("failed", reason, 0, 0));
            return;
        }
        // A bounded local cube is much more useful for identifying exactly
        // what the game's tool changed than comparing 32 million cells.
        var sample = VisualBits.Intersect(area, Build.BoxAround(here, 6, 6, 6));
        if (sample.IsEmpty || sample.Count > 30_000)
        {
            Send(player, new VisualProgress("failed", "Sample region outside altar or too large; move closer to the target.", 0, 0));
            return;
        }
        _cancel = false; _busy = true;
        _baseline = null; _lastDiff = null;
        _ = CaptureAsync(player, altar!, new VisualSnapshot(Saves.WorldId!, altar!, sample), _epoch);
    }

    private async Task CaptureAsync(EntityId player, PlayerBase altar, VisualSnapshot snap, int epoch)
    {
        var inv = new VisualInventory();
        var batches = 0;
        try
        {
            Send(player, new VisualProgress("capture",
                $"Capturing fixed ~12 m cube, {snap.Box.Count:N0} voxels. Only read operations.", 0, snap.Box.Count));
            foreach (var box in VisualBits.Batches(snap.Box))
            {
                if (!SameSession(altar, snap.WorldId, epoch)) throw new OperationCanceledException("capture cancelled or world changed");
                if (!await Build.Load(box, TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("sample voxel tiles unavailable; no baseline retained");
                if (!SameSession(altar, snap.WorldId, epoch)) throw new OperationCanceledException("capture cancelled");
                var values = Build.Read(box);
                inv.Add(values);
                foreach (var (cell, value) in VisualBits.Enumerate(box, values))
                    snap.Cells.Add(cell, value);
                if (++batches % 2 == 0) await Context.Scheduler.NextTick();
            }
            if (!SameSession(altar, snap.WorldId, epoch)) throw new OperationCanceledException("capture cancelled");
            if (snap.Cells.Count != snap.Box.Count) throw new InvalidOperationException("incomplete snapshot");
            snap.CapturedUtc = DateTime.UtcNow;
            _baseline = snap;
            Log.Info($"base-visual-probe BASELINE world={snap.WorldId} altar={altar.Altar.Value} " +
                $"box={snap.Box} cells={snap.Cells.Count} buildings={inv.Buildings}, " +
                $"zeroPaint={inv.ZeroPaint}, growth={inv.Overgrown}, weather={inv.Weathered}; READ-ONLY");
            Send(player, new VisualProgress("captured",
                $"Baseline captured: {snap.Cells.Count:N0} cells ({inv.Buildings:N0} building, " +
                $"{inv.Overgrown:N0} overgrowth, {inv.Weathered:N0} weather). " +
                "Use an Overgrowth/Decay Tool on a nearby wall/roof, then Compare.",
                snap.Cells.Count, snap.Box.Count));
        }
        catch (Exception ex)
        {
            Log.Warn("base-visual-probe baseline failed (read-only): " + ex);
            if (epoch == _epoch)
                Send(player, new VisualProgress(_cancel ? "cancelled" : "failed",
                    "No usable baseline: " + ex.Message, snap.Cells.Count, snap.Box.Count));
        }
        finally { if (epoch == _epoch) _busy = false; }
    }

    private void StartCompare(EntityId player)
    {
        if (!Ready(player)) return;
        if (_baseline is not { } snap)
        {
            Send(player, new VisualProgress("failed", "Capture baseline first; it remains fixed until recaptured.", 0, 0));
            return;
        }
        if (!TryArea(player, out var altar, out _, out _, out var reason) ||
            altar is null || altar.Altar != snap.Altar.Altar ||
            altar.Center != snap.Altar.Center || altar.Half != snap.Altar.Half ||
            Saves.WorldId != snap.WorldId)
        {
            Send(player, new VisualProgress("failed", "Return to the same Flame Altar and world. " +
                (reason.Length > 0 ? reason : "The altar differs from the baseline."), 0, 0));
            return;
        }
        _cancel = false; _busy = true;
        _ = CompareAsync(player, snap, _epoch);
    }

    private async Task CompareAsync(EntityId player, VisualSnapshot snap, int epoch)
    {
        var diffs = new VisualChangeCounts();
        var batches = 0;
        try
        {
            Send(player, new VisualProgress("compare",
                "Comparing fixed cube with baseline — no writes. Exact raw material and paint bytes are logged.",
                0, snap.Box.Count));
            foreach (var box in VisualBits.Batches(snap.Box))
            {
                if (!SameSession(snap.Altar, snap.WorldId, epoch))
                    throw new OperationCanceledException("comparison cancelled or world changed");
                if (!await Build.Load(box, TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("sample voxel tile unreadable; no partial comparison reported");
                if (!SameSession(snap.Altar, snap.WorldId, epoch)) throw new OperationCanceledException("comparison cancelled");
                var values = Build.Read(box);
                foreach (var (cell, value) in VisualBits.Enumerate(box, values))
                {
                    if (!snap.Cells.TryGetValue(cell, out var previous))
                        throw new InvalidOperationException("baseline missing voxel " + cell);
                    diffs.Compare(cell, previous, value);
                }
                if (++batches % 2 == 0) await Context.Scheduler.NextTick();
            }
            if (diffs.Compared != snap.Box.Count) throw new InvalidOperationException("sample coverage mismatch");
            var result = new VisualDifference(diffs.Compared, diffs.Changed, diffs.MaterialChanged,
                diffs.GrowthAdded, diffs.GrowthCleared, diffs.GrowthChanged,
                diffs.WeatherAdded, diffs.WeatherCleared, diffs.TintChanged, diffs.Samples.ToArray());
            _lastDiff = result;
            Log.Info($"base-visual-probe DIFF world={snap.WorldId} altar={snap.Altar.Altar.Value} " +
                $"baseline={snap.CapturedUtc:O} compared={diffs.Compared} changed={diffs.Changed} " +
                $"materialOrOccupancyChanged={diffs.MaterialChanged} growthAdded={diffs.GrowthAdded} " +
                $"growthCleared={diffs.GrowthCleared} growthLevelChanged={diffs.GrowthChanged} " +
                $"weatherAdded={diffs.WeatherAdded} weatherCleared={diffs.WeatherCleared} " +
                $"otherPaintChanged={diffs.TintChanged}; NO EDITS");
            foreach (var sample in diffs.Samples)
                Log.Info("base-visual-probe DIFF-SAMPLE " + sample);
            Send(player, result);
            Send(player, new VisualProgress("compared",
                $"{diffs.Changed:N0} changed cells ({diffs.GrowthCleared:N0} growth cleared, " +
                $"{diffs.WeatherCleared:N0} weather cleared, {diffs.MaterialChanged:N0} material/occupancy changes). " +
                "Log contains exact 16-bit examples. Baseline retained.",
                diffs.Compared, snap.Box.Count));
        }
        catch (Exception ex)
        {
            Log.Warn("base-visual-probe comparison stopped (no edits): " + ex);
            if (epoch == _epoch)
                Send(player, new VisualProgress(_cancel ? "cancelled" : "failed", ex.Message,
                    diffs.Compared, snap.Box.Count));
        }
        finally { if (epoch == _epoch) _busy = false; }
    }

    private void Send(EntityId player, VisualProgress p)
    {
        Log.Info($"base-visual-probe {p.Phase}: {p.Message}");
        if (Client.IsHosting && Client.HasLocalPlayer && player == Client.LocalPlayer)
            Client.Post(() => Receive(p));
        else
            _network?.SendTo(player, p);
    }

    private void Send(EntityId player, VisualCensus p)
    {
        if (Client.IsHosting && Client.HasLocalPlayer && player == Client.LocalPlayer)
            Client.Post(() => Receive(p));
        else _network?.SendTo(player, p);
    }

    private void Send(EntityId player, VisualDifference p)
    {
        if (Client.IsHosting && Client.HasLocalPlayer && player == Client.LocalPlayer)
            Client.Post(() => Receive(p));
        else _network?.SendTo(player, p);
    }

    private void Receive(VisualProgress p)
    {
        Volatile.Write(ref _statusView, p);
        if (p.Phase is "complete" or "compared" or "captured" or "failed")
            Hud.Toast(p.Message, p.Phase == "failed" ? ToastKind.Warning : ToastKind.Success);
    }

    private void Receive(VisualCensus p) => Volatile.Write(ref _censusView, p);
    private void Receive(VisualDifference p) => Volatile.Write(ref _diffView, p);

    private void Draw(UiDraw ui)
    {
        ui.TextWrapped("EXPERIMENTAL, READ ONLY. The game defines weathered (bit 7) and overgrowth (bits 4-6) " +
            "in the high byte of a BUILDING voxel. This probe does not change any world voxel.");
        ui.Separator();
        if (ui.Button("1. Census full Flame Altar")) Client.Post(() => Request(1));
        ui.SameLine();
        if (ui.Button("Cancel operation")) Client.Post(() => Request(5));
        if (ui.Button("2. Capture nearby ~12 m cube")) Client.Post(() => Request(2));
        ui.SameLine();
        if (ui.Button("3. Compare same cube")) Client.Post(() => Request(3));
        ui.TextDisabled("Capture beside a wall or roof; use the vanilla Overgrowth or Decay Tool; then Compare.");
        var status = Volatile.Read(ref _statusView);
        if (status is not null)
        {
            ui.TextWrapped($"[{status.Phase}] {status.Message}");
            if (status.Total > 0)
                ui.Progress((float)(status.Read / (double)status.Total),
                    $"{status.Read:N0} / {status.Total:N0}");
        }
        if (Volatile.Read(ref _censusView) is { } census)
        {
            ui.Separator("Entire Flame Altar - building appearance");
            ui.Text($"Read: {census.Total:N0}; building voxels: {census.Buildings:N0}; zero paint: {census.ZeroPaint:N0}");
            ui.Text($"Overgrowth flags: {census.Growth:N0}; weathering flags: {census.Weather:N0}; both: {census.Both:N0}");
            ui.Text($"Other paint/tint: {census.OtherPaint:N0} (these bits are NOT cleanup targets)");
            ui.Text("Overgrowth bit levels: " + string.Join(", ",
                Enumerable.Range(0, 8).Select(i => $"{i}={census.Levels[i]:N0}")));
        }
        if (Volatile.Read(ref _diffView) is { } d)
        {
            ui.Separator("Paired local comparison against fixed baseline");
            ui.Text($"Compared: {d.Compared:N0}; changed: {d.Changed:N0}; material/occupancy: {d.MaterialChanged:N0}");
            ui.Text($"Overgrowth: +{d.GrowthAdded:N0}, cleared {d.GrowthCleared:N0}, level changed {d.GrowthChanged:N0}");
            ui.Text($"Weathering: +{d.WeatherAdded:N0}, cleared {d.WeatherCleared:N0}");
            ui.Text($"Other paint/tint changes: {d.TintChanged:N0}");
            if (ui.BeginChild("visual-diff-samples", 0, 135 * ui.Scale))
                foreach (var line in d.Samples) ui.TextWrapped(line);
            ui.EndChild();
        }
        ui.TextDisabled("Static vegetation entities and missing blocks are outside this voxel-metadata experiment.");
        ui.TextDisabled("For complete evidence upload Kiln's support report after Capture / Tool / Compare.");
    }

    [Command("vprobe census", Description = "Read-only full-altar building paint flags census", Permission = CommandPermission.Admin)]
    private void CensusCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in an active world");
        StartCensus(c.Source.Player);
        c.Reply("Read-only full altar census started. Progress and material counts are logged.");
    }

    [Command("vprobe capture", Description = "Capture ~12 m voxel cube beside a wall or roof", Permission = CommandPermission.Admin)]
    private void CaptureCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in an active world");
        StartCapture(c.Source.Player);
        c.Reply("Read-only baseline capture requested.");
    }

    [Command("vprobe compare", Description = "Compare a fixed baseline after using a game tool", Permission = CommandPermission.Admin)]
    private void CompareCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in an active world");
        StartCompare(c.Source.Player);
        c.Reply("Read-only comparison requested; inspect kiln.log for exact changes.");
    }

    [Command("vprobe cancel", Description = "Stop the read-only census/compare", Permission = CommandPermission.Admin)]
    private void CancelCommand(CommandContext c)
    {
        _cancel = true;
        c.Reply("Cancel requested. No voxel edits by this mod.");
    }

    [Command("vprobe report", Description = "Show latest full-altar building flag summary", Permission = CommandPermission.Admin)]
    private void ReportCommand(CommandContext c)
    {
        if (_lastCensus is { } v)
        {
            c.Reply($"Read {v.Total:N0}; building {v.Buildings:N0} (including {v.ZeroPaint:N0} zero-paint); " +
                $"overgrowth {v.Growth:N0}; weathering {v.Weather:N0}; both {v.Both:N0}.");
            c.Reply("Growth level 0..7: " + string.Join(", ",
                Enumerable.Range(0, 8).Select(i => $"{i}={v.Levels[i]:N0}")));
        }
        else c.Reply("No complete full-altar census recorded.");
        if (_lastDiff is { } d)
            c.Reply($"Fixed-area comparison: changed {d.Changed:N0}/{d.Compared:N0}; " +
                $"growth cleared {d.GrowthCleared:N0}; weather cleared {d.WeatherCleared:N0}; " +
                $"material/occupancy changed {d.MaterialChanged:N0}. Exact examples in kiln.log.");
    }

    [Subscribe]
    private void WorldOpened(WorldLoadedEvent e) => Reset();
    [Subscribe]
    private void WorldStopping(ServerStoppingEvent e) => Reset();

    private void Reset()
    {
        _epoch++;
        _cancel = true;
        _busy = false;
        _baseline = null;
        _lastDiff = null;
        _lastCensus = null;
        if (Client.Available && Client.IsClient)
            Client.Post(() => { _statusView = null; _censusView = null; _diffView = null; });
    }

    public override void OnUnload()
    {
        Reset();
        _key?.Dispose();
        _window?.Dispose();
        _network?.Dispose();
    }
}
