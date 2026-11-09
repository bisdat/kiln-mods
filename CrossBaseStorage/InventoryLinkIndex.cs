// SPDX-License-Identifier: MIT
// Read-only reverse index for the linked 8-slot inventory entities exposed by
// the pinned Kiln Items.Read(owner).Inventories API. This class makes NO
// engine eligibility, Flame Altar, permissions or Magic Chest claims.
namespace CrossBaseStorage;

internal readonly record struct InventoryLinkOwner(
    uint Owner, bool MagicByTemplate, IReadOnlyList<uint> LinkedInventories);

internal enum InventoryLinkStatus { Matched, NotFound, Ambiguous, InvalidSnapshot }

internal readonly record struct InventoryLinkMatch(uint Owner, int LinkIndex, bool MagicByTemplate);

internal sealed record InventoryLinkResult(
    InventoryLinkStatus Status, uint InventoryEntity, IReadOnlyList<InventoryLinkMatch> Matches);

internal sealed class InventoryLinkIndex
{
    private readonly Dictionary<uint, IReadOnlyList<InventoryLinkMatch>> _byInventory;
    public bool Valid { get; }
    public int OwnerCount { get; }
    public int InventoryCount => _byInventory.Count;
    public int AmbiguousCount => _byInventory.Count(pair => pair.Value.Count > 1);

    private InventoryLinkIndex(
        bool valid, int ownerCount, Dictionary<uint, IReadOnlyList<InventoryLinkMatch>> byInventory)
    {
        Valid = valid;
        OwnerCount = ownerCount;
        _byInventory = byInventory;
    }

    public static InventoryLinkIndex Build(IEnumerable<InventoryLinkOwner> owners)
    {
        ArgumentNullException.ThrowIfNull(owners);
        var byInventory = new Dictionary<uint, List<InventoryLinkMatch>>();
        var seenOwners = new HashSet<uint>();
        var ownerCount = 0;
        foreach (var owner in owners)
        {
            ownerCount++;
            // The public Items API returns at most 32 flattened links
            // (including expanded virtual links). Reject any strange data
            // rather than attributing a staged payment to the wrong owner.
            if (owner.Owner == 0 || !seenOwners.Add(owner.Owner) ||
                owner.LinkedInventories is null ||
                owner.LinkedInventories.Count is < 1 or > 32)
                return Invalid(ownerCount);

            var seenLinks = new HashSet<uint>();
            for (var i = 0; i < owner.LinkedInventories.Count; i++)
            {
                var link = owner.LinkedInventories[i];
                if (link == 0 || !seenLinks.Add(link))
                    return Invalid(ownerCount);
                if (!byInventory.TryGetValue(link, out var matches))
                    byInventory.Add(link, matches = new List<InventoryLinkMatch>());
                matches.Add(new InventoryLinkMatch(owner.Owner, i, owner.MagicByTemplate));
            }
        }

        // Each inventory entity must map to one owner to be unambiguous.
        // Two owners sharing it are retained as an explicit Ambiguous result.
        var lookup = byInventory.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<InventoryLinkMatch>)pair.Value
                .OrderBy(m => m.Owner).ThenBy(m => m.LinkIndex).ToArray());
        return new InventoryLinkIndex(true, ownerCount, lookup);
    }

    private static InventoryLinkIndex Invalid(int count) =>
        new(false, count, new Dictionary<uint, IReadOnlyList<InventoryLinkMatch>>());

    public InventoryLinkResult Resolve(uint inventoryEntity)
    {
        if (!Valid)
            return new(InventoryLinkStatus.InvalidSnapshot, inventoryEntity, []);
        if (inventoryEntity == 0 || !_byInventory.TryGetValue(inventoryEntity, out var matches))
            return new(InventoryLinkStatus.NotFound, inventoryEntity, []);
        return new(matches.Count == 1 ? InventoryLinkStatus.Matched : InventoryLinkStatus.Ambiguous,
            inventoryEntity, matches);
    }
}
