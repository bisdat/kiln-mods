// The game stores a hash of its UTF-8 custom chest name in the
// CustomPlayerString component. Verified on the same host chest with two
// known UI renames (2026-11-07):
// "Starter Blocks" -> 0x35F105E7ECC53E32
// "Tools"          -> 0xC26501E3ADE7DA3C
//
// This helper only reads the 8-byte ECS value via Kiln.World. It never
// walks the game's private string store, reads pointers, or mutates a chest.
using System.Text;
using Kiln;

namespace EndlessChests;

internal static class ChestNames
{
    private const string Component = "CustomPlayerString";
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Hash(string name)
    {
        var hash = OffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(name))
            hash = unchecked((hash ^ b) * Prime);
        return hash;
    }

    // "Empty" is intentionally case-insensitive, unlike other original
    // catalogue labels. Cover every ASCII capitalization directly because
    // FNV-1a hashes cannot be folded to lower case after hashing.
    private static readonly HashSet<ulong> EmptyHashes = BuildEmptyHashes();

    private static HashSet<ulong> BuildEmptyHashes()
    {
        var result = new HashSet<ulong>();
        const string text = "empty";
        for (var mask = 0; mask < (1 << text.Length); mask++)
        {
            var name = text.ToCharArray();
            for (var i = 0; i < name.Length; i++)
                if ((mask & (1 << i)) != 0) name[i] = char.ToUpperInvariant(name[i]);
            result.Add(Hash(new string(name)));
        }
        return result;
    }

    public static bool IsEmptyHash(ulong hash) => EmptyHashes.Contains(hash);

    // A missing, differently sized or unreadable component must never be
    // treated as a known name. This only returns a possible hash, not text.
    public static bool TryReadHash(EntityId chest, out ulong hash)
    {
        hash = 0;
        return World.Available &&
            World.ComponentSize(chest, Component) == sizeof(ulong) &&
            World.TryRead(chest, Component, out hash) && hash != 0;
    }
}
