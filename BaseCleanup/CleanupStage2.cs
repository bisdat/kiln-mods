// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kiln;

namespace Bisdat.BaseCleanup;

public sealed record CleanupRequest(byte Operation, byte Options) : INetMessage<CleanupRequest>
{
    // 1 prepare, 2 confirm, 3 undo, 4 cancel, 5 status.
    public void Encode(NetWriter w) => w.Byte(Operation).Byte(Options);
    public static CleanupRequest Decode(ref NetReader r) => new(r.Byte(), r.Byte());
}

public sealed record CleanupStatus(string Phase, string Message, long Done, long Total,
    long Rubble, long Mud, long Mycelium, string Backup) : INetMessage<CleanupStatus>
{
    // Separate count for opt-in decay repair. Old status fields stay intact.
    public long Decay { get; init; }

    public void Encode(NetWriter w) => w.String(Phase).String(Message).Int64(Done).Int64(Total)
        .Int64(Rubble).Int64(Mud).Int64(Mycelium).String(Backup).Int64(Decay);
    public static CleanupStatus Decode(ref NetReader r)
    {
        var status = new CleanupStatus(r.String(32) ?? "", r.String(512) ?? "",
            r.Int64(), r.Int64(), r.Int64(), r.Int64(), r.Int64(), r.String(160) ?? "");
        return status with { Decay = r.Int64() };
    }
}

/// <summary>
/// Stage 2 runs only on the authoritative server. Original voxel values
/// and the operation index are staged in the current world's Kiln WorldData
/// before a verified, timestamped Kiln whole-world backup. Only then are
/// guarded voxel edits allowed. Each world retains its latest ten backups.
/// Undo reads each journal after any restart and compare-writes originals
/// only where the expected post-cleanup voxel still exists.
/// </summary>
public sealed partial class BaseCleanupMod
{
    private const string LedgerPrefix = "cleanup-rollback-v1-";
    // Preserve the v0.3.1/0.3.2 journal intact, even if fully undone.
    private const string PreviousLedgerPrefix = "cleanup-rollback-v2-";
    // Independent test journal for the material-37 experiment. No recovery
    // record from any released version is overwritten.
    private const string CurrentLedgerPrefix = "cleanup-rollback-v3-";
    // v0.3.4: distinct key for EACH confirmed REMOVE, preserving every
    // previous recovery record. Newest operation must be undone first.
    private const string HistoryPrefix = "cleanup-rollback-v4-index-";
    private const string OperationPrefix = "cleanup-rollback-v4-";
    private const int MaxHistory = 32; // Fail closed; never discard recovery records.
    // One stable per-altar WorldData key; a second altar cannot overwrite
    // a previous altar's recovery record. GUIDs derived from ephemeral
    // entity IDs would be wrong across hosted world restarts.
    private static string LedgerKeyFor(PlayerBase altar)
    {
        var parts = new[] {
            altar.Center.X, altar.Center.Y, altar.Center.Z,
            (double)altar.Half.X, (double)altar.Half.Y, (double)altar.Half.Z,
        };
        var normalized = string.Join("|", parts.Select(n =>
            Math.Round(n, 3).ToString("F3", CultureInfo.InvariantCulture)));
        return LedgerPrefix + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant()[..32];
    }
    // Journal keys use the same stable altar geometry hash across generations.
    // Only the prefix changes; v0.2 and v0.3.2 histories remain recoverable.
    private static string PreviousLedgerKeyFor(PlayerBase altar) =>
        PreviousLedgerPrefix + LedgerKeyFor(altar)[LedgerPrefix.Length..];

    private static string CurrentLedgerKeyFor(PlayerBase altar) =>
        CurrentLedgerPrefix + LedgerKeyFor(altar)[LedgerPrefix.Length..];

    // Older builds could label already-restored cells as conflicts if UNDO
    // was invoked again after a successful rollback. The cumulative conflict
    // counter is therefore not proof that any edits remain: once the journal
    // records ALL edited cells restored in phase "undone", it is settled.
    // Partially restored journals are still blocked regardless of conflicts.
    private static bool FullyUndone(CleanupLedger old) =>
        old.Phase == "undone" && old.Planned > 0 &&
        (old.Removed > 0 || old.OperationNumber > 0) &&
        old.Restored >= old.Removed;

    // A *successful* earlier cleanup is also safe to retain without undoing:
    // the new experiment has a SEPARATE journal key, so no earlier original
    // voxel record can be overwritten. We must not force the user to undo
    // legitimately accepted rubble/decay merely to run a new mud test.
    //
    // "complete" alone is not sufficient: a partially committed cleanup,
    // any skipped/conflicted write, or a partially executed Undo must still
    // block subsequent operations until its recovery is resolved.
    private static bool SettledPreviousJournal(CleanupLedger previous) =>
        FullyUndone(previous) ||
        (previous.Phase == "complete" &&
         previous.Planned > 0 && previous.Removed == previous.Planned &&
         previous.Restored == 0 && previous.Conflicted == 0);

    private static string HistoryKeyFor(PlayerBase altar) =>
        HistoryPrefix + LedgerKeyFor(altar)[LedgerPrefix.Length..];

    private static string OperationKeyFor(PlayerBase altar, int sequence) =>
        OperationPrefix + LedgerKeyFor(altar)[LedgerPrefix.Length..] +
        "-" + sequence.ToString("D6", CultureInfo.InvariantCulture);

    private static CleanupHistory ReadHistory(PlayerBase altar)
    {
        if (!WorldData.TryGet<CleanupHistory>(HistoryKeyFor(altar), out var history))
            return new CleanupHistory();
        if (history is null || history.Sequences is null || history.NextSequence < 1 ||
            history.NextSequence > 1_000_000 || history.Sequences.Count > MaxHistory)
            throw new InvalidOperationException("invalid cleanup operation history; no writes permitted");
        var previous = 0;
        foreach (var sequence in history.Sequences)
        {
            if (sequence <= previous || sequence >= history.NextSequence)
                throw new InvalidOperationException("invalid or non-monotonic cleanup history; no writes permitted");
            previous = sequence;
        }
        return history;
    }

    private static IEnumerable<CleanupLedger> ReadOperationJournals(PlayerBase altar)
    {
        var history = ReadHistory(altar);
        for (var i = history.Sequences.Count - 1; i >= 0; i--)
        {
            var sequence = history.Sequences[i];
            if (!WorldData.TryGet<CleanupLedger>(OperationKeyFor(altar, sequence), out var journal) ||
                journal is null || string.IsNullOrEmpty(journal.Packed) ||
                journal.OperationNumber != sequence)
                throw new InvalidOperationException(
                    $"Cleanup operation #{sequence} is missing/corrupt. Refusing to proceed.");
            yield return journal;
        }
    }

