// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Kiln;

namespace Bisdat.BaseVisualProbe;

/// <summary>
/// Read-only sampling of a 7x7 horizontal footprint around the player's
/// feet, from up to 11 cells BELOW to 2 cells ABOVE that position.
/// A "top" column cell is the highest OCCUPIED voxel at or below the
/// player's sampled Y. This is a voxel approximation, NOT a render/decal
/// detector or a safe list of materials to remove.
/// </summary>
internal sealed class FloorMaterialProfile
{
    internal readonly byte Mode;
    internal readonly string WorldId;
    internal readonly EntityId Altar;
    internal readonly CellBox Box;
    internal readonly CellPos Center;
    internal readonly Dictionary<byte, long> AllMaterial = new();
    internal readonly Dictionary<byte, long> TopMaterial = new();
    internal readonly Dictionary<ushort, long> TopRaw = new();
    internal readonly List<string> CenterLayers = new();
    internal readonly List<string> NearColumns = new();
    internal long Read, Terrain, Building, Air, TerrainPartial, TopTerrain, TopBuilding, NoFloor;
    internal ushort? CenterFloorRaw;
    internal int? CenterFloorDy;

    private FloorMaterialProfile(byte mode, string world, EntityId altar, CellBox box, CellPos center)
    {
        Mode = mode; WorldId = world; Altar = altar; Box = box; Center = center;
    }

    // A zero-paint building voxel is occupied. Zero-fill terrain and material 0
    // are not surfaces. This is deliberately NOT Kiln.Voxel.IsAir.
    internal static bool Occupied(Voxel v) =>
        v.Material >= 128 || (v.Material is > 0 and < 128 && v.Fill != 0);

    private static void Count<K>(Dictionary<K, long> values, K key) where K : notnull =>
        values[key] = values.TryGetValue(key, out var current) ? current + 1 : 1;

    internal static FloorMaterialProfile Analyse(byte mode, string world, EntityId altar,
        CellBox box, CellPos center, IReadOnlyDictionary<CellPos, Voxel> voxels)
    {
        if (mode is not (8 or 9) || box.IsEmpty || box.Count > 1024 ||
            voxels.Count != box.Count)
            throw new InvalidOperationException("invalid or incomplete local floor sample");
        var p = new FloorMaterialProfile(mode, world, altar, box, center)
        {
            Read = voxels.Count,
        };

        foreach (var (pos, v) in voxels)
        {
            if (v.Material >= 128)
                p.Building++;
            else if (v.Material != 0 && v.Fill > 0)
            {
                p.Terrain++;
                if (v.Fill < 255) p.TerrainPartial++;
            }
            else p.Air++;
            if (Occupied(v)) Count(p.AllMaterial, v.Material);

            // Exact center vertical profile, from above the player to below:
            // retain AIR as well as solid, because a visual puddle might be
            // a layer/decal with NO corresponding material change.
            if (pos.X == center.X && pos.Z == center.Z)
                p.CenterLayers.Add($"dy={pos.Y - center.Y,+3} y={pos.Y} " +
                    $"raw=0x{v.Raw:X4} mat={v.Material} " +
                    $"fill/paint={v.Fill} " +
                    (v.Material >= 128 ? "BUILDING" : Occupied(v) ? "TERRAIN" : "AIR"));
        }

        p.CenterLayers.Sort((a, b) =>
        {
            static int Y(string s)
            {
                var from = s.IndexOf("y=", StringComparison.Ordinal);
                if (from < 0) return 0;
                from += 2;
                var end = s.IndexOf(' ', from);
                return int.Parse(s.AsSpan(from, end < 0 ? s.Length - from : end - from),
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            // Sort by Y position descending, despite the source collection's
            // dictionary enumeration order.
            return Y(b).CompareTo(Y(a));
        });

        for (var z = box.Z; z < box.End.Z; z++)
        for (var x = box.X; x < box.End.X; x++)
        {
            // Keep world Y bounded by the user's position. Do not mistake a
            // roof/overhead beam ABOVE the player for the floor being tested.
            var top = Math.Min(center.Y, box.End.Y - 1);
            Voxel? floor = null;
            var floorY = 0;
            for (var y = top; y >= box.Y; y--)
            {
                if (!voxels.TryGetValue(new CellPos(x, y, z), out var v))
                    throw new InvalidOperationException("floor sample is missing an expected cell");
                if (!Occupied(v)) continue;
                floor = v;
                floorY = y;
                break;
            }
            if (floor is not { } found)
            {
                p.NoFloor++;
                continue;
            }

            Count(p.TopMaterial, found.Material);
            Count(p.TopRaw, found.Raw);
            if (found.Material >= 128) p.TopBuilding++;
            else p.TopTerrain++;

            if (x == center.X && z == center.Z)
            {
                p.CenterFloorRaw = found.Raw;
                p.CenterFloorDy = floorY - center.Y;
            }
            // The central 3x3 feet-adjacent columns are logged individually,
            // as these are most likely to reflect the visible puddle.
            if (Math.Abs(x - center.X) <= 1 && Math.Abs(z - center.Z) <= 1)
                p.NearColumns.Add($"dx={x-center.X,+2} dz={z-center.Z,+2} " +
                    $"floorDy={floorY-center.Y,+3} mat={found.Material} " +
                    $"fill/paint={found.Fill} raw=0x{found.Raw:X4} " +
                    (found.Material >= 128 ? "BUILDING" : "TERRAIN"));
        }
        if (p.Terrain + p.Building + p.Air != p.Read)
            throw new InvalidOperationException("voxel occupancy classification mismatch");
        if (p.TopMaterial.Values.Sum() != p.TopTerrain + p.TopBuilding)
            throw new InvalidOperationException("floor material count mismatch");
        if (p.TopTerrain + p.TopBuilding + p.NoFloor != (long)box.SizeX * box.SizeZ)
            throw new InvalidOperationException("floor column coverage mismatch");
        return p;
    }

    internal string[] TopMaterials(int limit = 12) =>
        TopMaterial.OrderByDescending(x => x.Value).ThenBy(x => x.Key)
            .Take(limit).Select(x =>
                $"mat={x.Key} {(x.Key >= 128 ? "building" : "terrain")}: {x.Value} floor columns").ToArray();

    internal string[] TopRawValues(int limit = 12) =>
        TopRaw.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(limit)
            .Select(x =>
            {
                var v = new Voxel(x.Key);
                return $"raw=0x{x.Key:X4} mat={v.Material} fill/paint={v.Fill}: " +
                    $"{x.Value} floor columns";
            }).ToArray();

    internal string[] AllMaterials(int limit = 12) =>
        AllMaterial.OrderByDescending(x => x.Value).ThenBy(x => x.Key)
            .Take(limit).Select(x =>
                $"mat={x.Key} {(x.Key >= 128 ? "building" : "terrain")}: {x.Value} occupied cells").ToArray();
}
