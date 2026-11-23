// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
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
    public void Encode(NetWriter w) => w.String(Phase).String(Message).Int64(Done).Int64(Total)
        .Int64(Rubble).Int64(Mud).Int64(Mycelium).String(Backup);
    public static CleanupStatus Decode(ref NetReader r) =>
        new(r.String(32) ?? "", r.String(512) ?? "", r.Int64(), r.Int64(),
            r.Int64(), r.Int64(), r.Int64(), r.String(160) ?? "");
}

/// <summary>
/// Stage 2 runs only on the authoritative server. A confirmed plan is
/// snapshotted in WorldData BEFORE Saves.Backup (which saves that journal).
/// Only after the backup succeeds are cells compare-and-written to air.
/// Undo reads the saved journal after any restart and compare-writes the
/// original raw voxel back if and only if the world still contains air.
/// </summary>
public sealed partial class BaseCleanupMod
{
    private const string LedgerKey = "cleanup-rollback-v1";
    private const string CleanupPermission = "base-cleanup.command.baseclean";
    private const string ConfirmRemove = "REMOVE";
    private const string ConfirmUndo = "UNDO";
    private const int PreviewMinutes = 10;

    private CleanupPlan? _preparedCleanup;
    private CleanupPlan? _activeCleanup;
    private bool _cleanupBusy;
    private bool _cleanupCancel;
    private int _cleanupEpoch;
    private CleanupStatus? _viewCleanup;
    private string _cleanupConfirm = "";
    private string _undoConfirm = "";
    private bool _includeMud = true;
    private bool _includeMycelium = true;

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
            case 2: BeginCleanup(player); break;
            case 3: BeginUndo(player); break;
            case 4: CancelCleanup(player); break;
            case 5: CleanupPublish(player, CleanupCurrentStatus()); break;
            default: CleanupPublish(player, new("failed", "Unknown cleanup action.", 0, 0, 0, 0, 0, "")); break;
        }
    }

    private CleanupStatus CleanupCurrentStatus()
    {
        if (_cleanupBusy && _activeCleanup is { } active)
            return new("working", "Preparing or applying a cleanup; see progress updates.",
                active.Read, active.Total, active.Hits, 0, 0, "");
        if (_preparedCleanup is { } p)
            return PlanStatus(p, "prepared", "Plan ready. Inspect the counts and type REMOVE to confirm.");
        if (Saves.Available && Saves.WorldDataReady &&
            WorldData.TryGet<CleanupLedger>(LedgerKey, out var l) && l.Packed.Length != 0)
            return new(l.Phase, $"Saved undo journal: {l.Planned:N0} voxels; removed {l.Removed:N0}; " +
                $"restored {l.Restored:N0}; conflicts {l.Conflicted:N0}. Select UNDO to restore unchanged air cells.",
                l.Removed + l.Restored, l.Planned, 0, 0, 0, l.BackupName);
        return new("idle", "No cleanup prepared. Stage 1 scan remains available above.", 0, 0, 0, 0, 0, "");
    }

    private static CleanupStatus PlanStatus(CleanupPlan p, string phase, string message) =>
        new(phase, message, p.Read, p.Total,
            BaseScanCore.PlaceableRubble.Sum(x => p.Materials[x]) + BaseScanCore.WorldRubble.Sum(x => p.Materials[x]),
            p.Materials[BaseScanCore.UnderwaterMud],
            BaseScanCore.MineableMycelium.Sum(x => p.Materials[x]), "");

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

    private bool HasOutstandingLedger(out string message)
    {
        message = "";
        if (WorldData.TryGet<CleanupLedger>(LedgerKey, out var journal) &&
            journal.Packed.Length != 0)
        {
            message = $"A cleanup journal already exists ({journal.Phase}; backup {journal.BackupName}). " +
                      "Use Undo instead of overwriting that recovery record.";
            return true;
        }
        return false;
    }

    private void BeginPreparation(EntityId player, byte options)
    {
        if ((options & ~CleanupCore.AllowedOptions) != 0)
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
        if (HasOutstandingLedger(out reason))
        {
            CleanupPublish(player, new("failed", reason, 0, 0, 0, 0, 0, ""));
            return;
        }
        if (AltarAt(player) is not { } altar || Build.Grid is not { } grid)
        {
            CleanupPublish(player, new("failed", "Stand inside the Flame Altar to prepare its cleanup.", 0, 0, 0, 0, 0, ""));
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
                $"Plan complete: {plan.Hits:N0} cells across {plan.Chunks.Count:N0} batches. " +
                "No edits yet. Review counts, then type REMOVE to back up and commit."));
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

    private void BeginCleanup(EntityId player)
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
        if (!SafeServices(out var reason) || !SameZone(p) ||
            AltarAt(player) is not { } current || current.Altar != p.Altar.Altar)
        {
            CleanupPublish(player, new("failed", reason.Length > 0 ? reason :
                "The altar, world or player location has changed. Prepare again.", 0, 0, 0, 0, 0, ""));
            return;
        }
        if (HasOutstandingLedger(out reason))
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
        var backupCompleted = false;
        try
        {
            var name = "base-cleanup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            journal = CleanupSafety.Ledger(plan, name);
            // The entire original voxel journal is included in a real
            // game SAVE before the first write, and in the backup sidecar.
            WorldData.Set(LedgerKey, journal);
            CleanupPublish(plan.Player, PlanStatus(plan, "backup",
                "Saving undo journal and creating a full-world backup BEFORE the first voxel edit."));
            var backup = await Saves.Backup(name);
            if (StopRequested(plan)) throw new OperationCanceledException("cancelled during backup");
            if (backup.WorldId != plan.WorldId || backup.Files == 0)
                throw new InvalidOperationException("backup did not confirm matching world data");
            backupCompleted = true;
            journal.BackupName = backup.Name;
            journal.Phase = "applying";
            CleanupPublish(plan.Player, new("applying",
                $"World backup {backup.Name} verified; removing selected materials. Cancel remains available.",
                0, plan.Hits, 0, 0, 0, backup.Name));

            var completed = 0L;
            var conflicts = 0L;
            var chunks = 0;
            var options = new BuildOptions
            {
                Flags = BuildFlags.InBases | BuildFlags.RespectZones,
                Actor = plan.Player,
                Journal = false, // Persistent WorldData journal, not 64-entry session history.
                Label = "Base Cleanup Stage 2 rubble/mud",
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
                    desired[cell.Index] = Voxel.Air;
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
                        $"Removal: processed {completed:N0}/{plan.Hits:N0} cells; removed {journal.Removed:N0}, " +
                        $"skipped {conflicts:N0}. No surface/support checks.",
                        completed, plan.Hits, 0, 0, 0, journal.BackupName));
                await Context.Scheduler.NextTick();
            }

            journal.Phase = StopRequested(plan) || conflicts > 0 ? "partial" : "complete";
            CleanupPublish(plan.Player, new(journal.Phase,
                $"{(StopRequested(plan) ? "Stopped" : "Finished")}: removed {journal.Removed:N0}; " +
                $"skipped {conflicts:N0}. Backup {journal.BackupName}. " +
                "Type UNDO to restore matching air cells from the persistent journal.",
                completed, plan.Hits, 0, 0, 0, journal.BackupName));
            Log.Info($"base-cleanup: removal {journal.Phase}; removed={journal.Removed}, conflicts={conflicts}, backup={journal.BackupName}");
        }
        catch (Exception ex)
        {
            Log.Warn("base-cleanup: apply stopped: " + ex);
            if (journal is not null)
            {
                if (!backupCompleted)
                {
                    // No writes were permitted without a successful backup.
                    WorldData.Remove(LedgerKey);
                }
                else
                {
                    journal.Phase = "partial";
                }
            }
            if (plan.Generation == _cleanupEpoch)
                CleanupPublish(plan.Player, PlanStatus(plan, "failed",
                    (backupCompleted ? "Cleanup interrupted; saved journal supports UNDO. " :
                        "Backup failed; NO VOXELS CHANGED. ") + ex.Message));
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
        if (!SafeServices(out var reason) ||
            !WorldData.TryGet<CleanupLedger>(LedgerKey, out var journal) ||
            string.IsNullOrEmpty(journal.Packed))
        {
            CleanupPublish(player, new("failed", reason.Length != 0 ? reason :
                "No stored cleanup journal for this world.", 0, 0, 0, 0, 0, ""));
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
            CleanupPublish(player, new("undoing", $"Selective undo from backup journal {journal.BackupName}...",
                0, journal.Planned, 0, 0, 0, journal.BackupName));
            var options = new BuildOptions
            {
                Flags = BuildFlags.InBases | BuildFlags.RespectZones,
                Actor = player, Journal = false, Label = "Base Cleanup Stage 2 undo",
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
                    if (expected[cell.Index].Raw != Voxel.Air.Raw)
                    {
                        // A player changed it, or it was already put back.
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
            journal.Phase = _cleanupCancel || generation != _cleanupEpoch ? "partial" : "undone";
            CleanupPublish(player, new(journal.Phase,
                $"Undo: restored {restored:N0} voxels; left {conflicts:N0} non-air/already-restored cells unchanged. " +
                $"World backup remains available: {journal.BackupName}.",
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
        ui.Separator("STAGE 2 - CONFIRMED RUBBLE REMOVAL");
        ui.TextWrapped("Warning: this is a permanent world edit until undone. ALL matching rubble " +
            "voxels are removed anywhere in the altar volume, including buried rubble. " +
            "There are no structural or surface-exposure filters.");
        ui.Checkbox("Include underwater mud (material 91)", ref _includeMud);
        ui.Checkbox("Include mineable mycelium", ref _includeMycelium);
        ui.TextDisabled("Overgrowth/decay are NOT supported yet.");
        var flags = (byte)((_includeMud ? CleanupCore.IncludeMud : 0) |
                           (_includeMycelium ? CleanupCore.IncludeMycelium : 0));
        if (ui.Button("1. Prepare removal plan (read-only)"))
            Client.Post(() => CleanupRequestLocal(1, flags));
        ui.SameLine();
        if (ui.Button("Cancel")) Client.Post(() => CleanupRequestLocal(4));
        ui.TextWrapped("A FULL-WORLD backup and persistent selective-undo journal are REQUIRED " +
            "before any removal. The backup may roll the entire world back if restored later.");
        ui.InputText("Type REMOVE to commit", ref _cleanupConfirm, 20);
        if (ui.Button("2. CONFIRM: save backup and remove"))
        {
            if (_cleanupConfirm == ConfirmRemove)
            {
                _cleanupConfirm = "";
                Client.Post(() => CleanupRequestLocal(2));
            }
            else Hud.Toast("Type REMOVE exactly before confirming.", ToastKind.Warning);
        }
        ui.InputText("Type UNDO to restore", ref _undoConfirm, 20);
        if (ui.Button("Undo previous cleanup"))
        {
            if (_undoConfirm == ConfirmUndo)
            {
                _undoConfirm = "";
                Client.Post(() => CleanupRequestLocal(3));
            }
            else Hud.Toast("Type UNDO exactly before reverting.", ToastKind.Warning);
        }
        var status = Volatile.Read(ref _viewCleanup);
        if (status is null) return;
        ui.TextWrapped($"Cleanup [{status.Phase}]: {status.Message}");
        if (status.Total > 0)
            ui.Progress((float)(status.Done / (double)status.Total),
                $"{status.Done:N0} / {status.Total:N0} cells");
        if (status.Phase is "prepared" or "planning")
            ui.Text($"Selected rubble {status.Rubble:N0}; mud {status.Mud:N0}; mycelium {status.Mycelium:N0}");
        if (!string.IsNullOrEmpty(status.Backup))
            ui.TextWrapped("Full-world backup: " + status.Backup);
    }

    private void CleanupStopped()
    {
        _cleanupEpoch++;
        _cleanupCancel = true;
        _preparedCleanup = null;
        _activeCleanup = null;
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
            CleanupPublish(Client.LocalPlayer, CleanupCurrentStatus());
    }

    [Command("baseclean prepare", Description = "Read-only plan of rubble/mud/mycelium removal inside current Flame Altar",
        Permission = CommandPermission.Admin)]
    private void CleanupPrepareCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in the world");
        BeginPreparation(c.Source.Player, CleanupCore.AllowedOptions);
        c.Reply("Preparing read-only removal plan; inspect the Base Cleanup UI or /baseclean status.");
    }

    [Command("baseclean confirm", Description = "Explicitly confirm backup and cleanup (requires REMOVE)",
        Permission = CommandPermission.Admin)]
    private void CleanupConfirmCommand(CommandContext c, string confirmation)
    {
        if (confirmation != ConfirmRemove) throw new CommandException("type /baseclean confirm REMOVE");
        if (c.Source.Player.IsNone) throw new CommandException("run as a player in the world");
        BeginCleanup(c.Source.Player);
        c.Reply("Confirmation requested; backup must complete before removal starts.");
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
        var s = CleanupCurrentStatus();
        c.Reply($"[{s.Phase}] {s.Message}");
        c.Reply($"rubble={s.Rubble} mud={s.Mud} mycelium={s.Mycelium} backup={s.Backup}");
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
