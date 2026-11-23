// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiln;

namespace Bisdat.BaseCleanup;

// Register these message types in identical order on client and server.
public sealed record ScanRequest(byte Operation) : INetMessage<ScanRequest>
{
    public void Encode(NetWriter w) => w.Byte(Operation);
    public static ScanRequest Decode(ref NetReader r) => new(r.Byte());
}

public sealed record ScanProgress(string Message, long Visited, long Total, byte State) : INetMessage<ScanProgress>
{
    // 0 idle, 1 scanning, 2 complete, 3 partial, 4 cancelled, 5 failed.
    public void Encode(NetWriter w) => w.String(Message).Int64(Visited).Int64(Total).Byte(State);
    public static ScanProgress Decode(ref NetReader r) =>
        new(r.String(512) ?? "", r.Int64(), r.Int64(), r.Byte());
}

public sealed record ScanReport(string Area, long TotalCells, long ReadCells, long UnreadableCells,
    long AirCells, long PartialCells, long[] Counts, long[] FillSums, double CellSize) : INetMessage<ScanReport>
{
    public void Encode(NetWriter w)
    {
        w.String(Area).Int64(TotalCells).Int64(ReadCells).Int64(UnreadableCells)
            .Int64(AirCells).Int64(PartialCells).Int64(BitConverter.DoubleToInt64Bits(CellSize));
        for (var i = 0; i < 256; i++) w.Int64(Counts[i]);
        for (var i = 0; i < 256; i++) w.Int64(FillSums[i]);
    }

    public static ScanReport Decode(ref NetReader r)
    {
        var area = r.String(256) ?? "";
        var total = r.Int64();
        var read = r.Int64();
        var missing = r.Int64();
        var air = r.Int64();
        var partial = r.Int64();
        var cellSize = BitConverter.Int64BitsToDouble(r.Int64());
        var counts = new long[256];
        var fills = new long[256];
        for (var i = 0; i < counts.Length; i++) counts[i] = r.Int64();
        for (var i = 0; i < fills.Length; i++) fills[i] = r.Int64();
        return new ScanReport(area, total, read, missing, air, partial, counts, fills, cellSize);
    }

    public long Sum(params byte[] materials)
    {
        long result = 0;
        foreach (var material in materials) result += Counts[material];
        return result;
    }

    public long Rubble => Sum(BaseScanCore.PlaceableRubble) + Sum(BaseScanCore.WorldRubble);
    public long Mycelium => Sum(BaseScanCore.MineableMycelium);
    public long Mud => Counts[BaseScanCore.MudTerrain];
    public long Buildings => Counts.Skip(128).Sum();
    public long Terrain => Counts.Skip(1).Take(127).Sum();
    public double CubicMetres(int material) => FillSums[material] / 255.0 * CellSize * CellSize * CellSize;
}

/// <summary>
/// Full-volume read-only survey. No Build.Write, Build.Dig, Build.BreakBox,
/// placing/removing props, native hooks or world mutations.
/// </summary>
public sealed partial class BaseCleanupMod : Mod
{
    private const string Binding = "base-cleanup.preview";
    private const string Channel = "base-cleanup:survey";
    private const string PermissionNode = "base-cleanup.command.basescan";
    private NetChannel? _net;
    private KeyBinding? _key;
    private UiWindow? _window;

    // Immutable references for the render callback: no mutable scanner state
    // or server-only API is accessed while the window is being drawn.
    private ScanProgress? _viewProgress;
    private ScanReport? _viewReport;
    private ScanJob? _running;
    private ScanReport? _latest;
    private string? _lastSurveyWorldId;
    private PlayerBase? _lastSurveyAltar;
    private int _uiStage; // 0 scan, 1 options, 2 plan, 3 remove, 4 working, 5 undo
    private int _generation;

    private sealed class ScanJob(EntityId player, PlayerBase zone, CellBox bounds, double cellSize, int generation)
    {
        internal readonly EntityId Player = player;
        internal readonly PlayerBase Zone = zone;
        internal readonly CellBox Bounds = bounds;
        internal readonly double CellSize = cellSize;
        internal readonly int Generation = generation;
        internal readonly VoxelInventory Inventory = new();
        internal long Visited;
        internal long Unreadable;
        internal bool CancelRequested;
    }