    private static bool TryGetJournal(PlayerBase altar, out CleanupLedger journal)
    {
        // Latest NOT YET undone operation first. Older journals are never
        // overwritten; after undoing the latest, the next becomes available.
        // If a committed operation was followed by another operation, undo
        // in reverse order to protect dependent voxel states.
        foreach (var item in ReadOperationJournals(altar))
        {
            if (!FullyUndone(item)) { journal = item; return true; }
        }
        if (WorldData.TryGet<CleanupLedger>(CurrentLedgerKeyFor(altar), out var v3) &&
            !string.IsNullOrEmpty(v3.Packed) && !FullyUndone(v3))
        {
            journal = v3; return true;
        }
        if (WorldData.TryGet<CleanupLedger>(PreviousLedgerKeyFor(altar), out var v2) &&
            !string.IsNullOrEmpty(v2.Packed) && !FullyUndone(v2))
        {
            journal = v2; return true;
        }
        if (WorldData.TryGet<CleanupLedger>(LedgerKeyFor(altar), out var v1) &&
            !string.IsNullOrEmpty(v1.Packed) && !FullyUndone(v1))
        {
            journal = v1; return true;
        }
        journal = null!;
        return false;
    }

    private const string CleanupPermission = "base-cleanup.command.baseclean";
    private const string ConfirmRemove = "REMOVE";
    private const string ConfirmUndo = "UNDO";
    private const int PreviewMinutes = 10;
    private const string ConfirmDeleteJournals = "DELETE_ALL_JOURNALS";
    private const int PurgeMinutes = 2;
    private EntityId _journalPurgePlayer = EntityId.None;
    private string? _journalPurgeWorldId;
    private string[]? _journalPurgeKeys;
    private DateTime _journalPurgeAtUtc;

    private CleanupPlan? _preparedCleanup;
    private CleanupPlan? _activeCleanup;
    private bool _cleanupBusy;
    private bool _cleanupCancel;
    private int _cleanupEpoch;
    private CleanupStatus? _viewCleanup;
    private string _cleanupConfirm = "";
    private string _undoConfirm = "";
    private bool _includeRubble = true;
    private bool _includeMud = true;
    private bool _includeMycelium = true;
    private bool _includeDecay = true; // Default ON; remains independently user-toggleable.

    private void CleanupRequestLocal(byte operation, byte options = 0)
    {
        if (!Client.HasLocalPlayer || Client.LocalPlayer.IsNone)
        {
            Hud.Toast("Base Cleanup: enter a world first.", ToastKind.Warning);
            return;
        }
        var player = Client.LocalPlayer;
        if (Client.IsHosting)
            Context.Scheduler.Post(() => CleanupAct(player, operation, options, localHost: true));
        else if (_net?.SendToServer(new CleanupRequest(operation, options)) != true)
            Hud.Toast("Base Cleanup: server does not have the cleanup channel.", ToastKind.Warning);
    }

    private void OnCleanupRequest(CleanupRequest message, NetContext context) =>
        CleanupAct(context.Player, message.Operation, message.Options, localHost: false);

    private void CleanupAct(EntityId player, byte operation, byte options, bool localHost)
    {
        if (player.IsNone || (!localHost && !Permissions.Has(player, CleanupPermission)))
        {
            CleanupPublish(player, new("failed", "Admin permission is required for Base Cleanup.", 0, 0, 0, 0, 0, ""));
            return;
        }
        switch (operation)
        {
            case 1: BeginPreparation(player, options); break;
            case 2: BeginCleanup(player, options); break;
            case 3: BeginUndo(player); break;
            case 4: CancelCleanup(player); break;
            case 5: CleanupPublish(player, CleanupCurrentStatus(player)); break;
            default: CleanupPublish(player, new("failed", "Unknown cleanup action.", 0, 0, 0, 0, 0, "")); break;
        }
    }

    private CleanupStatus CleanupCurrentStatus(EntityId player)
    {
        if (_cleanupBusy && _activeCleanup is { } active)
            return new("working", "Preparing or applying a cleanup; see progress updates.",
                active.Read, active.Total, active.Hits, 0, 0, "");
        if (_preparedCleanup is { } p)
            return PlanStatus(p, "prepared", "Plan ready. Inspect the counts and type REMOVE to confirm.");
        if (Saves.Available && Saves.WorldDataReady && AltarAt(player) is { } here)
        {
            try
            {
                if (TryGetJournal(here, out var l))
                    return new(l.Phase, $"Saved operation " +
                        (l.OperationNumber > 0 ? $"#{l.OperationNumber}" : "(legacy)") +
                        $": {l.Planned:N0} voxels (decay {l.DecayPlanned:N0}); " +
                        $"edited {l.Removed:N0}; restored {l.Restored:N0}; " +
                        $"conflicts {l.Conflicted:N0}. Undo is separate for each operation, newest first.",
                        l.Removed + l.Restored, l.Planned, 0, 0, 0, l.BackupName) { Decay = l.DecayPlanned };
            }
            catch (Exception ex)
            {
                Log.Warn("base-cleanup journal status unavailable: " + ex);
                return new("failed", "Saved cleanup history is unreadable; " +
                    "no operation will be permitted until recovery records are resolved.",
                    0, 0, 0, 0, 0, "");
            }
        }
        return new("idle", "Step 1: Scan current Flame Altar. No pending Undo operations.", 0, 0, 0, 0, 0, "");
    }

    private static CleanupStatus PlanStatus(CleanupPlan p, string phase, string message) =>
        new(phase, message, p.Read, p.Total,
            BaseScanCore.PlaceableRubble.Sum(x => p.Materials[x]) + BaseScanCore.WorldRubble.Sum(x => p.Materials[x]),
            p.Materials[BaseScanCore.MudTerrain],
            BaseScanCore.MineableMycelium.Sum(x => p.Materials[x]), "") { Decay = p.DecayHits };

    private void CleanupPublish(EntityId recipient, CleanupStatus message)
    {
        Log.Info($"base-cleanup: {message.Phase}: {message.Message}");
        if (Client.IsHosting && Client.HasLocalPlayer && Client.LocalPlayer == recipient)
            Client.Post(() => CleanupReceive(message));
        else
            _net?.SendTo(recipient, message);
    }

