// Known-name lookup for the game's FNV-1a64 CustomPlayerString component.
// The game does not provide the underlying text through Kiln.World, so a
// hash cannot simply be lowercased after the fact.
//
// Fast path: exact, lower, upper and title-case spellings.
// Fallback: exact ASCII case-insensitive match for up to 30 letters,
// performed once per previously unseen hash with a bidirectional FNV search.
// Longer names retain their fast-path spellings to bound tick-thread work.
using System.Globalization;
using System.Text;

namespace EndlessChests;

internal sealed class ChestNameMatcher
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    // Multiplicative inverse of FNV-1a64 prime modulo 2^64.
    private const ulong InversePrime = 14886173955864302971UL;
    private const int MaxMixedCaseLetters = 30;

    private readonly Dictionary<ulong, string> _fast = [];
    private readonly HashSet<ulong> _ambiguous = [];
    private readonly Dictionary<ulong, string?> _cache = [];
    private readonly List<Candidate>[] _byLow5 =
        Enumerable.Range(0, 32).Select(_ => new List<Candidate>()).ToArray();

    public int Count => _fast.Count;
    public int MaxExhaustiveLetters => MaxMixedCaseLetters;

    private sealed class Candidate(string name)
    {
        public string Name { get; } = name;
        public byte[] Bytes { get; } = Encoding.UTF8.GetBytes(name);
        public int LetterCount { get; } = name.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
    }

    public void Add(string name)
    {
        var entry = new Candidate(name);
        _byLow5[(int)(ChestNames.Hash(name) & 31)].Add(entry);

        // Common spellings are resolved in O(1) on the hot game thread,
        // including for very long item names and Unicode text.
        AddFast(name, name);
        AddFast(name.ToLowerInvariant(), name);
        AddFast(name.ToUpperInvariant(), name);
        AddFast(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()), name);
    }

    private void AddFast(string spelled, string canonical)
    {
        var hash = ChestNames.Hash(spelled);
        if (_ambiguous.Contains(hash)) return;
        if (_fast.TryGetValue(hash, out var earlier))
        {
            if (string.Equals(earlier, canonical, StringComparison.Ordinal)) return;
            _fast.Remove(hash);
            _ambiguous.Add(hash);
            return;
        }
        _fast.Add(hash, canonical);
    }

    public bool TryResolve(ulong fingerprint, out string selection)
    {
        selection = "";
        if (_ambiguous.Contains(fingerprint)) return false;
        if (_fast.TryGetValue(fingerprint, out var exact))
        {
            selection = exact;
            return true;
        }
        if (_cache.TryGetValue(fingerprint, out var cached))
        {
            if (cached is null) return false;
            selection = cached;
            return true;
        }

        // ASCII case changes flip bit 5 of the input bytes. FNV-1a's
        // multiplication by an odd number preserves equality of the final
        // five low hash bits across all ASCII capitalization variants.
        // Limit the search to that bucket instead of every catalogue name.
        string? recognized = null;
        foreach (var candidate in _byLow5[(int)(fingerprint & 31)])
        {
            if (candidate.LetterCount > MaxMixedCaseLetters ||
                !MatchesAsciiCase(candidate.Bytes, candidate.LetterCount, fingerprint))
                continue;

            // Two different catalogue names that can produce the same
            // fingerprint are ambiguous: never choose one for a transaction.
            if (recognized is not null &&
                !string.Equals(recognized, candidate.Name, StringComparison.OrdinalIgnoreCase))
            {
                _cache[fingerprint] = null;
                return false;
            }
            recognized = candidate.Name;
        }

        _cache[fingerprint] = recognized; // cache negative results as well
        if (recognized is null) return false;
        selection = recognized;
        return true;
    }

    private static bool AsciiLetter(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';

    // Meet in the middle: generate all hash states for the first half of
    // the letters, then invert FNV-1a from the supplied final hash over
    // all possibilities in the second half. 2^15 per half maximum;
    // no private game-memory access, and no 2^30 materialized variants.
    private static bool MatchesAsciiCase(byte[] bytes, int letters, ulong target)
    {
        if (letters == 0) return ChestNames.Hash(Encoding.UTF8.GetString(bytes)) == target;
        var half = letters / 2;
        var cut = 0;
        if (half > 0)
        {
            var seen = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (!AsciiLetter(bytes[i])) continue;
                if (++seen == half) { cut = i + 1; break; }
            }
        }

        var prefixStates = new HashSet<ulong>();
        Forward(bytes, 0, cut, OffsetBasis, prefixStates);
        return Backward(bytes, bytes.Length - 1, cut, target, prefixStates);
    }

    private static void Forward(
        byte[] bytes, int index, int cut, ulong hash, HashSet<ulong> states)
    {
        if (index == cut) { states.Add(hash); return; }
        var next = unchecked((hash ^ bytes[index]) * Prime);
        Forward(bytes, index + 1, cut, next, states);
        if (AsciiLetter(bytes[index]))
        {
            var other = unchecked((hash ^ (bytes[index] ^ 0x20u)) * Prime);
            Forward(bytes, index + 1, cut, other, states);
        }
    }

    private static bool Backward(
        byte[] bytes, int index, int cut, ulong hash, HashSet<ulong> prefixStates)
    {
        if (index < cut) return prefixStates.Contains(hash);
        var previous = unchecked(hash * InversePrime);
        if (Backward(bytes, index - 1, cut, previous ^ bytes[index], prefixStates))
            return true;
        return AsciiLetter(bytes[index]) &&
               Backward(bytes, index - 1, cut, previous ^ (bytes[index] ^ 0x20u), prefixStates);
    }
}
