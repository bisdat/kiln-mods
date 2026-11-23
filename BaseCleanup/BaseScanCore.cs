// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Kiln;

namespace Bisdat.BaseCleanup;

/// <summary>
/// Stage 1 counts ALL filled cells in one altar's 3-D zone, including
/// buried deposits and free-standing structures. No surface/exposure checks.
/// </summary>
internal static class BaseScanCore
{
    // Exact material IDs established by Rubble Remover 0.1.4 research.
    internal static readonly byte[] PlaceableRubble = [8, 64, 66, 69, 74, 81, 87, 102, 107];
    internal static readonly byte[] WorldRubble = [23, 36, 43, 44, 61, 62, 92];
    internal static readonly byte[] MineableMycelium = [14, 33, 65, 72];
    // Experimentally identified in creek/ditch puddle material probe.
    // Removes voxel TERRAIN, not merely its visible surface effect.
    // Underwater Mud (91) is deliberately NOT included in this test.
    internal const byte MudTerrain = 37;

    internal static bool IsRubble(int material) =>
        Array.IndexOf(PlaceableRubble, (byte)material) >= 0 ||
        Array.IndexOf(WorldRubble, (byte)material) >= 0;
    internal static bool IsMycelium(int material) =>
        Array.IndexOf(MineableMycelium, (byte)material) >= 0;

    // Select the voxel centres contained within the actual altar build zone,
    // not a guessed circular radius. Clamp to the readable voxel grid.
    internal static CellBox ZoneCells(PlayerBase zone, VoxelGrid grid)
    {
        if (grid.CellSize <= 0 || !double.IsFinite(grid.CellSize))
            throw new InvalidOperationException("the voxel grid has no usable cell size");
        var step = grid.CellSize;
        var x0 = CellMin(zone.Center.X - zone.Half.X, grid.Origin.X, step, grid.CellsX);
        var x1 = CellEnd(zone.Center.X + zone.Half.X, grid.Origin.X, step, grid.CellsX);
        var y0 = CellMin(zone.Center.Y - zone.Half.Y, grid.Origin.Y, step, 8192);
        var y1 = CellEnd(zone.Center.Y + zone.Half.Y, grid.Origin.Y, step, 8192);
        var z0 = CellMin(zone.Center.Z - zone.Half.Z, grid.Origin.Z, step, grid.CellsZ);
        var z1 = CellEnd(zone.Center.Z + zone.Half.Z, grid.Origin.Z, step, grid.CellsZ);
        return new CellBox(x0, y0, z0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0), Math.Max(0, z1 - z0));
    }

    private static int CellMin(double coordinate, double origin, double cell, int limit) =>
        (int)Math.Clamp(Math.Ceiling((coordinate - origin) / cell - 0.5 - 1e-9), 0, limit);

    private static int CellEnd(double coordinate, double origin, double cell, int limit) =>
        (int)Math.Clamp(Math.Floor((coordinate - origin) / cell - 0.5 + 1e-9) + 1, 0, limit);

    // Each read is at most 8192 voxels, deliberately bounded.
    internal static IEnumerable<CellBox> Batches(CellBox zone)
    {
        const int nx = 32, ny = 16, nz = 16;
        for (var z = zone.Z; z < zone.End.Z; z += nz)
        for (var y = zone.Y; y < zone.End.Y; y += ny)
        for (var x = zone.X; x < zone.End.X; x += nx)
            yield return new CellBox(x, y, z,
                Math.Min(nx, zone.End.X - x),
                Math.Min(ny, zone.End.Y - y),
                Math.Min(nz, zone.End.Z - z));
    }
}

internal sealed class VoxelInventory
{
    // Zero-fill (air) retains stale material bytes and must be excluded.
    internal readonly long[] Count = new long[256];
    internal readonly long[] FillSum = new long[256];
    internal long AirCells;
    internal long PartialCells;
    internal long ReadCells;

    internal void Add(ReadOnlySpan<Voxel> voxels)
    {
        foreach (var v in voxels)
        {
            ReadCells++;
            if (v.IsAir)
            {
                AirCells++;
                continue;
            }
            Count[v.Material]++;
            FillSum[v.Material] += v.Fill;
            if (!v.IsSolid) PartialCells++;
        }
    }

    internal long Sum(params byte[] materialIds)
    {
        long count = 0;
        foreach (var m in materialIds) count += Count[m];
        return count;
    }
}