    private void CleanupReceive(CleanupStatus status)
    {
        Volatile.Write(ref _viewCleanup, status);
        var step = Volatile.Read(ref _uiStage);
        // Status may be refreshed when opening the panel. A previously
        // completed cleanup does not jump past Step 1 on a fresh session.
        switch (status.Phase)
        {
            case "planning":
                Volatile.Write(ref _uiStage, 2);
                break;
            case "prepared":
                Volatile.Write(ref _uiStage, 3);
                break;
            case "backup":
            case "applying":
            case "undoing":
                Volatile.Write(ref _uiStage, 4);
                break;
            case "complete":
            case "partial":
                if (step is 3 or 4) Volatile.Write(ref _uiStage, 5);
                break;
            case "undone":
                // A completed Undo consumes the operation's rollback step.
                // The next REMOVE must always start from a fresh scan.
                if (step is 4 or 5)
                {
                    Volatile.Write(ref _uiStage, 0);
                    Volatile.Write(ref _viewReport, null);
                    Volatile.Write(ref _viewProgress, null);
                    _cleanupConfirm = "";
                    _undoConfirm = "";
                    // Refresh the history so an EARLIER independently saved
                    // operation can be offered for its own Undo.
                    Client.Post(() => CleanupRequestLocal(5));
                }
                break;
            case "cancelled":
                Volatile.Write(ref _uiStage,
                    Volatile.Read(ref _viewReport) is { UnreadableCells: 0 } ? 1 : 0);
                break;
            case "failed":
                // An invalid/expired plan must not leave REMOVE visible.
                if (step == 3) Volatile.Write(ref _uiStage, 1);
                else if (step == 2) Volatile.Write(ref _uiStage, 1);
                else if (step == 4)
                    Volatile.Write(ref _uiStage,
                        status.Message.Contains("NO VOXELS CHANGED", StringComparison.Ordinal)
                            ? 1 : 5);
                break;
        }
        if (status.Phase is "failed" or "complete" or "cancelled" or "partial" or "undone")
            Hud.Toast(status.Message, status.Phase is "complete" or "undone" ? ToastKind.Success : ToastKind.Warning);
    }

    private static PlayerBase? AltarAt(EntityId player)
    {
        if (World.Position(player) is not { } here || !Build.Available) return null;
        return Build.Bases.Where(b => b.Contains(here))
            .OrderBy(b => SquaredDistance(here, b.Center))
            .ThenBy(b => b.Altar.Value)
            .FirstOrDefault();
    }

    private static bool SafeServices(out string why)
    {
        if (!Build.Available || Build.Status is not { Read: true, Write: true, Persist: true } ||
            !Capabilities.Is("build.read") || !Capabilities.Is("build.write"))
        {
            why = "Kiln Build cannot safely read, write and persist voxels on this game.";
            return false;
        }
        if (!Saves.Available || !Saves.WorldDataReady || Saves.WorldId is null ||
            !Capabilities.Is("kiln.saves"))
        {
            why = "Kiln Saves or current-world mod data is not ready; cleanup refused.";
            return false;
        }
        if (WorldData.Loaded != DataMatch.Exact)
        {
            why = "World mod-data generation does not exactly match this save; cleanup/undo refused.";
            return false;
        }
        why = "";
        return true;
    }