    public override void OnLoad()
    {
        _net = Network.Channel(Channel, "3", required: false)
            .Register<ScanRequest>(OnRequest, NetDirection.ToServer)
            .Register<ScanProgress>((m, _) => ReceiveProgress(m), NetDirection.ToClient)
            .Register<ScanReport>((m, _) => ReceiveReport(m), NetDirection.ToClient)
            .Register<CleanupRequest>(OnCleanupRequest, NetDirection.ToServer)
            .Register<CleanupStatus>((m, _) => CleanupReceive(m), NetDirection.ToClient);

        if (Client.Available && Client.IsClient && Ui.Available)
        {
            _window = Ui.Window("Base Cleanup - Survey, Removal and Cosmetic Repair", Draw, new UiWindowOptions
            {
                Width = 680, Height = 710, StartOpen = false,
            });
            _key = KeyBindings.Register(Binding, "Shift+ScrollLock", () =>
            {
                _window.Show();
                Request(3); // Opening the panel never removes blocks.
                CleanupRequestLocal(5);
            }, "Open Base Cleanup preview", "Base Cleanup");
        }
        Log.Info("Base Cleanup v0.3.5 TEST loaded: Stage 1 survey; Stage 2 backup-first terrain cleanup plus opt-in weathering repair, persistent undo.");
    }

    private void Request(byte op)
    {
        if (!Client.HasLocalPlayer || Client.LocalPlayer.IsNone)
        {
            Hud.Toast("Base Cleanup: enter a world first.", ToastKind.Warning);
            return;
        }
        var local = Client.LocalPlayer;
        if (Client.IsHosting)
        {
            // A hosted offline world need not have a loopback network channel.
            // Move from the client UI thread onto the authoritative server tick.
            Context.Scheduler.Post(() => Act(local, op, true));
        }
        else if (_net?.SendToServer(new ScanRequest(op)) != true)
        {
            Hud.Toast("Base Cleanup: server has no survey channel.", ToastKind.Warning);
        }
    }

    private void OnRequest(ScanRequest request, NetContext context) =>
        Act(context.Player, request.Operation, false);

    private void Act(EntityId player, byte operation, bool localHost)
    {
        if (player.IsNone || (!localHost && !Permissions.Has(player, PermissionNode)))
        {
            Publish(player, new ScanProgress("Base Cleanup: admin permission required.", 0, 0, 5));
            return;
        }
        switch (operation)
        {
            case 1: Start(player); break;
            case 2: Cancel(player); break;
            case 3:
                Publish(player, CurrentStatus());
                if (_latest is { } r) Publish(player, r);
                break;
            default:
                Publish(player, new ScanProgress("Base Cleanup: invalid request.", 0, 0, 5));
                break;
        }
    }

    private void Start(EntityId player)
    {
        if (_cleanupBusy)
        {
            Publish(player, new ScanProgress("Finish the current cleanup or Undo first.", 0, 0, 5));
            return;
        }
        if (_running is { } old)
        {
            Publish(player, new ScanProgress("A survey is already running. Open Preview to see its progress.",
                old.Visited, old.Bounds.Count, 1));
            return;
        }
        if (!Build.Available || !Capabilities.Is("build.read") || Build.Grid is not { } grid)
        {
            Publish(player, new ScanProgress("Voxel reads unavailable: " + Capabilities.Why("build.read"), 0, 0, 5));
            return;
        }
        if (World.Position(player) is not { } at)
        {
            Publish(player, new ScanProgress("Cannot locate your character in the server world.", 0, 0, 5));
            return;
        }
        // Overlapping altar zones are distinct. Choose the nearest zone that
        // contains the player, not an invented spherical build radius.
        var zone = Build.Bases.Where(b => b.Contains(at))
            .OrderBy(b => SquaredDistance(at, b.Center))
            .ThenBy(b => b.Altar.Value)
            .FirstOrDefault();
        if (zone is null)
        {
            Publish(player, new ScanProgress("Stand inside an active Flame Altar's build zone.", 0, 0, 5));
            return;
        }
        var bounds = BaseScanCore.ZoneCells(zone, grid);
        if (bounds.IsEmpty)
        {
            Publish(player, new ScanProgress("The altar zone does not intersect Kiln's readable voxel grid.", 0, 0, 5));
            return;
        }
        var job = new ScanJob(player, zone, bounds, grid.CellSize, _generation);
        _running = job;
        _latest = null;
        _lastSurveyWorldId = null;
        _lastSurveyAltar = null;
        _preparedCleanup = null;
        Publish(player, new ScanProgress($"Scanning altar {zone.Altar.Value}, all {bounds.Count:N0} voxels (read-only).",
            0, bounds.Count, 1));
        _ = RunScan(job); // All exceptions are caught/reported by RunScan.
    }

