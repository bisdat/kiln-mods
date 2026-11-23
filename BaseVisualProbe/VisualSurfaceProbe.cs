// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiln;

namespace Bisdat.BaseVisualProbe;

/// <summary>
/// Tiny, observational "clean wall" versus "ivy wall" surface samples.
/// This is NOT a detector of ivy: the labels are chosen by the player
/// from what they can actually see, and exposure is only six-neighbour air.
/// </summary>
public sealed record VisualSurfaceReport(
    byte Mode, long Read, long Buildings, long ZeroPaint,
    long BorderUnknown, long Enclosed, long Exposed,
    long Growth7All, long Growth1All,
    long Growth7Exposed, long Growth1Exposed,
    long WeatheredExposed, long Material138Exposed,
    string[] TopRaw) : INetMessage<VisualSurfaceReport>
{
    public void Encode(NetWriter w)
    {
        w.Byte(Mode).Int64(Read).Int64(Buildings).Int64(ZeroPaint)
            .Int64(BorderUnknown).Int64(Enclosed).Int64(Exposed)
            .Int64(Growth7All).Int64(Growth1All)
            .Int64(Growth7Exposed).Int64(Growth1Exposed)
            .Int64(WeatheredExposed).Int64(Material138Exposed)
            .Byte(checked((byte)TopRaw.Length));
        foreach (var line in TopRaw) w.String(line);
    }

    public static VisualSurfaceReport Decode(ref NetReader r)
    {
        var mode = r.Byte();
        var read = r.Int64();
        var buildings = r.Int64();
        var zero = r.Int64();
        var border = r.Int64();
        var enclosed = r.Int64();
        var exposed = r.Int64();
        var level7 = r.Int64();
        var level1 = r.Int64();
        var exposed7 = r.Int64();
        var exposed1 = r.Int64();
        var weather = r.Int64();
        var m138 = r.Int64();
        var n = Math.Min(10, (int)r.Byte());
        var top = new string[n];
        for (var i = 0; i < n; i++) top[i] = r.String(200) ?? "";
        return new(mode, read, buildings, zero, border, enclosed, exposed,
            level7, level1, exposed7, exposed1, weather, m138, top);
    }
}

public sealed partial class BaseVisualProbeMod
{
    private VisualSurfaceProfile? _cleanSpot;
    private VisualSurfaceProfile? _ivySpot;
    private VisualSurfaceReport? _cleanSpotView;
    private VisualSurfaceReport? _ivySpotView;

    // 6 = visually clean, 7 = visibly overgrown. The player chooses the
    // visual label; this program never claims to infer visible ivy from
    // raw voxel bit flags.
    private void StartSpot(EntityId player, byte mode)
    {
        if (!Ready(player)) return;
        if (!TryArea(player, out var altar, out var area, out var here, out var reason))
        {
            Send(player, new VisualProgress("failed", reason, 0, 0));
            return;
        }

        // 5-metre cube (normally 11^3 cells), NOT another full altar scan.
        // It must be close to the actual wall/roof, because the sample
        // is centred on the player rather than their cursor target.
        var box = VisualBits.Intersect(area, Build.BoxAround(here, 2.5, 2.5, 2.5));
        if (box.IsEmpty || box.Count > 4096)
        {
            Send(player, new VisualProgress("failed",
                "Surface spot is empty or too large. Stand close to the wall.", 0, 0));
            return;
        }
        _busy = true; _cancel = false;
        _ = SpotAsync(player, altar!, box, Saves.WorldId!, _epoch, mode);
    }