    private bool HasOutstandingLedger(PlayerBase altar, out string message)
    {
        message = "";
        try
        {
            var history = ReadHistory(altar);
            if (history.Sequences.Count >= MaxHistory)
            {
                message = $"Cleanup history has reached {MaxHistory} permanent operations. " +
                    "No journal may be overwritten. Use a new altar/world or arrange archival.";
                return true;
            }
            foreach (var item in ReadOperationJournals(altar))
            {
                if (!SettledPreviousJournal(item))
                {
                    message = $"Operation #{item.OperationNumber} needs recovery " +
                        $"({item.Phase}; edited {item.Removed}, restored {item.Restored}, " +
                        $"conflicts {item.Conflicted}). Use Undo before preparing another.";
                    return true;
                }
            }
            // The v1/v2/v3 records are immutable, including after Undo.
            foreach (var key in new[] { CurrentLedgerKeyFor(altar),
                PreviousLedgerKeyFor(altar), LedgerKeyFor(altar) })
            {
                if (WorldData.TryGet<CleanupLedger>(key, out var legacy) &&
                    !string.IsNullOrEmpty(legacy.Packed) &&
                    !SettledPreviousJournal(legacy))
                {
                    message = $"An earlier cleanup needs recovery ({legacy.Phase}; " +
                        $"edited {legacy.Removed}, restored {legacy.Restored}, " +
                        $"conflicts {legacy.Conflicted}). Undo before preparing another.";
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("base-cleanup history verification refused to proceed: " + ex);
            message = "Cleanup journal history unreadable; no new edits allowed: " + ex.Message;
            return true;
        }
    }

    private void BeginPreparation(EntityId player, byte options)
    {
        if (options == 0 || (options & ~CleanupCore.AllowedOptions) != 0)
        {
            CleanupPublish(player, new("failed", "Invalid cleanup material options.", 0, 0, 0, 0, 0, ""));
            return;
        }
        if (_cleanupBusy || _running is not null)
        {
            CleanupPublish(player, new("failed", "Finish or cancel the current survey/cleanup first.", 0, 0, 0, 0, 0, ""));
            return;
        }
        if (!SafeServices(out var reason))
        {
            CleanupPublish(player, new("failed", reason, 0, 0, 0, 0, 0, ""));
            return;
        }
        if (AltarAt(player) is not { } altar || Build.Grid is not { } grid)
        {
            CleanupPublish(player, new("failed", "Stand inside the Flame Altar to prepare its cleanup.", 0, 0, 0, 0, 0, ""));
            return;
        }
        if (_latest is null || _latest.UnreadableCells != 0 ||
            _lastSurveyWorldId != Saves.WorldId || _lastSurveyAltar is not { } surveyed ||
            surveyed.Altar != altar.Altar || surveyed.Center != altar.Center ||
            surveyed.Half != altar.Half)
        {
            CleanupPublish(player, new("failed",
                "Step 1 required: complete a fresh, readable survey in this Flame Altar before selecting cleanup options.",
                0, 0, 0, 0, 0, ""));
            return;
        }
        if (HasOutstandingLedger(altar, out reason))
        {
            CleanupPublish(player, new("failed", reason, 0, 0, 0, 0, 0, ""));
            return;
        }
        var box = BaseScanCore.ZoneCells(altar, grid);
        if (box.IsEmpty)
        {
            CleanupPublish(player, new("failed", "The altar volume does not intersect the voxel grid.", 0, 0, 0, 0, 0, ""));
            return;
        }
        _cleanupBusy = true;
        _cleanupCancel = false;
        _preparedCleanup = null;
        var plan = new CleanupPlan(player, Saves.WorldId!, altar, options, _cleanupEpoch) { Total = box.Count };
        _activeCleanup = plan;
        _ = PrepareAsync(plan, box);
    }

    private async Task PrepareAsync(CleanupPlan plan, CellBox bounds)
    {
        try
        {
            CleanupPublish(plan.Player, PlanStatus(plan, "planning",
                $"READ-ONLY planning scan: {bounds.Count:N0} voxels in Flame Altar {plan.Altar.Altar.Value}."));
            var nextNotice = 10;
            foreach (var box in BaseScanCore.Batches(bounds))
            {
                if (StopRequested(plan))
                {
                    CleanupPublish(plan.Player, PlanStatus(plan, "cancelled",
                        "Planning cancelled. No world blocks were changed."));
                    return;
                }
                if (!SameZone(plan) || !await Build.Load(box, TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("the Flame Altar or a voxel tile became unavailable; no cleanup was made");
                if (StopRequested(plan)) return;
                var values = Build.Read(box);
                CleanupChunk? chunk = null;
                for (var i = 0; i < values.Length; i++)
                {
                    var v = values[i];
                    if (!CleanupCore.Target(v, plan.Options)) continue;
                    chunk ??= new CleanupChunk(box);
                    chunk.Cells.Add(new CleanupCell(i, v.Raw));
                    if (CleanupCore.DecayTarget(v, plan.Options))
                        plan.DecayHits++;
                    else
                        plan.Materials[v.Material]++;
                    plan.Hits++;
                }
                if (chunk is not null) plan.Chunks.Add(chunk);
                plan.Read += box.Count;
                var percent = (int)(100 * plan.Read / plan.Total);
                if (percent >= nextNotice)
                {
                    CleanupPublish(plan.Player, PlanStatus(plan, "planning",
                        $"Planning {percent}% (read-only); {plan.Hits:N0} candidate cells."));
                    nextNotice = (percent / 10 + 1) * 10;
                }
                await Context.Scheduler.NextTick();
            }
            if (StopRequested(plan)) return;
            plan.PreparedAtUtc = DateTime.UtcNow;
            _preparedCleanup = plan;
            CleanupPublish(plan.Player, PlanStatus(plan, "prepared",
                $"Plan complete: {plan.Hits:N0} cells (decay repair {plan.DecayHits:N0}) " +
                $"across {plan.Chunks.Count:N0} batches. No edits yet. " +
                "Review counts, then type REMOVE to create an isolated Undo journal and apply."));
        }
        catch (Exception ex)
        {
            Log.Warn("base-cleanup: planning failed: " + ex);
            if (plan.Generation == _cleanupEpoch)
                CleanupPublish(plan.Player, PlanStatus(plan, "failed",
                    "Planning aborted without edits: " + ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_activeCleanup, plan)) _activeCleanup = null;
            _cleanupBusy = false;
        }
    }

    private bool SameZone(CleanupPlan plan) =>
        plan.Generation == _cleanupEpoch && Saves.WorldId == plan.WorldId &&
        Build.Grid is not null &&
        Build.Bases.Any(b => b.Altar == plan.Altar.Altar &&
            b.Center == plan.Altar.Center && b.Half == plan.Altar.Half);

    private bool StopRequested(CleanupPlan plan) =>
        _cleanupCancel || plan.CancelRequested || plan.Generation != _cleanupEpoch;

    private void BeginCleanup(EntityId player, byte selectedOptions)
    {
        if (_cleanupBusy || _running is not null)
        {
            CleanupPublish(player, new("failed", "A scan or cleanup is already running.", 0, 0, 0, 0, 0, ""));
            return;
        }
        if (_preparedCleanup is not { } p || p.Player != player ||
            p.PreparedAtUtc.AddMinutes(PreviewMinutes) < DateTime.UtcNow ||
            p.Hits == 0 || p.Hits > 4_000_000)
        {
            CleanupPublish(player, new("failed", "No unexpired, nonempty plan for this player. Prepare again.", 0, 0, 0, 0, 0, ""));
            return;
        }
        if (selectedOptions != p.Options)
        {
            CleanupPublish(player, new("failed",
                "The cleanup options changed after planning. Prepare a new plan before confirming.",
                0, 0, 0, 0, 0, ""));
            return;
        }
        if (!SafeServices(out var reason) || !SameZone(p) ||
            AltarAt(player) is not { } current || current.Altar != p.Altar.Altar)
        {
            CleanupPublish(player, new("failed", reason.Length > 0 ? reason :
                "The altar, world or player location has changed. Prepare again.", 0, 0, 0, 0, 0, ""));
            return;
        }
        if (HasOutstandingLedger(p.Altar, out reason))
        {
            CleanupPublish(player, new("failed", reason, 0, 0, 0, 0, 0, ""));
            return;
        }
        _cleanupBusy = true;
        _cleanupCancel = false;
        _activeCleanup = p;
        _preparedCleanup = null;
        _ = ApplyAsync(p);
    }

    private async Task ApplyAsync(CleanupPlan plan)
    {
        CleanupLedger? journal = null;
        CleanupHistory? history = null;
        string? journalKey = null;
        // The pre-REMOVE full-world backup saves a consistent world file and
        // matching Kiln WorldData generation. It must finish, be verified and
        // pass retention BEFORE any voxel write. No restore UI is provided.
        try
        {
            history = ReadHistory(plan.Altar);
            if (history.Sequences.Count >= MaxHistory)
                throw new InvalidOperationException("per-altar cleanup history full");
            var sequence = history.NextSequence;
            journalKey = OperationKeyFor(plan.Altar, sequence);
            if (WorldData.Contains(journalKey))
                throw new InvalidOperationException("cleanup operation key already exists; refusing overwrite");
            var name = "journal-op" + sequence.ToString("D6", CultureInfo.InvariantCulture);
            journal = CleanupSafety.Ledger(plan, name);
            journal.OperationNumber = sequence;
            // Stage the original voxel journal and operation index BEFORE
            // the backup, so its saved world and mod data are consistent.
            WorldData.Set(journalKey, journal);
            history.NextSequence++;
            history.Sequences.Add(sequence);
            WorldData.Set(HistoryKeyFor(plan.Altar), history);
            if (StopRequested(plan) || !SameZone(plan))
                throw new OperationCanceledException("cancelled or altar changed before backup");
            CleanupPublish(plan.Player, PlanStatus(plan, "backup",
                "Saving a timestamped full-world backup before REMOVE. No voxels have changed."));
            var backup = await Saves.Backup(CleanupBackups.Label);
            CleanupBackups.Verify(backup, plan.WorldId);
            var pruned = CleanupBackups.Prune(backup);
            if (StopRequested(plan) || !SameZone(plan))
                throw new OperationCanceledException("cancelled or altar changed after backup; no voxels changed");
            journal.BackupName = Path.GetFileName(backup.Directory);
            journal.Phase = "applying";
            Log.Info($"base-cleanup: world backup {journal.BackupName} verified; " +
                $"rotated {pruned} old backup(s), keeping at most {CleanupBackups.KeepPerWorld} for this world");
            CleanupPublish(plan.Player, new("applying",
                $"World backup {journal.BackupName} verified; applying selected cleanup " +
                $"({plan.DecayHits:N0} weathered building voxels).",
                0, plan.Hits, 0, 0, 0, journal.BackupName));

            var completed = 0L;
            var conflicts = 0L;
            var chunks = 0;
            var options = new BuildOptions
            {
                Flags = BuildFlags.InBases | BuildFlags.RespectZones,
                Actor = plan.Player,
                Journal = false, // Persistent WorldData journal, not 64-entry session history.
                Label = "Base Cleanup v0.3 terrain / cosmetic decay",
            };
            foreach (var chunk in plan.Chunks)
            {
                if (StopRequested(plan)) break;
                if (!SameZone(plan)) throw new InvalidOperationException("world or altar changed during cleanup");
                if (!await Build.Load(chunk.Box, TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("voxel tile unavailable during cleanup");
                if (StopRequested(plan)) break;

                var expected = Build.Read(chunk.Box);
                var desired = (Voxel[])expected.Clone();
                var mask = new bool[expected.Length];
                var toWrite = 0;
                foreach (var cell in chunk.Cells)
                {
                    if (expected[cell.Index].Raw != cell.Raw)
                    {
                        conflicts++;
                        continue;
                    }
                    desired[cell.Index] = CleanupCore.EditedValue(cell.Raw);
                    mask[cell.Index] = true;
                    toWrite++;
                }
                if (toWrite > 0)
                {
                    var result = Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask);
                    if (result.Refusal == BuildRefusal.Changed)
                        conflicts += toWrite; // Another writer raced us. Do not overwrite.
                    else if (result.Refused || !result.Verified || result.CellsChanged != toWrite)
                        throw new InvalidOperationException($"voxel commit refused or unverified at {chunk.Box}: " +
                            $"{result.Reason ?? "unexpected count/verification"} (changed {result.CellsChanged}/{toWrite})");
                    else
                        journal.Removed += result.CellsChanged;
                }
                completed += chunk.Cells.Count;
                journal.Conflicted = conflicts;
                if (++chunks % 20 == 0 || completed == plan.Hits)
                    CleanupPublish(plan.Player, new("applying",
                        $"Cleanup: processed {completed:N0}/{plan.Hits:N0} cells; edited {journal.Removed:N0}, " +
                        $"skipped {conflicts:N0}. Only weather bit changes for buildings.",
                        completed, plan.Hits, 0, 0, 0, journal.BackupName));
                await Context.Scheduler.NextTick();
            }

            journal.Phase = journal.Removed == 0 ? "undone" :
                StopRequested(plan) || conflicts > 0 ? "partial" : "complete";
            // A new REMOVE requires a new Stage 1 survey, even if a previous
            // cleanup was fully undone. No stale scan/plan can be reused.
            _latest = null;
            _lastSurveyWorldId = null;
            _lastSurveyAltar = null;
            CleanupPublish(plan.Player, new(journal.Phase,
                $"{(StopRequested(plan) ? "Stopped" : "Finished")}: edited {journal.Removed:N0} voxels " +
                $"(decay planned {plan.DecayHits:N0}); skipped {conflicts:N0}. " +
                $"Backup {journal.BackupName}. Type UNDO to restore only unchanged cells.",
                completed, plan.Hits, 0, 0, 0, journal.BackupName));
            Log.Info($"base-cleanup: removal {journal.Phase}; removed={journal.Removed}, conflicts={conflicts}, backup={journal.BackupName}");
        }
        catch (Exception ex)
        {
            Log.Warn("base-cleanup: apply stopped: " + ex);
            if (journal is not null)
            {
                // An already indexed journal is NEVER deleted, including if
                // application failed. If no voxels were committed, recording
                // it as undone is safe; otherwise Undo can recover originals.
                journal.Phase = journal.Removed > 0 ? "partial" : "undone";
            }
            if (plan.Generation == _cleanupEpoch)
                CleanupPublish(plan.Player, PlanStatus(plan, "failed",
                    (journal?.Removed > 0 ? "Cleanup interrupted; journal supports UNDO. " :
                        "NO VOXELS CHANGED (backup or pre-commit validation failed). ") + ex.Message));
        }
        finally
        {
            _cleanupBusy = false;
            if (ReferenceEquals(_activeCleanup, plan)) _activeCleanup = null;
        }
    }

    private void BeginUndo(EntityId player)
    {
        if (_cleanupBusy || _running is not null)
        {
            CleanupPublish(player, new("failed", "Another operation is running.", 0, 0, 0, 0, 0, ""));
            return;
        }
        if (!SafeServices(out var reason))
        {
            CleanupPublish(player, new("failed", reason, 0, 0, 0, 0, 0, ""));
            return;
        }
        if (AltarAt(player) is not { } altar)
        {
            CleanupPublish(player, new("failed", "Stand inside the altar whose cleanup you want to undo.",
                0, 0, 0, 0, 0, ""));
            return;
        }
        CleanupLedger journal;
        try
        {
            if (!TryGetJournal(altar, out journal))
            {
                CleanupPublish(player, new("failed", "No remaining operation to undo in this altar.",
                    0, 0, 0, 0, 0, ""));
                return;
            }
        }
        catch (Exception ex)
        {
            CleanupPublish(player, new("failed", "Cannot load saved Undo history: " + ex.Message,
                0, 0, 0, 0, 0, ""));
            return;
        }
        if (Saves.WorldId != journal.WorldId || Build.Grid is not { } grid ||
            Math.Abs(grid.CellSize - journal.CellSize) > 1e-9 ||
            !Build.Bases.Any(b => CleanupSafety.MatchingAltar(journal, b)))
        {
            CleanupPublish(player, new("failed", "Rollback refused: world identity, grid or altar volume changed.",
                0, journal.Planned, 0, 0, 0, journal.BackupName));
            return;
        }
        _cleanupBusy = true;
        _cleanupCancel = false;
        _preparedCleanup = null;
        _ = UndoAsync(player, journal, _cleanupEpoch);
    }

    private async Task UndoAsync(EntityId player, CleanupLedger journal, int generation)
    {
        var visited = 0L;
        var restored = 0L;
        var conflicts = 0L;
        try
        {
            var chunks = CleanupCore.Decode(journal.Packed);
            if (CleanupCore.Count(chunks) != journal.Planned)
                throw new InvalidOperationException("cleanup journal count mismatch");
            CleanupPublish(player, new("undoing", $"Selective Undo from journal {journal.BackupName}...",
                0, journal.Planned, 0, 0, 0, journal.BackupName));
            var options = new BuildOptions
            {
                Flags = BuildFlags.InBases | BuildFlags.RespectZones,
                Actor = player, Journal = false, Label = "Base Cleanup v0.3 selective undo",
            };
            foreach (var chunk in chunks)
            {
                if (_cleanupCancel || generation != _cleanupEpoch) break;
                if (Saves.WorldId != journal.WorldId ||
                    !Build.Bases.Any(b => CleanupSafety.MatchingAltar(journal, b)))
                    throw new InvalidOperationException("world/altar changed during rollback");
                if (!await Build.Load(chunk.Box, TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("rollback voxel tile did not load");
                if (_cleanupCancel || generation != _cleanupEpoch) break;
                var expected = Build.Read(chunk.Box);
                var desired = (Voxel[])expected.Clone();
                var mask = new bool[expected.Length];
                var restoreCount = 0;
                foreach (var cell in chunk.Cells)
                {
                    if (expected[cell.Index].Raw == cell.Raw)
                    {
                        // Already restored by an earlier partial Undo (or never
                        // committed due to a guarded conflict). No new change,
                        // and definitely NOT an Undo conflict.
                        continue;
                    }
                    if (expected[cell.Index].Raw != CleanupCore.EditedValue(cell.Raw).Raw)
                    {
                        // Terrain must still be air; cosmetic repair must
                        // retain the exact post-edit material and paint bits.
                        // Never overwrite unrelated changes.
                        conflicts++;
                        continue;
                    }
                    desired[cell.Index] = new Voxel(cell.Raw);
                    mask[cell.Index] = true;
                    restoreCount++;
                }
                if (restoreCount > 0)
                {
                    var result = Build.WriteIfUnchanged(chunk.Box, desired, expected, options, mask);
                    if (result.Refusal == BuildRefusal.Changed)
                        conflicts += restoreCount;
                    else if (result.Refused || !result.Verified || result.CellsChanged != restoreCount)
                        throw new InvalidOperationException($"rollback commit unverified at {chunk.Box}: " +
                            (result.Reason ?? "unexpected count/verification"));
                    else
                        restored += result.CellsChanged;
                }
                visited += chunk.Cells.Count;
                if (visited % 4000 < chunk.Cells.Count || visited == journal.Planned)
                    CleanupPublish(player, new("undoing",
                        $"Rollback {visited:N0}/{journal.Planned:N0}; restored {restored:N0}; not overwritten {conflicts:N0}.",
                        visited, journal.Planned, 0, 0, 0, journal.BackupName));
                await Context.Scheduler.NextTick();
            }
            journal.Restored += restored;
            journal.Conflicted = conflicts;
            journal.Phase = !_cleanupCancel && generation == _cleanupEpoch &&
                journal.Restored >= journal.Removed && conflicts == 0
                    ? "undone" : "partial";
            CleanupPublish(player, new(journal.Phase,
                $"Undo: restored {restored:N0} voxels; skipped {conflicts:N0} changed/already-restored cells. " +
                $"Original record retained: {journal.BackupName}.",
                visited, journal.Planned, 0, 0, 0, journal.BackupName));
        }
        catch (Exception ex)
        {
            Log.Warn("base-cleanup: undo interrupted: " + ex);
            journal.Restored += restored;
            journal.Conflicted = conflicts;
            journal.Phase = "partial";
            if (generation == _cleanupEpoch)
                CleanupPublish(player, new("failed",
                    "Undo interrupted; journal retained for retry. " + ex.Message,
                    visited, journal.Planned, 0, 0, 0, journal.BackupName));
        }
        finally { _cleanupBusy = false; }
    }

    private void CancelCleanup(EntityId player)
    {
        if (!_cleanupBusy)
        {
            _preparedCleanup = null; // A prepared plan is purely memory.
            CleanupPublish(player, new("cancelled", "Unconfirmed plan discarded. No changes were made.", 0, 0, 0, 0, 0, ""));
            return;
        }
        _cleanupCancel = true;
        if (_activeCleanup is { } p) p.CancelRequested = true;
        CleanupPublish(player, new("working",
            "Cancellation requested. Any committed cells remain in the saved undo journal.",
            0, 0, 0, 0, 0, ""));
    }

    private void DrawCleanup(UiDraw ui)
    {
        var step = Volatile.Read(ref _uiStage);
        var status = Volatile.Read(ref _viewCleanup);
        var flags = (byte)((_includeRubble ? CleanupCore.IncludeRubble : 0) |
                           (_includeMud ? CleanupCore.IncludeMud : 0) |
                           (_includeMycelium ? CleanupCore.IncludeMycelium : 0) |
                           (_includeDecay ? CleanupCore.IncludeDecay : 0));

        if (step == 1)
        {
            ui.TextWrapped("Select cleanup categories for this operation. " +
                "To roll back one category independently, select only that category, " +
                "REMOVE, then use its separate Undo journal.");
            ui.TextWrapped("MUD-37 TEST: the mud choice digs ALL matching terrain in this " +
                "3-D Flame Altar, including buried cells. It may create deep holes. " +
                "Review the plan on a copied world.");
            ui.Checkbox("Include rubble (entire 3-D altar)", ref _includeRubble);
            ui.Checkbox("Include creek/ditch mud (material 37) — TEST", ref _includeMud);
            ui.Checkbox("Include mineable mycelium", ref _includeMycelium);
            ui.Checkbox("Repair cosmetic decay (ON by default)", ref _includeDecay);
            ui.TextDisabled("Building decay clears weather bit only; ivy/overgrowth is excluded.");
            if (ui.Button("2. Prepare selected options (read-only)"))
            {
                Volatile.Write(ref _uiStage, 2);
                Client.Post(() => CleanupRequestLocal(1, flags));
            }
            ui.SameLine();
            if (ui.Button("Rescan instead"))
            {
                Volatile.Write(ref _uiStage, 0);
                Client.Post(() => Request(1));
            }
            if (status?.Phase == "failed")
                ui.TextWrapped("Previous request: " + status.Message);
            return;
        }

        if (step == 2)
        {
            ui.Separator("3 / 5 - Preparing read-only plan");
            ui.TextWrapped(status?.Message ?? "Waiting for the host to prepare the plan.");
            if (status is { Total: > 0 })
                ui.Progress((float)(status.Done / (double)status.Total),
                    $"{status.Done:N0} / {status.Total:N0} voxels inspected");
            if (ui.Button("Cancel this plan")) Client.Post(() => CleanupRequestLocal(4));
            return;
        }

        if (step == 3)
        {
            ui.Separator("4 / 5 - Review plan and confirm REMOVE");
            if (status?.Phase != "prepared")
            {
                ui.TextWrapped("Awaiting a valid server-side plan. REMOVE is not yet available.");
                return;
            }
            ui.TextWrapped(status.Message);
            ui.Text($"Rubble {status.Rubble:N0} | Mud 37 {status.Mud:N0} | " +
                $"Mycelium {status.Mycelium:N0} | Decay {status.Decay:N0}");
            ui.TextWrapped("The prepared plan is valid for 10 minutes. An independent Undo " +
                "journal will be registered in Kiln WorldData before editing. This mod " +
                "does NOT request a world save, restore or backup. Records persist with " +
                "the game's next normal save.");
            ui.InputText("Type REMOVE to commit", ref _cleanupConfirm, 20);
            if (ui.Button("4. REMOVE this prepared operation"))
            {
                if (_cleanupConfirm == ConfirmRemove)
                {
                    _cleanupConfirm = "";
                    Volatile.Write(ref _uiStage, 4);
                    Client.Post(() => CleanupRequestLocal(2, flags));
                }
                else Hud.Toast("Type REMOVE exactly to confirm this operation.", ToastKind.Warning);
            }
            ui.SameLine();
            if (ui.Button("Back: change options"))
            {
                _cleanupConfirm = "";
                Volatile.Write(ref _uiStage, 1);
                Client.Post(() => CleanupRequestLocal(4));
            }
            return;
        }

        if (step == 4)
        {
            ui.Separator("Operation in progress");
            ui.TextWrapped(status?.Message ?? "Awaiting operation status...");
            if (status is { Total: > 0 })
                ui.Progress((float)(status.Done / (double)status.Total),
                    $"{status.Done:N0} / {status.Total:N0}");
            if (!string.IsNullOrEmpty(status?.Backup))
                ui.TextWrapped("Journal reference: " + status.Backup);
            if (ui.Button("Request cancellation"))
                Client.Post(() => CleanupRequestLocal(4));
            return;
        }

        if (step == 5)
        {
            ui.Separator("5 / 5 - Roll back this operation");
            ui.TextWrapped(status?.Message ?? "Review the latest saved operation before Undo.");
            if (!string.IsNullOrEmpty(status?.Backup))
                ui.TextWrapped("Operation journal: " + status.Backup);
            ui.TextWrapped("Each REMOVE has its own saved Undo journal. " +
                "Undo restores only voxels still in this operation's edited state. " +
                "Later operations must be undone before earlier ones. " +
                "To restore categories individually, remove one category per operation.");
            ui.InputText("Type UNDO to restore", ref _undoConfirm, 20);
            if (ui.Button("5. UNDO latest eligible operation"))
            {
                if (_undoConfirm == ConfirmUndo)
                {
                    _undoConfirm = "";
                    Volatile.Write(ref _uiStage, 4);
                    Client.Post(() => CleanupRequestLocal(3));
                }
                else Hud.Toast("Type UNDO exactly to restore.", ToastKind.Warning);
            }
            ui.SameLine();
            if (ui.Button("Start another operation (fresh scan)"))
            {
                _undoConfirm = "";
                Volatile.Write(ref _uiStage, 0);
                Client.Post(() => Request(1));
            }
            return;
        }
    }

    private void CleanupStopped()
    {
        _cleanupEpoch++;
        _cleanupCancel = true;
        _preparedCleanup = null;
        _activeCleanup = null;
        _cleanupBusy = false;
        _journalPurgeKeys = null;
        _journalPurgeWorldId = null;
        _journalPurgePlayer = EntityId.None;
        _lastSurveyWorldId = null;
        _lastSurveyAltar = null;
        _latest = null;
        if (Client.Available && Client.IsClient)
            Client.Post(() =>
            {
                Volatile.Write(ref _uiStage, 0);
                Volatile.Write(ref _viewReport, null);
                Volatile.Write(ref _viewProgress, null);
                Volatile.Write(ref _viewCleanup, null);
                _cleanupConfirm = "";
                _undoConfirm = "";
            });
    }

    [Subscribe]
    private void CleanupWorldOpened(WorldLoadedEvent e)
    {
        CleanupStopped();
        // The world's own saved journal (WorldData) is read afresh on demand.
        CleanupPublishForHost();
    }

    private void CleanupPublishForHost()
    {
        if (Client.IsHosting && Client.HasLocalPlayer)
            CleanupPublish(Client.LocalPlayer, CleanupCurrentStatus(Client.LocalPlayer));
    }

    private static string[] JournalKeysInWorld() =>
        WorldData.Keys.Where(k =>
            k.StartsWith("cleanup-rollback-", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

    [Command("baseclean journals", Description =
        "Preview Base Cleanup Undo journal keys in the currently loaded world (no changes)",
        Permission = CommandPermission.Admin)]
    private void JournalsCommand(CommandContext c)
    {
        _journalPurgeKeys = null;
        _journalPurgeWorldId = null;
        _journalPurgePlayer = EntityId.None;
        if (c.Source.Player.IsNone || !Saves.Available || !Saves.WorldDataReady ||
            Saves.WorldId is null || WorldData.Loaded != DataMatch.Exact)
            throw new CommandException("Enter a loaded world with matching Kiln WorldData first.");
        if (_cleanupBusy || _running is not null)
            throw new CommandException("A scan/cleanup/Undo is running. Finish it before reviewing journals.");
        var keys = JournalKeysInWorld();
        c.Reply($"Base Cleanup journals: {keys.Length} keys in CURRENT world {Saves.WorldId}.");
        if (keys.Length == 0)
        {
            c.Reply("Nothing to delete. Kiln may still retain historical save generations elsewhere.");
            return;
        }
        c.Reply("Deleting these keys PERMANENTLY loses ALL Base Cleanup Undo history " +
            "for every altar in this world, including older v0.2/v0.3.2/v0.3.3 journals. " +
            "Terrain already changed is NOT restored.");
        c.Reply("It does NOT remove old Kiln save generations, world backups, " +
            "or anything belonging to other mods. No world save is requested.");
        _journalPurgeKeys = keys;
        _journalPurgeWorldId = Saves.WorldId;
        _journalPurgePlayer = c.Source.Player;
        _journalPurgeAtUtc = DateTime.UtcNow;
        c.Reply("For the next TWO minutes only, confirm in game chat: " +
            "/baseclean journals delete DELETE_ALL_JOURNALS");
    }

    [Command("baseclean journals delete", Description =
        "Permanently erase Base Cleanup journals in this world after an exact preview and confirmation",
        Permission = CommandPermission.Admin)]
    private void DeleteJournalsCommand(CommandContext c, string confirmation)
    {
        if (confirmation != ConfirmDeleteJournals)
            throw new CommandException("Exact confirmation required: DELETE_ALL_JOURNALS.");
        if (c.Source.Player.IsNone || !Saves.Available || !Saves.WorldDataReady ||
            Saves.WorldId is null || WorldData.Loaded != DataMatch.Exact)
            throw new CommandException("Current-world Kiln mod data is not ready; nothing deleted.");
        if (_cleanupBusy || _running is not null)
            throw new CommandException("Cannot delete journal history during a scan, cleanup or Undo.");
        if (_journalPurgeKeys is not { Length: > 0 } snapshot ||
            _journalPurgePlayer != c.Source.Player ||
            _journalPurgeWorldId != Saves.WorldId ||
            _journalPurgeAtUtc.AddMinutes(PurgeMinutes) < DateTime.UtcNow)
            throw new CommandException("Missing/expired journal preview. Run /baseclean journals again.");
        var current = JournalKeysInWorld();
        if (!current.SequenceEqual(snapshot, StringComparer.Ordinal))
            throw new CommandException("Journal keys changed after preview; nothing deleted. Preview again.");
        // Invalidate confirmation BEFORE any mutation so duplicate commands
        // cannot use the same preview. One mod, one current world; no files.
        _journalPurgeKeys = null;
        _journalPurgeWorldId = null;
        _journalPurgePlayer = EntityId.None;
        var removed = 0;
        foreach (var key in snapshot)
            if (WorldData.Remove(key)) removed++;
        _preparedCleanup = null;
        _latest = null;
        _lastSurveyWorldId = null;
        _lastSurveyAltar = null;
        if (Client.Available && Client.IsClient)
            Client.Post(() =>
            {
                Volatile.Write(ref _uiStage, 0);
                Volatile.Write(ref _viewReport, null);
                Volatile.Write(ref _viewCleanup, null);
            });
        Log.Warn($"base-cleanup: ADMIN DELETE ALL JOURNALS in current world: " +
            $"removed={removed}/{snapshot.Length} keys. No terrain modified; no save requested.");
        c.Reply($"Deleted {removed} Base Cleanup journal keys in the CURRENT world only. " +
            "No voxel edits or world save requested. Undo history is gone; " +
            "the change persists at the next game's normal save. " +
            "To remove the mod, uninstall its package separately in Kiln.");
    }

    [Command("baseclean prepare", Description = "Read-only plan of rubble/mud/mycelium and cosmetic decay in current Flame Altar",
        Permission = CommandPermission.Admin)]
    private void CleanupPrepareCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in the world");
        // Match the UI defaults. This is only a READ-ONLY plan;
        // edits still require a reviewed count and explicit REMOVE.
        BeginPreparation(c.Source.Player, CleanupCore.AllowedOptions);
        c.Reply("Preparing read-only cleanup plan including decay; inspect /baseclean status before REMOVE.");
    }

    [Command("baseclean preparedecay",
        Description = "Read-only plan including cosmetic decay (explicit opt-in)",
        Permission = CommandPermission.Admin)]
    private void CleanupPrepareDecayCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in the world");
        BeginPreparation(c.Source.Player, CleanupCore.AllowedOptions);
        c.Reply("Preparing plan INCLUDING decay; confirm only after inspecting counts.");
    }

    [Command("baseclean preparenodecay",
        Description = "Read-only plan excluding cosmetic decay (explicit opt-out)",
        Permission = CommandPermission.Admin)]
    private void CleanupPrepareNoDecayCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in the world");
        BeginPreparation(c.Source.Player, CleanupCore.IncludeRubble | CleanupCore.IncludeMud | CleanupCore.IncludeMycelium);
        c.Reply("Preparing plan EXCLUDING decay; inspect /baseclean status before REMOVE.");
    }

    [Command("baseclean confirm", Description = "Explicitly confirm backup and cleanup (requires REMOVE)",
        Permission = CommandPermission.Admin)]
    private void CleanupConfirmCommand(CommandContext c, string confirmation)
    {
        if (confirmation != ConfirmRemove) throw new CommandException("type /baseclean confirm REMOVE");
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in the world");
        // The console has no UI checkboxes. Confirm exactly the reviewed
        // server-side plan, never silently enable any options.
        BeginCleanup(c.Source.Player, _preparedCleanup?.Options ?? 0);
        c.Reply("Confirmation requested; backup must complete before cleanup starts.");
    }

    [Command("baseclean undo", Description = "Restore unchanged cleanup air voxels (requires UNDO)",
        Permission = CommandPermission.Admin)]
    private void CleanupUndoCommand(CommandContext c, string confirmation)
    {
        if (confirmation != ConfirmUndo) throw new CommandException("type /baseclean undo UNDO");
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in the world");
        BeginUndo(c.Source.Player);
        c.Reply("Selective rollback requested; see the Base Cleanup UI.");
    }

    [Command("baseclean status", Description = "Show cleanup plan and persistent undo-journal status",
        Permission = CommandPermission.Admin)]
    private void CleanupStatusCommand(CommandContext c)
    {
        var s = CleanupCurrentStatus(c.Source.Player);
        c.Reply($"[{s.Phase}] {s.Message}");
        c.Reply($"rubble={s.Rubble} mud={s.Mud} mycelium={s.Mycelium} decay={s.Decay} backup={s.Backup}");
    }

    [Command("baseclean cancel", Description = "Request cancellation",
        Permission = CommandPermission.Admin)]
    private void CleanupCancelCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in the world");
        CancelCleanup(c.Source.Player);
        c.Reply("Cancellation requested.");
    }
}
