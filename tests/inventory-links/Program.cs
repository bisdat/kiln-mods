using CrossBaseStorage;

static void Check(bool condition, string reason)
{
    if (!condition) throw new Exception(reason);
}

var passed = 0;
void Verify(string name, Action action)
{
    action();
    ++passed;
    Console.WriteLine($"PASS: {name}");
}

static InventoryLinkOwner Chest(uint owner, bool magic, params uint[] links) =>
    new(owner, magic, links);

Verify("nine linked 8-slot inventory entities map to one 72-slot chest", () =>
{
    var map = InventoryLinkIndex.Build([
        Chest(1412, true, 1412, 1401, 1402, 1403, 1404, 1405, 1406, 1407, 1408),
        Chest(1081, true, 1081, 1061, 1062)]);
    Check(map.Valid && map.OwnerCount == 2 && map.InventoryCount == 12, "index counts");
    var found = map.Resolve(1408);
    Check(found.Status == InventoryLinkStatus.Matched, "linked inventory should map");
    Check(found.Matches.Count == 1 && found.Matches[0] == new InventoryLinkMatch(1412, 8, true),
        "owner, index and template heuristic");
});

Verify("first link may be owner entity itself", () =>
{
    var map = InventoryLinkIndex.Build([Chest(1412, true, 1412, 1401)]);
    var found = map.Resolve(1412);
    Check(found.Status == InventoryLinkStatus.Matched &&
          found.Matches[0].LinkIndex == 0, "owner-as-first-link");
});

Verify("non-magic chests must not be silently reclassified", () =>
{
    var map = InventoryLinkIndex.Build([Chest(900, false, 900)]);
    var found = map.Resolve(900);
    Check(found.Status == InventoryLinkStatus.Matched && !found.Matches[0].MagicByTemplate,
        "template guess must remain false");
});

Verify("shared link across two owners is ambiguous and never resolves uniquely", () =>
{
    var map = InventoryLinkIndex.Build([
        Chest(10, true, 10, 333), Chest(20, false, 20, 333)]);
    Check(map.Valid && map.AmbiguousCount == 1, "must retain ambiguity");
    var found = map.Resolve(333);
    Check(found.Status == InventoryLinkStatus.Ambiguous &&
          found.Matches.Count == 2 &&
          found.Matches[0].Owner == 10 &&
          found.Matches[1].Owner == 20, "ambiguous ownership");
    Check(map.Resolve(10).Status == InventoryLinkStatus.Matched, "unshared link still maps");
});

Verify("unknown inventory is not called an ordinary chest or a player", () =>
{
    var map = InventoryLinkIndex.Build([Chest(10, true, 10)]);
    var found = map.Resolve(999);
    Check(found.Status == InventoryLinkStatus.NotFound && found.Matches.Count == 0,
        "unobserved links remain unknown");
});

Verify("zero inventory ID is never a source match", () =>
{
    var map = InventoryLinkIndex.Build([Chest(10, true, 10)]);
    Check(map.Resolve(0).Status == InventoryLinkStatus.NotFound, "zero ID must not resolve");
});

Verify("duplicate container owner invalidates the snapshot", () =>
{
    var map = InventoryLinkIndex.Build([Chest(10, true, 10), Chest(10, true, 20)]);
    Check(!map.Valid && map.Resolve(10).Status == InventoryLinkStatus.InvalidSnapshot,
        "duplicate owner");
});

Verify("duplicate links within one owner invalidate the snapshot", () =>
{
    var map = InventoryLinkIndex.Build([Chest(10, true, 10, 11, 11)]);
    Check(!map.Valid && map.Resolve(11).Status == InventoryLinkStatus.InvalidSnapshot,
        "duplicate link");
});

Verify("missing link or owner zero invalidate the snapshot", () =>
{
    foreach (var map in new[] {
        InventoryLinkIndex.Build([Chest(10, true, 10, 0)]),
        InventoryLinkIndex.Build([Chest(0, true, 10)])})
        Check(!map.Valid && map.Resolve(10).Status == InventoryLinkStatus.InvalidSnapshot,
            "invalid identity");
});

Verify("empty or oversized source lists invalidate the snapshot", () =>
{
    var empty = InventoryLinkIndex.Build([Chest(10, true)]);
    var many = InventoryLinkIndex.Build([Chest(10, true,
        Enumerable.Range(100, 33).Select(i => (uint)i).ToArray())]);
    Check(!empty.Valid && !many.Valid, "invalid link counts");
});

Verify("input order does not change owner resolution", () =>
{
    var x = Chest(500, true, 701, 702);
    var y = Chest(100, false, 703);
    var a = InventoryLinkIndex.Build([x, y]);
    var b = InventoryLinkIndex.Build([y, x]);
    Check(a.Resolve(702).Matches.SequenceEqual(b.Resolve(702).Matches), "order independence");
});

Verify("empty list resolves nothing but is a valid observation", () =>
{
    var map = InventoryLinkIndex.Build([]);
    Check(map.Valid && map.OwnerCount == 0 &&
          map.Resolve(12).Status == InventoryLinkStatus.NotFound, "empty scan");
});

Console.WriteLine($"PASS: {passed} read-only inventory link ownership tests.");
