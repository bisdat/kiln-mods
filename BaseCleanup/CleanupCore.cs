// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Kiln;

namespace Bisdat.BaseCleanup;

// Binary journal is stored as one compressed, base64-encoded WorldData value.
// Every record preserves the exact previous 16-bit voxel (material AND fill).
internal static class CleanupCore
{
    internal const byte IncludeMud = 1;
    internal const byte IncludeMycelium = 2;
    // Opt-in only: this flag is never enabled by the old console command.
    internal const byte IncludeDecay = 4;
    internal const byte AllowedOptions = IncludeMud | IncludeMycelium | IncludeDecay;

    // EMF/Kiln building paint high byte: bit 7 = weathering; bits 4..6
    // represent growth state and MUST NOT be modified by decay repair.
    internal const ushort WeatherMask = 0x8000;
    internal static bool DecayTarget(Voxel voxel, byte options) =>
        (options & IncludeDecay) != 0 &&
        voxel.Material >= 128 && (voxel.Raw & WeatherMask) != 0;

    // Terrain targets are removed to AIR; only cosmetic building weathering
    // is cleared. The material byte, 7 other paint bits and geometry remain.
    // This is also the exact expected post-edit value used for safe Undo.
    internal static Voxel EditedValue(ushort originalRaw) =>
        (originalRaw & 0xff) >= 128
            ? new Voxel((ushort)(originalRaw & ~WeatherMask))
            : Voxel.Air;
    private const int Magic = 0x32434C42; // BLC2
    private const int MaxChunks = 250_000, MaxCells = 4_000_000;

    internal static bool Target(Voxel voxel, byte options) =>
        DecayTarget(voxel, options) ||
        (!voxel.IsAir &&
         (BaseScanCore.IsRubble(voxel.Material)
          || (voxel.Material == BaseScanCore.UnderwaterMud && (options & IncludeMud) != 0)
          || (BaseScanCore.IsMycelium(voxel.Material) && (options & IncludeMycelium) != 0)));

    internal static string Encode(IReadOnlyList<CleanupChunk> chunks)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip))
        {
            writer.Write(Magic);
            writer.Write(chunks.Count);
            foreach (var c in chunks)
            {
                writer.Write(c.Box.X); writer.Write(c.Box.Y); writer.Write(c.Box.Z);
                writer.Write(c.Box.SizeX); writer.Write(c.Box.SizeY); writer.Write(c.Box.SizeZ);
                writer.Write(c.Cells.Count);
                foreach (var v in c.Cells) { writer.Write(v.Index); writer.Write(v.Raw); }
            }
        }
        return Convert.ToBase64String(output.ToArray());
    }

    internal static List<CleanupChunk> Decode(string encoded)
    {
        if (encoded.Length > 24_000_000) throw new InvalidDataException("cleanup journal is too large");
        using var source = new MemoryStream(Convert.FromBase64String(encoded));
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);
        if (reader.ReadInt32() != Magic) throw new InvalidDataException("unsupported cleanup journal");
        var count = reader.ReadInt32();
        if (count < 0 || count > MaxChunks) throw new InvalidDataException("invalid journal chunk count");
        var result = new List<CleanupChunk>(count);
        long total = 0;
        for (var i = 0; i < count; i++)
        {
            var box = new CellBox(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            if (box.IsEmpty || box.Count > 8192) throw new InvalidDataException("invalid journal box");
            var items = reader.ReadInt32();
            if (items < 0 || items > box.Count || (total += items) > MaxCells)
                throw new InvalidDataException("invalid journal cell count");
            var c = new CleanupChunk(box);
            var used = new HashSet<int>();
            for (var j = 0; j < items; j++)
            {
                var index = reader.ReadInt32();
                var raw = reader.ReadUInt16();
                if (index < 0 || index >= box.Count || !used.Add(index) ||
                    (raw >> 8) == 0 ||
                    ((raw & 0xff) >= 128 && (raw & WeatherMask) == 0))
                    throw new InvalidDataException("invalid cleanup journal cell");
                c.Cells.Add(new CleanupCell(index, raw));
            }
            result.Add(c);
        }
        if (reader.Read() != -1) throw new InvalidDataException("unexpected journal trailing data");
        return result;
    }

    internal static long Count(IReadOnlyList<CleanupChunk> chunks)
    {
        long n = 0;
        foreach (var c in chunks) n += c.Cells.Count;
        return n;
    }
}

internal readonly record struct CleanupCell(int Index, ushort Raw);

internal sealed class CleanupChunk(CellBox box)
{
    internal CellBox Box { get; } = box;
    internal List<CleanupCell> Cells { get; } = [];
}

// WorldData serializes public properties. There is only one active ledger per
// world: a new cleanup is refused until a previous rollback is dealt with.
public sealed class CleanupLedger
{
    public string WorldId { get; set; } = "";
    public string BackupName { get; set; } = "";
    public string Phase { get; set; } = "prepared";
    public string Packed { get; set; } = "";
    public int Planned { get; set; }
    public long Removed { get; set; }
    public long Restored { get; set; }
    public long Conflicted { get; set; }
    // Optional new field; v0.2.0 saved ledgers deserialize with zero.
    public long DecayPlanned { get; set; }
    public double CellSize { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double CenterZ { get; set; }
    public float HalfX { get; set; }
    public float HalfY { get; set; }
    public float HalfZ { get; set; }
}

internal sealed class CleanupPlan(
    EntityId player, string world, PlayerBase altar, byte options, int generation)
{
    internal EntityId Player = player;
    internal string WorldId = world;
    internal PlayerBase Altar = altar;
    internal byte Options = options;
    internal int Generation = generation;
    internal List<CleanupChunk> Chunks = [];
    internal long[] Materials = new long[256];
    internal long Read;
    internal long Total;
    internal long Hits;
    internal long DecayHits;
    internal bool CancelRequested;
    internal DateTime PreparedAtUtc;
}

internal static class CleanupSafety
{
    internal static bool MatchingAltar(CleanupLedger ledger, PlayerBase zone) =>
        Math.Abs(zone.Center.X - ledger.CenterX) < 0.001 &&
        Math.Abs(zone.Center.Y - ledger.CenterY) < 0.001 &&
        Math.Abs(zone.Center.Z - ledger.CenterZ) < 0.001 &&
        Math.Abs(zone.Half.X - ledger.HalfX) < 0.001 &&
        Math.Abs(zone.Half.Y - ledger.HalfY) < 0.001 &&
        Math.Abs(zone.Half.Z - ledger.HalfZ) < 0.001;

    internal static CleanupLedger Ledger(CleanupPlan plan, string backupName) => new()
    {
        WorldId = plan.WorldId,
        BackupName = backupName,
        Phase = "prepared",
        Packed = CleanupCore.Encode(plan.Chunks),
        Planned = checked((int)plan.Hits),
        DecayPlanned = plan.DecayHits,
        CellSize = Build.Grid!.CellSize,
        CenterX = plan.Altar.Center.X, CenterY = plan.Altar.Center.Y, CenterZ = plan.Altar.Center.Z,
        HalfX = plan.Altar.Half.X, HalfY = plan.Altar.Half.Y, HalfZ = plan.Altar.Half.Z,
    };
}
