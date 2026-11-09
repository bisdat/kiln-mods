// SPDX-License-Identifier: MIT
// FINAL read-only pre-implementation handoff of a complete native recipe bill.
// Source quantities are observer snapshots, NOT server-authorised payment
// sources. This class has NO Kiln API dependency or inventory write methods.
namespace CrossBaseStorage;

internal readonly record struct HandoffIngredient(uint ItemId, long Units);
internal readonly record struct HandoffStock(uint ItemId, QuoteSource Source);
internal enum HandoffStatus
{
    UnknownIngredients, InvalidSources, MissingMaterials, LocalOnly, RemoteCandidates
}
internal sealed record HandoffLine(uint ItemId, long Required, long Visible,
    long Missing, IReadOnlyList<QuoteDebit> Proposed);
internal sealed record HandoffReport(HandoffStatus Status,
    IReadOnlyList<HandoffLine> Lines, int RemoteOwners, long RequiredTotal)
{
    // IMPORTANT: RemoteCandidates means only read-only stock arithmetic.
    // There is no runtime engine authorisation or transaction evidence here.
    public bool NativePaymentPermitted => false;
}

internal static class CraftHandoffPlanner
{
    public static HandoffReport Inspect(
        IReadOnlyList<HandoffIngredient> nativeInputs,
        IReadOnlyList<HandoffStock> observed,
        uint player)
    {
        ArgumentNullException.ThrowIfNull(nativeInputs);
        ArgumentNullException.ThrowIfNull(observed);
        if (player == 0) return Fail(HandoffStatus.InvalidSources);
        // Category placeholders (ItemId=0) and rawCount=0 are unresolved,
        // never free; do not output a partly "payable" recipe.
        if (nativeInputs.Count == 0 || nativeInputs.Count > 64 ||
            nativeInputs.Any(i => i.ItemId == 0 || i.Units <= 0))
            return Fail(HandoffStatus.UnknownIngredients);

        var summed = new SortedDictionary<uint, long>();
        long grandTotal = 0;
        foreach (var input in nativeInputs)
        {
            if (summed.TryGetValue(input.ItemId, out var current) &&
                current > long.MaxValue - input.Units)
                return Fail(HandoffStatus.InvalidSources);
            if (grandTotal > long.MaxValue - input.Units)
                return Fail(HandoffStatus.InvalidSources);
            summed[input.ItemId] = current + input.Units;
            grandTotal += input.Units;
        }

        if (observed.Count > 8192 || observed.Any(s =>
                s.ItemId == 0 || s.Source.EntityId == 0 ||
                s.Source.Available < 0 || !double.IsFinite(s.Source.DistanceSquared) ||
                s.Source.DistanceSquared < 0 ||
                (s.Source.IsPlayer && s.Source.EntityId != player) ||
                (!s.Source.IsPlayer && s.Source.EntityId == player)))
            return Fail(HandoffStatus.InvalidSources);

        var lines = new List<HandoffLine>(summed.Count);
        var remoteOwners = new HashSet<uint>();
        var shortfall = false;
        foreach (var (item, needed) in summed)
        {
            var sources = observed.Where(s => s.ItemId == item).Select(s => s.Source).ToArray();
            // Reuse strict duplicate-identity checks. Never double-count an
            // owner or a linked 8-slot inventory as a separate chest.
            var quote = MaterialQuotePlanner.QuoteExact(needed, sources);
            if (quote.Status == QuoteStatus.InvalidSources)
                return Fail(HandoffStatus.InvalidSources);
            if (quote.Status == QuoteStatus.Insufficient)
                shortfall = true;
            foreach (var debit in quote.Debits.Where(d => !d.IsPlayer))
                remoteOwners.Add(debit.EntityId);
            lines.Add(new HandoffLine(item, needed, quote.Visible, quote.Missing, quote.Debits));
        }

        if (shortfall)
        {
            // All-or-nothing estimate: no partial multi-item recipe plan
            // survives another ingredient's stock shortfall.
            lines = lines.Select(x => x with { Proposed = Array.Empty<QuoteDebit>() }).ToList();
            remoteOwners.Clear();
        }
        return new HandoffReport(
            shortfall ? HandoffStatus.MissingMaterials :
            remoteOwners.Count > 0 ? HandoffStatus.RemoteCandidates : HandoffStatus.LocalOnly,
            lines, remoteOwners.Count, grandTotal);
    }

    private static HandoffReport Fail(HandoffStatus status) =>
        new(status, [], 0, 0);
}