    private async Task SpotAsync(EntityId player, PlayerBase altar,
        CellBox box, string world, int epoch, byte mode)
    {
        try
        {
            var side = mode == 6 ? "CLEAN" : "OVERGROWN";
            Send(player, new VisualProgress("spot",
                $"Reading nearby visually {side.ToLowerInvariant()} wall: {box.Count:N0} voxels (no edits).",
                0, box.Count));

            if (!SameSession(altar, world, epoch))
                throw new OperationCanceledException("spot cancelled / world or altar changed");
            if (!await Build.Load(box, TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("surface sample tile not loaded");
            if (!SameSession(altar, world, epoch))
                throw new OperationCanceledException("spot cancelled / world or altar changed");

            var read = Build.Read(box);
            var cells = new Dictionary<CellPos, Voxel>(read.Length);
            foreach (var (pos, voxel) in VisualBits.Enumerate(box, read))
                cells.Add(pos, voxel);

            var p = VisualSurfaceProfile.Analyse(mode, world, altar.Altar, box, cells);
            if (mode == 6) _cleanSpot = p; else _ivySpot = p;

            Log.Info($"base-visual-probe SPOT {side} world={world} altar={altar.Altar.Value} " +
                $"box={box}, read={p.Read}, buildings={p.Buildings}, zeroPaint={p.ZeroPaint}, " +
                $"boundaryUnknown={p.BorderUnknown}, enclosed={p.Enclosed}, exposed={p.Exposed}, " +
                $"growth7All={p.Growth7All}, growth1All={p.Growth1All}, " +
                $"growth7Exposed={p.Growth7Exposed}, growth1Exposed={p.Growth1Exposed}, " +
                $"weatherExposed={p.WeatheredExposed}, material138Exposed={p.Material138Exposed}; READ-ONLY");
            Log.Info($"base-visual-probe SPOT {side} ALL levels: {p.LevelsString(p.LevelsAll)}");
            Log.Info($"base-visual-probe SPOT {side} AIR-ADJACENT levels: {p.LevelsString(p.LevelsExposed)}");
            foreach (var line in p.TopRawExposed())
                Log.Info($"base-visual-probe SPOT {side} AIR-ADJACENT RAW " + line);

            var other = mode == 6 ? _ivySpot : _cleanSpot;
            if (other is not null &&
                other.WorldId == p.WorldId && other.Altar == p.Altar)
            {
                var clean = mode == 6 ? p : other;
                var ivy = mode == 7 ? p : other;
                Log.Info("base-visual-probe PAIR same world+altar, different physical locations: " +
                    $"CLEAN exposed={clean.Exposed}, level7={clean.Growth7Exposed} " +
                    $"({VisualSurfaceProfile.Percent(clean.Growth7Exposed, clean.Exposed)}), " +
                    $"level1={clean.Growth1Exposed}; " +
                    $"OVERGROWN exposed={ivy.Exposed}, level7={ivy.Growth7Exposed} " +
                    $"({VisualSurfaceProfile.Percent(ivy.Growth7Exposed, ivy.Exposed)}), " +
                    $"level1={ivy.Growth1Exposed}. " +
                    "These are counts per air-adjacent cell, not visible ivy coverage.");
            }

            Send(player, new VisualSurfaceReport(mode, p.Read, p.Buildings, p.ZeroPaint,
                p.BorderUnknown, p.Enclosed, p.Exposed, p.Growth7All, p.Growth1All,
                p.Growth7Exposed, p.Growth1Exposed, p.WeatheredExposed,
                p.Material138Exposed, p.TopRawExposed()));
            Send(player, new VisualProgress("spot-complete",
                $"{side} spot recorded: {p.Exposed:N0} air-adjacent building voxels, " +
                $"{p.Growth7Exposed:N0} with level 7 " +
                $"({VisualSurfaceProfile.Percent(p.Growth7Exposed, p.Exposed)}). No changes.",
                p.Read, p.Read));
        }
        catch (Exception e)
        {
            Log.Warn("base-visual-probe surface sample stopped (read-only): " + e);
            if (epoch == _epoch)
                Send(player, new VisualProgress(_cancel ? "cancelled" : "failed",
                    "Surface spot not saved: " + e.Message, 0, box.Count));
        }
        finally { if (epoch == _epoch) _busy = false; }
    }

    private void Send(EntityId player, VisualSurfaceReport p)
    {
        if (Client.IsHosting && Client.HasLocalPlayer && player == Client.LocalPlayer)
            Client.Post(() => Receive(p));
        else _network?.SendTo(player, p);
    }

    private void Receive(VisualSurfaceReport p)
    {
        if (p.Mode == 6) Volatile.Write(ref _cleanSpotView, p);
        else if (p.Mode == 7) Volatile.Write(ref _ivySpotView, p);
    }

    private void DrawSurfaceSpot(UiDraw ui)
    {
        ui.Separator("Local surface investigation — NO full altar scan");
        ui.TextWrapped("Stand close to a visibly CLEAN wall/roof and take reference A. " +
            "Then move close to a visibly IVY/OVERGROWN wall/roof and take reference B.");
        if (ui.Button("A. Sample CLEAN wall (~5 m cube)")) Client.Post(() => Request(6));
        if (ui.Button("B. Sample OVERGROWN wall (~5 m cube)")) Client.Post(() => Request(7));

        var a = Volatile.Read(ref _cleanSpotView);
        var b = Volatile.Read(ref _ivySpotView);
        if (a is not null) DrawSpotReport(ui, "Clean reference A", a);
        if (b is not null) DrawSpotReport(ui, "Overgrown reference B", b);

        if (a is not null && b is not null)
        {
            var clean = a.Exposed == 0 ? "n/a" :
                $"{100d * a.Growth7Exposed / a.Exposed:F1}%";
            var ivy = b.Exposed == 0 ? "n/a" :
                $"{100d * b.Growth7Exposed / b.Exposed:F1}%";
            ui.Text($"Level 7 among air-adjacent buildings: CLEAN {clean} vs OVERGROWN {ivy}");
            ui.TextDisabled("Only compare within the same altar; check kiln.log for labelled locations and exact raw values.");
        }
        ui.TextDisabled("Air-adjacent is a 6-neighbour approximation, NOT a guarantee the surface is visibly overgrown.");
    }

    private static void DrawSpotReport(UiDraw ui, string label, VisualSurfaceReport report)
    {
        ui.Text($"{label}: {report.Buildings:N0} buildings; air-adjacent {report.Exposed:N0}; " +
            $"unclassified boundary {report.BorderUnknown:N0}");
        ui.Text($"Level 7: {report.Growth7Exposed:N0} exposed / {report.Growth7All:N0} all; " +
            $"level 1 exposed {report.Growth1Exposed:N0}");
        if (ui.BeginChild(label == "Clean reference A" ? "spot-clean-raw" : "spot-ivy-raw", 0, 84 * ui.Scale))
        {
            foreach (var t in report.TopRaw.Take(4)) ui.Text(t);
        }
        ui.EndChild();
    }

    private void ResetSurfaceSpot()
    {
        _cleanSpot = null; _ivySpot = null;
        if (Client.Available && Client.IsClient)
            Client.Post(() => { _cleanSpotView = null; _ivySpotView = null; });
    }
}