    private static double SquaredDistance(WorldPosition a, WorldPosition b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private async Task RunScan(ScanJob job)
    {
        var nextReportPercent = 10;
        var processedBatches = 0;
        try
        {
            foreach (var box in BaseScanCore.Batches(job.Bounds))
            {
                if (job.CancelRequested || job.Generation != _generation || !ReferenceEquals(_running, job))
                {
                    Publish(job.Player, new ScanProgress("Survey cancelled. No world blocks were altered.",
                        job.Visited, job.Bounds.Count, 4));
                    return;
                }
                if (!Build.Available || Build.Grid is null || !Build.Bases.Any(b => b.Altar == job.Zone.Altar &&
                    b.Center == job.Zone.Center && b.Half == job.Zone.Half))
                    throw new InvalidOperationException("the altar zone or voxel grid changed while scanning");

                // Stream tile data. A tile that cannot be read is UNREADABLE,
                // not silently counted as empty air.
                var loaded = await Build.Load(box, TimeSpan.FromSeconds(10));
                if (job.Generation != _generation || !ReferenceEquals(_running, job)) return;
                if (job.CancelRequested)
                {
                    Publish(job.Player, new ScanProgress("Survey cancelled. No world blocks were altered.",
                        job.Visited, job.Bounds.Count, 4));
                    return;
                }

                if (loaded)
                    job.Inventory.Add(Build.Read(box));
                else
                    job.Unreadable += box.Count;
                job.Visited += box.Count;

                var percent = (int)(100 * job.Visited / job.Bounds.Count);
                if (percent >= nextReportPercent)
                {
                    Publish(job.Player, new ScanProgress($"Survey {percent}% - no edits performed.",
                        job.Visited, job.Bounds.Count, 1));
                    nextReportPercent = (percent / 10 + 1) * 10;
                }
                // At most two bounded reads per server step plus tile streaming.
                if (++processedBatches % 2 == 0)
                    await Context.Scheduler.NextTick();
            }
            if (job.CancelRequested || job.Generation != _generation) return;
            var label = $"Altar {job.Zone.Altar.Value}; centre ({job.Zone.Center.X:F1}, " +
                        $"{job.Zone.Center.Y:F1}, {job.Zone.Center.Z:F1}); " +
                        $"zone {2 * job.Zone.Half.X:F1} x {2 * job.Zone.Half.Y:F1} x {2 * job.Zone.Half.Z:F1} m";
            var v = job.Inventory;
            var report = new ScanReport(label, job.Bounds.Count, v.ReadCells, job.Unreadable,
                v.AirCells, v.PartialCells, (long[])v.Count.Clone(), (long[])v.FillSum.Clone(), job.CellSize);
            _latest = report;
            if (job.Unreadable == 0)
            {
                _lastSurveyWorldId = Saves.WorldId;
                _lastSurveyAltar = job.Zone;
            }
            Log.Info($"Base Cleanup Stage 1 report: {label}, read={v.ReadCells}, unreadable={job.Unreadable}, " +
                     $"rubble={report.Rubble}, mycelium={report.Mycelium}, mud(37)={report.Mud}, " +
                     $"buildings={report.Buildings}; no terrain was changed.");
            for (var i = 0; i < 256; i++)
                if (report.Counts[i] != 0)
                    Log.Info($"Base Cleanup material {i}: {report.Counts[i]} filled cells, {report.CubicMetres(i):F2} m3");
            Publish(job.Player, report);
            Publish(job.Player, new ScanProgress(job.Unreadable == 0
                ? "Survey complete. No blocks were changed."
                : $"Partial survey: {job.Unreadable:N0} unreadable voxels. No blocks were changed.",
                job.Visited, job.Bounds.Count, job.Unreadable == 0 ? (byte)2 : (byte)3));
        }
        catch (Exception ex)
        {
            if (job.Generation == _generation && ReferenceEquals(_running, job))
            {
                Log.Warn($"Base Cleanup Stage 1 scan failed: {ex}");
                Publish(job.Player, new ScanProgress("Survey failed: " + ex.Message, job.Visited, job.Bounds.Count, 5));
            }
        }
        finally
        {
            if (ReferenceEquals(_running, job)) _running = null;
        }
    }

    private void Cancel(EntityId player)
    {
        if (_running is not { } scan)
        {
            Publish(player, CurrentStatus());
            return;
        }
        scan.CancelRequested = true;
        Publish(player, new ScanProgress("Cancelling survey after the current read. No changes to revert.",
            scan.Visited, scan.Bounds.Count, 1));
    }

    private ScanProgress CurrentStatus() => _running is { } j
        ? new ScanProgress("Read-only survey running", j.Visited, j.Bounds.Count, 1)
        : _latest is { } r
            ? new ScanProgress(r.UnreadableCells == 0 ? "Previous survey complete." : "Previous survey partial.",
                r.TotalCells, r.TotalCells, r.UnreadableCells == 0 ? (byte)2 : (byte)3)
            : new ScanProgress("No survey recorded for this world. Open Preview and select Scan.", 0, 0, 0);

    private void Publish(EntityId recipient, ScanProgress progress)
    {
        if (Client.IsHosting && Client.HasLocalPlayer && recipient == Client.LocalPlayer)
            Client.Post(() => ReceiveProgress(progress));
        else
            _net?.SendTo(recipient, progress);
    }

    private void Publish(EntityId recipient, ScanReport report)
    {
        if (Client.IsHosting && Client.HasLocalPlayer && recipient == Client.LocalPlayer)
            Client.Post(() => ReceiveReport(report));
        else
            _net?.SendTo(recipient, report);
    }

    private void ReceiveProgress(ScanProgress p)
    {
        Volatile.Write(ref _viewProgress, p);
        // The client displays ONLY the controls for the current step.
        // Partial or failed surveys never unlock Prepare.
        if (p.State == 2)
            Volatile.Write(ref _uiStage, 1); // options / prepare
        else if (p.State is 0 or 1 or 3 or 4 or 5)
            Volatile.Write(ref _uiStage, 0); // scan
        if (p.State is 2 or 3 or 4 or 5)
            Hud.Toast(p.Message, p.State is 2 ? ToastKind.Success : ToastKind.Warning);
    }

    private void ReceiveReport(ScanReport report)
    {
        Volatile.Write(ref _viewReport, report);
        _window?.Show();
    }

    private void Draw(UiDraw ui)
    {
        var stage = Volatile.Read(ref _uiStage);
        var progress = Volatile.Read(ref _viewProgress);
        var report = Volatile.Read(ref _viewReport);
        var cleanup = Volatile.Read(ref _viewCleanup);

        if (stage == 0)
        {
            ui.Separator("1 / 5 - Scan (read-only)");
            ui.TextWrapped("Start with a complete survey of the current Flame Altar. " +
                "This step does not modify the world and can be repeated.");
            if (ui.Button("1. Scan current Flame Altar"))
            {
                Volatile.Write(ref _viewReport, null);
                Client.Post(() => Request(1));
            }
            ui.SameLine();
            if (ui.Button("Cancel scan")) Client.Post(() => Request(2));
            if (progress is { } scan)
            {
                ui.TextWrapped(scan.Message);
                if (scan.Total > 0)
                    ui.Progress((float)(scan.Visited / (double)scan.Total),
                        $"{scan.Visited:N0} / {scan.Total:N0} voxels");
            }
            // A prior cleanup can remain recoverable across restarts, even
            // before the new survey. Only offer its own explicitly labelled
            // recovery, never an unprepared REMOVE button.
            if (cleanup is { Backup.Length: > 0 } &&
                cleanup.Phase is "complete" or "partial")
            {
                ui.Separator("Previous operation still available");
                ui.TextWrapped(cleanup.Message);
                if (ui.Button("Review previous operation's Undo"))
                    Volatile.Write(ref _uiStage, 5);
            }
            return;
        }

        if (stage == 1)
        {
            ui.Separator("2 / 5 - Choose cleanup options");
            if (report is null || report.UnreadableCells != 0)
            {
                ui.TextWrapped("A complete Stage 1 scan is required. Scan again.");
                if (ui.Button("Return to Scan")) Volatile.Write(ref _uiStage, 0);
                return;
            }
            ui.TextWrapped(report.Area);
            ui.Text($"Survey: {report.ReadCells:N0}/{report.TotalCells:N0} cells; unreadable {report.UnreadableCells:N0}");
            ui.Text($"Rubble: {report.Rubble:N0}; mud material 37: {report.Mud:N0}; mycelium: {report.Mycelium:N0}");
            ui.TextDisabled("Survey counts are current at scan time; Prepare will re-read and check every selected voxel.");
            DrawCleanup(ui);
            ui.Separator("Survey material inventory (optional detail)");
            if (ui.BeginChild("base-cleanup-materials", 0, 170 * ui.Scale))
            {
                if (ui.BeginTable("materials", "ID", "Voxels", "Volume m3"))
                {
                    for (var i = 0; i < 256; i++)
                        if (report.Counts[i] > 0)
                            ui.Row(i.ToString(), report.Counts[i].ToString("N0"), report.CubicMetres(i).ToString("F2"));
                    ui.EndTable();
                }
            }
            ui.EndChild();
            return;
        }

        // Planning, confirmation, active writes, and per-operation Undo are
        // sequential, mutually exclusive panels.
        DrawCleanup(ui);
    }

    [Command("basescan", Description = "Start a read-only survey of the altar you stand in", Permission = CommandPermission.Admin)]
    private void Scan(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run this as an in-world player/host");
        Start(c.Source.Player);
        c.Reply(CurrentStatus().Message);
    }

    [Command("basescan status", Description = "Show current survey progress", Permission = CommandPermission.Admin)]
    private void Status(CommandContext c)
    {
        var p = CurrentStatus();
        c.Reply($"{p.Message} ({p.Visited:N0}/{p.Total:N0})");
    }

    [Command("basescan cancel", Description = "Cancel the read-only survey", Permission = CommandPermission.Admin)]
    private void CancelCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run this as an in-world player/host");
        Cancel(c.Source.Player);
        c.Reply("Cancel requested; no world edits were made.");
    }

