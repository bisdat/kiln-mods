using CrossBaseStorage;

static void Check(bool value, string because)
{
    if (!value) throw new Exception(because);
}
static void Exact(long value, long expected, string because) =>
    Check(value == expected, $"{because}: expected {expected}, got {value}");
static void Same(string actual, string expected, string because) =>
    Check(actual == expected, $"{because}: expected {expected}, got {actual}");

var passed = 0;
void Verify(string description, Action test)
{
    test();
    Console.WriteLine($"PASS: {description}");
    passed++;
}

// From the user's real Workbench test: 0 player, 466 String in one distant
// Magic Chest and 250 in another, for a 3 String exact-item input.
Verify("Workbench String quotes only 3 units from nearby candidate, not 716", () =>
{
    var q = MaterialQuotePlanner.QuoteExact(3, [
        new QuoteSource(1, 0, 0, true),
        new QuoteSource(1412, 466, 100, false),
        new QuoteSource(1081, 250, 200, false)]);
    Check(q.Status == QuoteStatus.Satisfied, "String quote should be satisfied");
    Exact(q.Visible, 716, "visible stock");
    Exact(q.Debits.Count, 1, "one source selected");
    Exact(q.Debits[0].EntityId, 1412, "nearest chest ID");
    Exact(q.Debits[0].Units, 3, "exact units");
});

Verify("Source order: backpack, near chest, far chest", () =>
{
    var q = MaterialQuotePlanner.QuoteExact(9, [
        new QuoteSource(999, 2, 2500, false),
        new QuoteSource(1, 2, 0, true),
        new QuoteSource(500, 4, 10, false),
        new QuoteSource(300, 1, 300, false)]);
    Check(q.Status == QuoteStatus.Satisfied, "quote should succeed");
    Same(string.Join(",", q.Debits.Select(x => x.EntityId)), "1,500,300,999", "stable order");
    Same(string.Join(",", q.Debits.Select(x => x.Units)), "2,4,1,2", "allocation counts");
});

Verify("Multi-chest exact cost, no over-spending", () =>
{
    var q = MaterialQuotePlanner.QuoteExact(500, [
        new QuoteSource(1412, 466, 25, false),
        new QuoteSource(1081, 250, 100, false)]);
    Check(q.Status == QuoteStatus.Satisfied, "should allocate");
    Same(string.Join(",", q.Debits.Select(x => x.Units)), "466,34", "quantities");
    Exact(q.Debits.Sum(x => x.Units), 500, "exact allocation total");
});

Verify("Insufficient material fails closed without partial allocation", () =>
{
    var q = MaterialQuotePlanner.QuoteExact(717, [
        new QuoteSource(1412, 466, 25, false),
        new QuoteSource(1081, 250, 100, false)]);
    Check(q.Status == QuoteStatus.Insufficient, "should refuse");
    Exact(q.Missing, 1, "shortfall");
    Exact(q.Debits.Count, 0, "no proposed partial debits");
});

Verify("Duplicate source identity fails closed", () =>
{
    var q = MaterialQuotePlanner.QuoteExact(1, [
        new QuoteSource(1412, 466, 25, false),
        new QuoteSource(1412, 466, 25, false)]);
    Check(q.Status == QuoteStatus.InvalidSources, "duplicate must fail");
    Exact(q.Debits.Count, 0, "no debits when invalid");
});

Verify("Corrupt negative stock or NaN position fails closed", () =>
{
    foreach (var bad in new[] {
        new QuoteSource(1, -1, 0, false),
        new QuoteSource(1, 1, double.NaN, false),
        new QuoteSource(1, 1, -3, false)})
    {
        var q = MaterialQuotePlanner.QuoteExact(1, [bad]);
        Check(q.Status == QuoteStatus.InvalidSources, "bad source accepted");
    }
});

Verify("Deterministic tie-break by entity ID", () =>
{
    var a = new QuoteSource(140, 2, 10, false);
    var b = new QuoteSource(110, 2, 10, false);
    var q1 = MaterialQuotePlanner.QuoteExact(3, [a, b]);
    var q2 = MaterialQuotePlanner.QuoteExact(3, [b, a]);
    Same(string.Join(",", q1.Debits.Select(x => $"{x.EntityId}:{x.Units}")),
        "110:2,140:1", "source tie break");
    Same(string.Join(",", q2.Debits.Select(x => $"{x.EntityId}:{x.Units}")),
        "110:2,140:1", "input order independence");
});

Verify("Zero cost never invents a debit", () =>
{
    var q = MaterialQuotePlanner.QuoteExact(0, [
        new QuoteSource(1, 1000, 0, true)]);
    Check(q.Status == QuoteStatus.Satisfied, "zero cost valid");
    Exact(q.Debits.Count, 0, "no debits");
});

Verify("No source means a shortage", () =>
{
    var q = MaterialQuotePlanner.QuoteExact(3, []);
    Check(q.Status == QuoteStatus.Insufficient, "no sources");
    Exact(q.Missing, 3, "all units missing");
});

Verify("Overflow-safe stock totals", () =>
{
    var q = MaterialQuotePlanner.QuoteExact(2, [
        new QuoteSource(10, long.MaxValue, 5, false),
        new QuoteSource(20, long.MaxValue, 20, false)]);
    Check(q.Status == QuoteStatus.Satisfied, "big stacks");
    Exact(q.Visible, long.MaxValue, "saturated sum");
    Exact(q.Debits.Sum(x => x.Units), 2, "exact debit amount");
});

Console.WriteLine($"PASS: {passed} read-only material quote planner tests.");
