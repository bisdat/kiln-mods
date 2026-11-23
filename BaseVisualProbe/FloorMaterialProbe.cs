// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiln;

namespace Bisdat.BaseVisualProbe;

/// <summary>
/// Bounded per-player floor-material sample returned by the host. No request
/// accepts coordinates or a material identifier; the host uses the player's
/// authoritative position and reads only the current altar.
/// </summary>
public sealed record FloorMaterialReport(
    byte Mode, long Read, long Air, long Terrain, long Buildings,
    long PartialTerrain, long TopTerrain, long TopBuilding, long NoFloor,
    int? CenterMaterial, int? CenterFill, int? CenterFloorDy,
    string[] Materials, string[] Raw, string[] Layers, string[] Near)
    : INetMessage<FloorMaterialReport>
{
    public void Encode(NetWriter w)
    {
        w.Byte(Mode).Int64(Read).Int64(Air).Int64(Terrain).Int64(Buildings)
            .Int64(PartialTerrain).Int64(TopTerrain).Int64(TopBuilding).Int64(NoFloor)
            .Int32(CenterMaterial ?? -1).Int32(CenterFill ?? -1).Int32(CenterFloorDy ?? 999);
        EncodeLines(w, Materials);
        EncodeLines(w, Raw);
        EncodeLines(w, Layers);
        EncodeLines(w, Near);
    }

    private static void EncodeLines(NetWriter w, string[] lines)
    {
        w.Byte(checked((byte)lines.Length));
        foreach (var line in lines) w.String(line);
    }

    private static string[] DecodeLines(ref NetReader r, int maximum)
    {
        var count = r.Byte();
        if (count > maximum)
            throw new InvalidOperationException("invalid diagnostic line count");
        var result = new string[count];
        for (var i = 0; i < count; i++) result[i] = r.String(200) ?? "";
        return result;
    }

    public static FloorMaterialReport Decode(ref NetReader r)
    {
        var mode = r.Byte();
        var read = r.Int64();
        var air = r.Int64();
        var terrain = r.Int64();
        var buildings = r.Int64();
        var partial = r.Int64();
        var topTerrain = r.Int64();
        var topBuilding = r.Int64();
        var noFloor = r.Int64();
        var material = r.Int32();
        var fill = r.Int32();
        var dy = r.Int32();
        var materials = DecodeLines(ref r, 12);
        var raw = DecodeLines(ref r, 12);
        var layers = DecodeLines(ref r, 14);
        var near = DecodeLines(ref r, 9);
        return new FloorMaterialReport(mode, read, air, terrain, buildings, partial,
            topTerrain, topBuilding, noFloor, material < 0 ? null : material,
            fill < 0 ? null : fill, dy == 999 ? null : dy,
            materials, raw, layers, near);
    }
}

public sealed partial class BaseVisualProbeMod
{
    private FloorMaterialProfile? _puddleFloor;
    private FloorMaterialProfile? _dryFloor;
    private FloorMaterialReport? _puddleFloorView;
    private FloorMaterialReport? _dryFloorView;

    // 8: user stands on visible puddle or mud patch;
    // 9: user stands on visually dry floor, preferably ~1–5 metres away.
    // Labels represent user observation, NOT automatic visual recognition.
    private void StartFloorSample(EntityId player, byte mode)
    {
        if (!Ready(player)) return;
        if (!TryArea(player, out var altar, out var zone, out var here, out var error))
        {
            Send(player, new VisualProgress("failed", error, 0, 0));
            return;
        }
        if (Build.Grid is not { } grid || grid.CellSize <= 0)
        {
            Send(player, new VisualProgress("failed", "Voxel grid not ready.", 0, 0));
            return;
        }

        var feet = Build.CellOf(here);
        // At 0.5 m cells this is a 3.5 m square around the character,
        // looking from ~5.5 m below to ~1 m above. Clip to the altar.
        // The profile reports the exact dy and 16-bit raw bytes, NOT a
        // claim that the player's world position is precisely at their feet.
        var nominal = new CellBox(feet.X - 3, feet.Y - 11, feet.Z - 3, 7, 14, 7);
        var box = VisualBits.Intersect(zone, nominal);
        if (box.IsEmpty || box.Count > 1024 ||
            !box.Contains(feet) || box.SizeX < 3 || box.SizeZ < 3)
        {
            Send(player, new VisualProgress("failed",
                "No usable floor sample inside this altar. Move away from the zone edge.", 0, 0));
            return;
        }

        _busy = true;
        _cancel = false;
        _ = FloorSampleAsync(player, altar!, Saves.WorldId!, mode, feet, here, box, _epoch);
    }

