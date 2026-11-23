// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Kiln;

namespace Bisdat.BaseVisualProbe;

/// <summary>
/// Experimental interpretation of Kiln's raw BUILDING voxel paint byte.
/// This probe never mutates a Voxel. Read-only evidence must establish
/// whether the game's Overgrowth/Decay tools really toggle these bits.
/// </summary>
internal static class VisualBits
{
    internal const byte Weathered = 0x80;
    internal const byte Overgrowth = 0x70;
    internal const byte OtherPaint = 0x0f;

    // IMPORTANT: building material >= 128 means occupied EVEN when
    // the high byte (called Fill in Kiln.Voxel) is zero.
    internal static bool IsBuilding(Voxel v) => v.Material >= 128;
    internal static int Growth(Voxel v) => (v.Fill & Overgrowth) >> 4;
    internal static bool Weather(Voxel v) => (v.Fill & Weathered) != 0;
    internal static int Tint(Voxel v) => v.Fill & OtherPaint;

    internal static CellBox Intersect(CellBox a, CellBox b)
    {
        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        var z = Math.Max(a.Z, b.Z);
        var endX = Math.Min(a.End.X, b.End.X);
        var endY = Math.Min(a.End.Y, b.End.Y);
        var endZ = Math.Min(a.End.Z, b.End.Z);
        return new CellBox(x, y, z,
            Math.Max(0, endX - x), Math.Max(0, endY - y), Math.Max(0, endZ - z));
    }

    // Use cell CENTRES and actual altar build-zone rectangular bounds.
    // Not a sphere or a guessed radius; grid may be offset.
    internal static CellBox ZoneCells(PlayerBase zone, VoxelGrid grid)
    {
        var size = grid.CellSize;
        if (!double.IsFinite(size) || size <= 0)
            throw new InvalidOperationException("invalid voxel cell size");
        static int Min(double at, double origin, double step, int max) =>
            (int)Math.Clamp(Math.Ceiling((at - origin) / step - 0.5 - 1e-9), 0, max);
        static int End(double at, double origin, double step, int max) =>
            (int)Math.Clamp(Math.Floor((at - origin) / step - 0.5 + 1e-9) + 1, 0, max);
        var x0 = Min(zone.Center.X - zone.Half.X, grid.Origin.X, size, grid.CellsX);
        var y0 = Min(zone.Center.Y - zone.Half.Y, grid.Origin.Y, size, 8192);
        var z0 = Min(zone.Center.Z - zone.Half.Z, grid.Origin.Z, size, grid.CellsZ);
        var x1 = End(zone.Center.X + zone.Half.X, grid.Origin.X, size, grid.CellsX);
        var y1 = End(zone.Center.Y + zone.Half.Y, grid.Origin.Y, size, 8192);
        var z1 = End(zone.Center.Z + zone.Half.Z, grid.Origin.Z, size, grid.CellsZ);
        return new CellBox(x0, y0, z0,
            Math.Max(0, x1 - x0), Math.Max(0, y1 - y0), Math.Max(0, z1 - z0));
    }

    // 8192 voxels per Build.Read; x is the fastest-moving axis.
    internal static IEnumerable<CellBox> Batches(CellBox region)
    {
        const int nx = 32, ny = 16, nz = 16;
        for (var z = region.Z; z < region.End.Z; z += nz)
        for (var y = region.Y; y < region.End.Y; y += ny)
        for (var x = region.X; x < region.End.X; x += nx)
            yield return new CellBox(x, y, z,
                Math.Min(nx, region.End.X - x),
                Math.Min(ny, region.End.Y - y),
                Math.Min(nz, region.End.Z - z));
    }

    internal static IEnumerable<(CellPos Cell, Voxel Value)> Enumerate(CellBox box, Voxel[] values)
    {
        if (values.Length != box.Count)
            throw new InvalidOperationException("voxel read length mismatch");
        var index = 0;
        for (var z = box.Z; z < box.End.Z; z++)
        for (var y = box.Y; y < box.End.Y; y++)
        for (var x = box.X; x < box.End.X; x++)
            yield return (new CellPos(x, y, z), values[index++]);
    }
}

internal sealed class VisualInventory
{
    internal long Scanned, Buildings, ZeroPaint, Overgrown, Weathered, Both, OtherPainted;
    internal readonly long[] ByMaterial = new long[128];
    internal readonly long[] GrowthLevel = new long[8];

    internal void Add(ReadOnlySpan<Voxel> values)
    {
        Scanned += values.Length;
        foreach (var v in values)
        {
            if (!VisualBits.IsBuilding(v)) continue;
            Buildings++;
            ByMaterial[v.Material - 128]++;
            if (v.Fill == 0) ZeroPaint++;
            if (VisualBits.Tint(v) != 0) OtherPainted++;
            var growth = VisualBits.Growth(v);
            GrowthLevel[growth]++;
            if (growth != 0) Overgrown++;
            if (VisualBits.Weather(v)) Weathered++;
            if (growth != 0 && VisualBits.Weather(v)) Both++;
        }
    }
}

internal sealed class VisualSnapshot(
    string worldId, PlayerBase zone, CellBox box)
{
    internal readonly string WorldId = worldId;
    internal readonly PlayerBase Altar = zone;
    internal readonly CellBox Box = box;
    internal readonly Dictionary<CellPos, Voxel> Cells = new();
    internal DateTime CapturedUtc;
    internal EntityId Player;
}

internal sealed class VisualChangeCounts
{
    internal long Compared, Changed, MaterialChanged;
    internal long GrowthAdded, GrowthCleared, GrowthChanged;
    internal long WeatherAdded, WeatherCleared, TintChanged;
    internal readonly List<string> Samples = new();

    internal void Compare(CellPos pos, Voxel previous, Voxel current)
    {
        Compared++;
        if (previous.Raw == current.Raw) return;
        Changed++;
        if (!VisualBits.IsBuilding(previous) ||
            !VisualBits.IsBuilding(current) || previous.Material != current.Material)
        {
            MaterialChanged++;
            Example(pos, previous, current, "material/occupancy change; not a paint-only comparison");
            return;
        }

        var oldGrowth = VisualBits.Growth(previous);
        var newGrowth = VisualBits.Growth(current);
        var oldWeather = VisualBits.Weather(previous);
        var newWeather = VisualBits.Weather(current);
        if (oldGrowth == 0 && newGrowth != 0) GrowthAdded++;
        else if (oldGrowth != 0 && newGrowth == 0) GrowthCleared++;
        else if (oldGrowth != newGrowth) GrowthChanged++;
        if (!oldWeather && newWeather) WeatherAdded++;
        else if (oldWeather && !newWeather) WeatherCleared++;
        if (VisualBits.Tint(previous) != VisualBits.Tint(current)) TintChanged++;

        Example(pos, previous, current,
            $"growth {oldGrowth}->{newGrowth}, weather {(oldWeather ? 1 : 0)}->{(newWeather ? 1 : 0)}, " +
            $"tint {VisualBits.Tint(previous)}->{VisualBits.Tint(current)}");
    }

    private void Example(CellPos pos, Voxel a, Voxel b, string reason)
    {
        // The log is deliberately bounded. No giant voxel dump or private
        // player information; exact cell coordinates allow reproducibility.
        if (Samples.Count < 24)
            Samples.Add($"cell({pos.X},{pos.Y},{pos.Z}) mat {a.Material}->{b.Material} " +
                $"raw 0x{a.Raw:X4}->0x{b.Raw:X4}; {reason}");
    }
}
