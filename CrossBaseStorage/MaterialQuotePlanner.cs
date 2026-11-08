// SPDX-License-Identifier: MIT
// Cross-base Storage: pure, read-only proposal of exact-item source deductions.
// This class has NO Kiln dependencies and never touches inventory state.
// The vanilla engine must revalidate and atomically debit actual game sources.
namespace CrossBaseStorage;

internal enum QuoteStatus { Satisfied, Insufficient, InvalidSources }

internal readonly record struct QuoteSource(
    uint EntityId, long Available, double DistanceSquared, bool IsPlayer);

internal readonly record struct QuoteDebit(
    uint EntityId, long Units, bool IsPlayer);

internal sealed record MaterialQuote(
    QuoteStatus Status, long Required, long Visible, long Missing,
    IReadOnlyList<QuoteDebit> Debits);

internal static class MaterialQuotePlanner
{
    public static MaterialQuote QuoteExact(long required, IEnumerable<QuoteSource> sources)
    {
        if (required < 0)
            throw new ArgumentOutOfRangeException(nameof(required));
        ArgumentNullException.ThrowIfNull(sources);

        var candidates = sources.ToArray();
        // Each source identity must appear exactly once. Do not double-count
        // linked inventories or a player inventory that was listed as a chest.
        // Unknown/corrupt source data must fail closed.
        if (candidates.Any(s => s.Available < 0 || double.IsNaN(s.DistanceSquared) ||
                                s.DistanceSquared < 0) ||
            candidates.GroupBy(s => s.EntityId).Any(group => group.Count() != 1))
        {
            return new MaterialQuote(QuoteStatus.InvalidSources, required, 0, required, []);
        }

        // Saturating total is informational. Any actual native transaction
        // would need to validate live amounts and the game's permissions.
        long visible = 0;
        foreach (var source in candidates)
        {
            if (long.MaxValue - source.Available < visible)
                visible = long.MaxValue;
            else
                visible += source.Available;
        }

        if (visible < required)
            return new MaterialQuote(QuoteStatus.Insufficient, required, visible,
                required - visible, []);

        // Stable, deterministic fallback: the player's own inventory first,
        // then nearest eligible *candidate* Magic Chest by distance, then ID.
        // Proximity is NOT an authoritative base/altar-membership test.
        var ordered = candidates
            .Where(s => s.Available > 0)
            .OrderByDescending(s => s.IsPlayer)
            .ThenBy(s => s.DistanceSquared)
            .ThenBy(s => s.EntityId);
        var remaining = required;
        var debits = new List<QuoteDebit>();
        foreach (var source in ordered)
        {
            if (remaining == 0) break;
            var take = Math.Min(remaining, source.Available);
            if (take > 0)
            {
                debits.Add(new QuoteDebit(source.EntityId, take, source.IsPlayer));
                remaining -= take;
            }
        }

        if (remaining != 0)
            throw new InvalidOperationException("Quote arithmetic inconsistent with stock totals.");
        return new MaterialQuote(QuoteStatus.Satisfied, required, visible, 0, debits);
    }
}