    private async Task FloorSampleAsync(EntityId player, PlayerBase altar, string world,
        byte mode, CellPos feet, WorldPosition location, CellBox box, int epoch)
    {
        var label = mode == 8 ? "PUDDLE" : "DRY";
        try
        {
            Send(player, new VisualProgress("floor",
                $"Reading {label} floor sample ({box.Count:N0} cells), no edits.", 0, box.Count));
            if (!SameSession(altar, world, epoch))
                throw new OperationCanceledException("floor sample cancelled or world changed");
            if (!await Build.Load(box, TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("floor sample voxel tile did not load");
            if (!SameSession(altar, world, epoch))
                throw new OperationCanceledException("floor sample cancelled or world changed");

            var voxels = Build.Read(box);
            var cells = new Dictionary<CellPos, Voxel>(voxels.Length);
            foreach (var (cell, voxel) in VisualBits.Enumerate(box, voxels))
                cells.Add(cell, voxel);
            var p = FloorMaterialProfile.Analyse(mode, world, altar.Altar, box, feet, cells);
            if (mode == 8) _puddleFloor = p; else _dryFloor = p;

            Log.Info($"base-visual-probe FLOOR {label} world={world} altar={altar.Altar.Value} " +
                $"position={location}, playerCell={feet}, box={box}, cellSize={Build.Grid!.CellSize} " +
                $"read={p.Read} terrain={p.Terrain} building={p.Building} air={p.Air} " +
                $"partialTerrain={p.TerrainPartial} topTerrain={p.TopTerrain} " +
                $"topBuilding={p.TopBuilding} noFloor={p.NoFloor} " +
                $"centerFloorRaw={(p.CenterFloorRaw.HasValue ? $"0x{p.CenterFloorRaw.Value:X4}" : "NONE")} " +
                $"centerFloorDy={p.CenterFloorDy?.ToString() ?? "none"}; READ ONLY");
            foreach (var line in p.TopMaterials()) Log.Info($"base-visual-probe FLOOR {label} TOP-MATERIAL " + line);
            foreach (var line in p.TopRawValues()) Log.Info($"base-visual-probe FLOOR {label} TOP-RAW " + line);
            foreach (var line in p.AllMaterials()) Log.Info($"base-visual-probe FLOOR {label} ALL-MATERIAL " + line);
            foreach (var line in p.CenterLayers) Log.Info($"base-visual-probe FLOOR {label} CENTER-Y " + line);
            foreach (var line in p.NearColumns) Log.Info($"base-visual-probe FLOOR {label} CENTER-3x3 " + line);

            var other = mode == 8 ? _dryFloor : _puddleFloor;
            if (other is not null && other.WorldId == p.WorldId && other.Altar == p.Altar)
            {
                var wet = mode == 8 ? p : other;
                var dry = mode == 9 ? p : other;
                Log.Info($"base-visual-probe FLOOR-PAIR same world+altar: PUDDLE " +
                    $"centerRaw={(wet.CenterFloorRaw.HasValue ? $"0x{wet.CenterFloorRaw.Value:X4}" : "none")} " +
                    $"dy={wet.CenterFloorDy?.ToString() ?? "none"}; DRY " +
                    $"centerRaw={(dry.CenterFloorRaw.HasValue ? $"0x{dry.CenterFloorRaw.Value:X4}" : "none")} " +
                    $"dy={dry.CenterFloorDy?.ToString() ?? "none"}. " +
                    "Compare raw voxel values and floor materials; identical material does NOT prove a decal.");
                foreach (var mat in wet.TopMaterial.Keys.Union(dry.TopMaterial.Keys).OrderBy(x => x))
                {
                    var wc = wet.TopMaterial.GetValueOrDefault(mat);
                    var dc = dry.TopMaterial.GetValueOrDefault(mat);
                    Log.Info($"base-visual-probe FLOOR-PAIR mat={mat}: puddleTop={wc}, dryTop={dc} (different sites)");
                }
            }

            var center = p.CenterFloorRaw is { } raw ? new Voxel(raw) : (Voxel?)null;
            Send(player, new FloorMaterialReport(mode, p.Read, p.Air, p.Terrain, p.Building,
                p.TerrainPartial, p.TopTerrain, p.TopBuilding, p.NoFloor,
                center?.Material, center?.Fill, p.CenterFloorDy,
                p.TopMaterials(), p.TopRawValues(),
                p.CenterLayers.ToArray(), p.NearColumns.ToArray()));
            Send(player, new VisualProgress("floor-complete",
                $"{label}: {p.TopTerrain:N0} terrain + {p.TopBuilding:N0} building floor columns " +
                $"({p.NoFloor:N0} with no floor); exact raw values in kiln.log. NO EDITS.",
                p.Read, p.Read));
        }
        catch (Exception ex)
        {
            Log.Warn($"base-visual-probe FLOOR {label} failed, no edits: " + ex);
            if (epoch == _epoch)
                Send(player, new VisualProgress(_cancel ? "cancelled" : "failed",
                    "Floor sample not recorded: " + ex.Message, 0, box.Count));
        }
        finally { if (epoch == _epoch) _busy = false; }
    }

    private void Send(EntityId player, FloorMaterialReport report)
    {
        if (Client.IsHosting && Client.HasLocalPlayer && player == Client.LocalPlayer)
            Client.Post(() => Receive(report));
        else _network?.SendTo(player, report);
    }

    private void Receive(FloorMaterialReport report)
    {
        if (report.Mode == 8) Volatile.Write(ref _puddleFloorView, report);
        else if (report.Mode == 9) Volatile.Write(ref _dryFloorView, report);
    }

    private void DrawFloorProbe(UiDraw ui)
    {
        ui.Separator("Floor material / puddle comparison — READ ONLY");
        ui.TextWrapped("Stand ON one visible puddle and sample A. Move to DRY floor " +
            "nearby (same structure if possible) and sample B. No digging or full-altar scan.");
        if (ui.Button("A. Sample PUDDLE floor")) Client.Post(() => Request(8));
        ui.SameLine();
        if (ui.Button("B. Sample DRY floor")) Client.Post(() => Request(9));

        var wet = Volatile.Read(ref _puddleFloorView);
        var dry = Volatile.Read(ref _dryFloorView);
        if (wet is not null) DrawFloorReport(ui, "A: PUDDLE", wet);
        if (dry is not null) DrawFloorReport(ui, "B: DRY", dry);

        if (wet is not null && dry is not null)
        {
            ui.TextWrapped($"Center floor: PUDDLE material={wet.CenterMaterial?.ToString() ?? "none"} " +
                $"fill={wet.CenterFill?.ToString() ?? "?"}, " +
                $"DRY material={dry.CenterMaterial?.ToString() ?? "none"} " +
                $"fill={dry.CenterFill?.ToString() ?? "?"}.");
            ui.TextDisabled("Same material does not prove a decal; different material is not automatically safe to remove.");
        }
        ui.TextDisabled("A/B are 7×7-column local observations. Exact 16-bit values and Y layers go into kiln.log.");
    }

    private static void DrawFloorReport(UiDraw ui, string title, FloorMaterialReport p)
    {
        ui.Text($"{title}: {p.Read:N0} cells; terrain={p.Terrain:N0}, " +
            $"buildings={p.Buildings:N0}, air={p.Air:N0}; partial terrain={p.PartialTerrain:N0}");
        ui.Text($"Floor columns: terrain={p.TopTerrain:N0}; " +
            $"buildings={p.TopBuilding:N0}; none={p.NoFloor:N0}");
        ui.Text($"Center: material={p.CenterMaterial?.ToString() ?? "none"}, " +
            $"fill/paint={p.CenterFill?.ToString() ?? "none"}, " +
            $"dy={p.CenterFloorDy?.ToString() ?? "none"}");
        if (ui.BeginChild(p.Mode == 8 ? "floor-wet-raw" : "floor-dry-raw", 0, 82 * ui.Scale))
        {
            foreach (var line in p.Raw.Take(4)) ui.TextWrapped(line);
        }
        ui.EndChild();
    }

    [Command("vprobe floorwet", Description = "Read-only terrain/building voxels under a visible puddle",
        Permission = CommandPermission.Admin)]
    private void FloorWetCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone)
            throw new CommandException("run as a player in a live world");
        StartFloorSample(c.Source.Player, 8);
        c.Reply("Requested small READ-ONLY PUDDLE floor sample at player's position.");
    }

    [Command("vprobe floordry", Description = "Read-only control sample of nearby dry floor",
        Permission = CommandPermission.Admin)]
    private void FloorDryCommand(CommandContext c)
    {
        if (c.Source.Player.IsNone)
            throw new CommandException("run as a player in a live world");
        StartFloorSample(c.Source.Player, 9);
        c.Reply("Requested small READ-ONLY DRY floor sample at player's position.");
    }

    private void ResetFloorProbe()
    {
        _puddleFloor = null;
        _dryFloor = null;
        if (Client.Available && Client.IsClient)
            Client.Post(() => { _puddleFloorView = null; _dryFloorView = null; });
    }
}
