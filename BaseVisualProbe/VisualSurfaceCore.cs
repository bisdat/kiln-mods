// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Kiln;

namespace Bisdat.BaseVisualProbe;

/// <summary>
/// Small-spot read-only metadata profile. "Exposed" is an approximation:
/// a BUILDING cell with a six-neighbour adjacent AIR cell, provided ALL
/// six neighbours lie within the captured sample. It does NOT mean that
/// the game rendered ivy on that cell. Do not use this classification as
/// a destructive cleanup selection rule.
/// </summary>
internal sealed class VisualSurfaceProfile
{
    internal readonly byte Mode;
    internal readonly string WorldId;
    internal readonly EntityId Altar;
    internal readonly CellBox Box;
    internal long Read, Buildings, ZeroPaint, BorderUnknown, Enclosed, Exposed;
    internal long Growth7All, Growth1All, Growth7Exposed, Growth1Exposed;
    internal long WeatheredExposed, Material138Exposed;
    internal readonly long[] LevelsAll = new long[8];
    internal readonly long[] LevelsExposed = new long[8];
    internal readonly Dictionary<ushort, long> RawAll = new();
    internal readonly Dictionary<ushort, long> RawExposed = new();

    private VisualSurfaceProfile(byte mode, string world, EntityId altar, CellBox box)
    {
        Mode = mode; WorldId = world; Altar = altar; Box = box;
    }

    internal static VisualSurfaceProfile Analyse(
        byte mode, string world, EntityId altar, CellBox box,
        IReadOnlyDictionary<CellPos, Voxel> cells)
    {
        if (mode is not (6 or 7) || cells.Count != box.Count)
            throw new InvalidOperationException("incomplete or invalid visual surface sample");

        var p = new VisualSurfaceProfile(mode, world, altar, box) { Read = cells.Count };
        var offsets = new[] {
            new CellPos(1, 0, 0), new CellPos(-1, 0, 0),
            new CellPos(0, 1, 0), new CellPos(0, -1, 0),
            new CellPos(0, 0, 1), new CellPos(0, 0, -1),
        };

        foreach (var (pos, voxel) in cells)
        {
            if (!VisualBits.IsBuilding(voxel)) continue;
            p.Buildings++;
            if (voxel.Fill == 0) p.ZeroPaint++;
            var level = VisualBits.Growth(voxel);
            p.LevelsAll[level]++;
            if (level == 7) p.Growth7All++;
            if (level == 1) p.Growth1All++;
            Increment(p.RawAll, voxel.Raw);

            var hasAllNeighbours = true;
            var airNeighbour = false;
            foreach (var offset in offsets)
            {
                var adjacent = pos + offset;
                if (!cells.TryGetValue(adjacent, out var neighbour))
                {
                    hasAllNeighbours = false;
                    break;
                }
                // Terrain with 0 fill is air. Buildings with 0 paint
                // are NOT air, so check material first.
                if (!VisualBits.IsBuilding(neighbour) && neighbour.Fill == 0)
                    airNeighbour = true;
            }
            if (!hasAllNeighbours)
            {
                p.BorderUnknown++;
                continue;
            }
            if (!airNeighbour)
            {
                p.Enclosed++;
                continue;
            }

            p.Exposed++;
            p.LevelsExposed[level]++;
            if (level == 7) p.Growth7Exposed++;
            if (level == 1) p.Growth1Exposed++;
            if (VisualBits.Weather(voxel)) p.WeatheredExposed++;
            if (voxel.Material == 138) p.Material138Exposed++;
            Increment(p.RawExposed, voxel.Raw);
        }
        if (p.Buildings != p.BorderUnknown + p.Enclosed + p.Exposed)
            throw new InvalidOperationException("surface sample classification did not cover all buildings");
        return p;
    }

    private static void Increment(Dictionary<ushort, long> histogram, ushort raw) =>
        histogram[raw] = histogram.TryGetValue(raw, out var n) ? n + 1 : 1;

    internal string[] TopRawExposed(int limit = 10) =>
        RawExposed.OrderByDescending(x => x.Value).ThenBy(x => x.Key)
            .Take(limit).Select(x =>
            {
                var v = new Voxel(x.Key);
                return $"0x{x.Key:X4} (mat {v.Material}, growth {VisualBits.Growth(v)}, " +
                    $"weather {(VisualBits.Weather(v) ? 1 : 0)}, low {VisualBits.Tint(v)}): {x.Value:N0}";
            }).ToArray();

    internal string LevelsString(long[] levels) =>
        string.Join(", ", Enumerable.Range(0, 8).Select(i => $"{i}={levels[i]:N0}"));

    internal static string Percent(long count, long total) =>
        total == 0 ? "n/a" : $"{100d * count / total:F1}%";
}