    [Command("basescan report", Description = "Show the last survey summary", Permission = CommandPermission.Admin)]
    private void ReportCommand(CommandContext c)
    {
        if (_latest is not { } r) { c.Reply("No completed/partial survey available."); return; }
        c.Reply(r.Area);
        c.Reply($"Read {r.ReadCells:N0}/{r.TotalCells:N0}, unreadable {r.UnreadableCells:N0}; air {r.AirCells:N0}; partial fills {r.PartialCells:N0}");
        c.Reply($"Rubble {r.Rubble:N0}; mycelium {r.Mycelium:N0}; test creek mud (37) {r.Mud:N0}; building voxels {r.Buildings:N0}");
        c.Reply("Overgrowth and cosmetic decay: not counted by Stage 1. Stage 2 has opt-in decay planning.");
        c.Reply("Use /basescan materials [page] for the exact material histogram.");
    }

    [Command("basescan materials", Description = "Page through exact material IDs and their filled-cell counts", Permission = CommandPermission.Admin)]
    private void MaterialsCommand(CommandContext c, int page = 1)
    {
        if (_latest is not { } r) { c.Reply("No survey available."); return; }
        var present = Enumerable.Range(0, 256).Where(i => r.Counts[i] > 0).ToArray();
        const int pageSize = 12;
        var pages = Math.Max(1, (present.Length + pageSize - 1) / pageSize);
        if (page < 1 || page > pages) throw new CommandException($"page must be 1..{pages}");
        c.Reply($"Material histogram page {page}/{pages} (voxel volume is fill-weighted):");
        foreach (var i in present.Skip((page - 1) * pageSize).Take(pageSize))
            c.Reply($"material {i}: {r.Counts[i]:N0} voxels, {r.CubicMetres(i):F2} m3");
    }

    [Subscribe]
    private void Stopping(ServerStoppingEvent e)
    {
        _generation++;
        if (_running is { } j) j.CancelRequested = true;
        _running = null;
        _latest = null;
        _lastSurveyWorldId = null;
        _lastSurveyAltar = null;
        CleanupStopped();
    }

    public override void OnUnload()
    {
        CleanupStopped();
        _generation++;
        if (_running is { } j) j.CancelRequested = true;
        _running = null;
        _key?.Dispose();
        _window?.Dispose();
        _net?.Dispose();
    }
}
