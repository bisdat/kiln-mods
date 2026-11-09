using CrossBaseStorage;

int passed = 0;
void Verify(string name, Action action)
{
    action();
    ++passed;
    Console.WriteLine($"PASS: {name}");
}
static void Check(bool good, string reason)
{
    if (!good) throw new Exception(reason);
}
static HandoffStock Stock(uint item, uint owner, long units, double distance, bool isPlayer = false) =>
    new(item, new QuoteSource(owner, units, distance, isPlayer));
static HandoffReport Plan(HandoffIngredient[] inputs, params HandoffStock[] stocks) =>
    CraftHandoffPlanner.Inspect(inputs, stocks, 1);
static HandoffIngredient Need(uint item, long units) => new(item, units);
const uint String = 0x12a, Wood = 0x4b;

Verify("remote Magic Chest proposal is exact, never native-authorised", () =>
{
    var r = Plan([Need(String, 3)], Stock(String, 1, 0, 0, true),
        Stock(String, 1412, 466, 100), Stock(String, 1081, 250, 200));
    Check(r.Status == HandoffStatus.RemoteCandidates && r.RemoteOwners == 1 &&
          r.Lines[0].Proposed.Count == 1 &&
          r.Lines[0].Proposed[0].EntityId == 1412 &&
          r.Lines[0].Proposed[0].Units == 3 &&
          !r.NativePaymentPermitted, "only three String proposed from one chest");
});
Verify("player material always comes first", () =>
{
    var r = Plan([Need(String, 10)], Stock(String, 1, 3, 0, true),
        Stock(String, 1081, 50, 1), Stock(String, 1412, 40, 2));
    Check(r.Status == HandoffStatus.RemoteCandidates &&
          r.Lines[0].Proposed.Select(x => x.Units).SequenceEqual([3L, 7L]) &&
          r.Lines[0].Proposed[0].IsPlayer, "player-first 3+7");
});
Verify("local-only cost needs no new remote payer", () =>
{
    var r = Plan([Need(String, 4)], Stock(String, 1, 10, 0, true));
    Check(r.Status == HandoffStatus.LocalOnly && r.RemoteOwners == 0 &&
          r.Lines[0].Proposed.Count == 1 && !r.NativePaymentPermitted, "vanilla only");
});
Verify("unknown category placeholders fail whole recipe without allocations", () =>
{
    var r = Plan([Need(String, 3), Need(0, 0)], Stock(String, 1412, 466, 1));
    Check(r.Status == HandoffStatus.UnknownIngredients && r.Lines.Count == 0, "category");
});
Verify("zero count for nonzero item is not a free ingredient", () =>
{
    var r = Plan([Need(String, 3), Need(Wood, 0)], Stock(String, 1412, 466, 1));
    Check(r.Status == HandoffStatus.UnknownIngredients && r.Lines.Count == 0, "zero count");
});
Verify("duplicate ingredient rows aggregate before observing stock", () =>
{
    var r = Plan([Need(String, 2), Need(String, 3)], Stock(String, 1412, 4, 1));
    Check(r.Status == HandoffStatus.MissingMaterials && r.Lines.Count == 1 &&
          r.Lines[0].Required == 5 && r.Lines[0].Missing == 1, "must count once");
});
Verify("second material shortfall suppresses ALL proposed debits", () =>
{
    var r = Plan([Need(String, 3), Need(Wood, 5)],
        Stock(String, 1412, 40, 1), Stock(Wood, 1081, 4, 2));
    Check(r.Status == HandoffStatus.MissingMaterials &&
          r.Lines.Count == 2 && r.Lines.All(x => x.Proposed.Count == 0) &&
          r.RemoteOwners == 0, "partial recipe must fail closed");
});
Verify("duplicate source identity across same item is invalid", () =>
{
    var r = Plan([Need(String, 1)], Stock(String, 1412, 3, 1),
        Stock(String, 1412, 3, 1));
    Check(r.Status == HandoffStatus.InvalidSources && r.Lines.Count == 0, "duplicate");
});
Verify("a forged player source identity is rejected", () =>
{
    var r = Plan([Need(String, 1)], Stock(String, 1412, 3, 0, true));
    Check(r.Status == HandoffStatus.InvalidSources, "wrong player");
});
Verify("remote entry never reuses player entity id", () =>
{
    var r = Plan([Need(String, 1)], Stock(String, 1, 3, 1, false));
    Check(r.Status == HandoffStatus.InvalidSources, "remote player collision");
});
Verify("invalid owner and corrupted distances fail closed", () =>
{
    foreach (var bad in new[] {
        Stock(String, 0, 2, 1), Stock(String, 1412, 2, double.NaN),
        Stock(String, 1412, 2, double.PositiveInfinity), Stock(String, 1412, -1, 1) })
        Check(Plan([Need(String, 1)], bad).Status == HandoffStatus.InvalidSources, "bad source");
});
Verify("no sources means full shortage without fake partial allocations", () =>
{
    var r = Plan([Need(String, 2)]);
    Check(r.Status == HandoffStatus.MissingMaterials &&
          r.Lines[0].Missing == 2 && r.Lines[0].Proposed.Count == 0, "none");
});
Verify("shuffling chests keeps deterministic exact owner allocation", () =>
{
    var a = Stock(String, 1412, 3, 7);
    var b = Stock(String, 1081, 3, 7);
    var r1 = Plan([Need(String, 4)], a, b);
    var r2 = Plan([Need(String, 4)], b, a);
    Check(r1.Lines[0].Proposed.SequenceEqual(r2.Lines[0].Proposed) &&
          r1.Lines[0].Proposed[0].EntityId == 1081, "order");
});
Verify("overflown total costs are invalid", () =>
{
    var r = Plan([Need(String, long.MaxValue), Need(Wood, 1)]);
    Check(r.Status == HandoffStatus.InvalidSources, "total overflow");
});
Verify("overflown duplicate same-item rows are invalid", () =>
{
    var r = Plan([Need(String, long.MaxValue), Need(String, 1)]);
    Check(r.Status == HandoffStatus.InvalidSources, "item overflow");
});
Verify("empty bill is unknown, not a free craft", () =>
{
    Check(Plan([]).Status == HandoffStatus.UnknownIngredients, "empty");
});
Verify("zero player actor is an invalid snapshot", () =>
{
    Check(CraftHandoffPlanner.Inspect([Need(String, 1)], [], 0).Status ==
          HandoffStatus.InvalidSources, "zero actor");
});
Verify("no result ever claims native authorisation", () =>
{
    foreach (var r in new[] {
        Plan([Need(String, 3)], Stock(String, 1081, 6, 1)),
        Plan([Need(String, 3)], Stock(String, 1, 6, 0, true)),
        Plan([Need(String, 3)], Stock(String, 1081, 2, 1)),
        Plan([Need(0, 0)]) })
        Check(!r.NativePaymentPermitted, "never authoritative");
});
Console.WriteLine($"PASS: {passed} final investigation read-only handoff tests.");
