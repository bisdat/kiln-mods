using CraftPaymentTrace;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}
var passed = 0;
void Verify(string name, Action test)
{
    test();
    Console.WriteLine($"PASS: {name}");
    passed++;
}
static string Dump(Type type, string action) =>
    string.Join("\n", TraceReader.Read(type, action));

Verify("unmodified Kiln missing native trace API returns clear unavailable message", () =>
{
    var text = Dump(typeof(StandardRules), "cost");
    Check(text.Contains("UNAVAILABLE") && text.Contains("Standard Kiln"), text);
});
Verify("optional native service with no snapshot stays unavailable", () =>
{
    var text = Dump(typeof(EmptyRules), "cost");
    Check(text.Contains("UNAVAILABLE") && text.Contains("no snapshot"), text);
});
Verify("managed optional trace errors do not propagate into host command", () =>
{
    var text = Dump(typeof(FaultyRules), "cost");
    Check(text.Contains("UNAVAILABLE") && text.Contains("TargetInvocationException"), text);
});
Verify("readable native cost snapshot shows owner, payer, multiplier, result", () =>
{
    var text = Dump(typeof(FakeRules), "cost");
    Check(text.Contains("InventoryPay") && text.Contains("owner=1") &&
          text.Contains("multiplier=3") && text.Contains("nativeResult=0"), text);
});
Verify("native cost sample prints raw item and count without claiming debit", () =>
{
    var text = Dump(typeof(FakeRules), "cost");
    Check(text.Contains("rawCost[0] item=0x0000012a count=3") &&
          text.Contains("NOT debits"), text);
});
Verify("unresolved item or zero cost count is explicitly not free", () =>
{
    var text = Dump(typeof(FakeRules), "cost");
    Check(text.Contains("UNRESOLVED CATEGORY/PLACEHOLDER (not free)"), text);
});
Verify("invalid cost snapshot must suppress untrusted cost rows", () =>
{
    var text = Dump(typeof(InvalidRules), "cost");
    Check(text.Contains("status=invalid") && !text.Contains("rawCost[0]"), text);
});
Verify("never-observed trace must suppress untrusted cost rows", () =>
{
    var text = Dump(typeof(NeverRules), "cost");
    Check(text.Contains("not-yet-observed") && !text.Contains("rawCost[0]"), text);
});
Verify("bounded reader prints at most eight rows and records truncation", () =>
{
    var text = Dump(typeof(TruncatedRules), "cost");
    Check(text.Contains("TRUNCATED") && text.Contains("rawCost[7]") &&
          !text.Contains("rawCost[8]"), text);
});
Verify("journal identifies underlying inventory entities not chest owner", () =>
{
    var text = Dump(typeof(FakeRules), "journal");
    Check(text.Contains("RecipePayAlt") && text.Contains("STAGED") &&
          text.Contains("inventoryEntity=1421") && text.Contains("not a chest owner"), text);
});
Verify("invalid journal suppresses staged operations", () =>
{
    var text = Dump(typeof(InvalidRules), "journal");
    Check(text.Contains("status=invalid") && !text.Contains("staged inventoryEntity="), text);
});
Verify("payer counters never promise atomic correlations or remote crafting", () =>
{
    var text = Dump(typeof(FakeRules), "payer");
    Check(text.Contains("InventoryPay calls=4") &&
          text.Contains("RecipePayAlt calls=2") &&
          text.Contains("NOT correlated atomic"), text);
});
Verify("a matched raw cost and native journal share one stable call identifier", () =>
{
    var text = Dump(typeof(MatchedRules), "pair");
    Check(text.Contains("SAME-CALL") && text.Contains("sampleId=29") &&
          text.Contains("rawCost[0] item=0x0000012a count=3") &&
          text.Contains("staged inventoryEntity=1421") &&
          text.Contains("not proven committed"), text);
});

Verify("pairing rejects independently mismatched payer call metadata", () =>
{
    var text = Dump(typeof(MismatchedRules), "pair");
    Check(text.Contains("INVALID OR UNAVAILABLE") && !text.Contains("rawCost[0]") &&
          !text.Contains("staged inventoryEntity="), text);
});

Verify("invalid native pair never prints ingredient or journal data", () =>
{
    var text = Dump(typeof(InvalidPairRules), "pair");
    Check(text.Contains("pairStatus=invalid") &&
          text.Contains("INVALID OR UNAVAILABLE") && !text.Contains("rawCost[0]"), text);
});

Verify("vanilla Kiln does not expose the new optional paired API", () =>
{
    var text = Dump(typeof(StandardRules), "pair");
    Check(text.Contains("UNAVAILABLE") && text.Contains("TraceRecipePayment"), text);
});

Verify("unsupported actions throw instead of resolving unexpected reflection methods", () =>
{
    try { TraceReader.Read(typeof(FakeRules), "free"); }
    catch (ArgumentException) { return; }
    throw new Exception("unsupported action should fail");
});
Console.WriteLine($"PASS: {passed} bounded reflection/read-only trace formatting tests.");

public sealed record FakeCostRow(uint RawItemId, uint RawCount);
public sealed record FakeCost(
    uint Status, ulong Calls, ulong Rejected, uint PayerKind, uint Owner,
    uint TransactionOperator, uint Multiplier, uint NativeResult,
    uint RawEntryCount, uint Flags, IReadOnlyList<FakeCostRow> Rows);
public sealed record FakeSource(uint InventoryEntity, uint StagedUnits);
public sealed record FakeJournal(
    uint Status, ulong Calls, ulong Rejected, uint PayerKind, uint Owner,
    uint NativeResult, uint NewOperations, uint TakeOperations,
    uint DistinctInventories, IReadOnlyList<FakeSource> Sources);
public sealed record FakePayer(
    ulong PrimaryCalls, ulong AlternativeCalls, ulong UiCalls,
    uint LastPrimaryOwner, uint LastPrimaryResult,
    uint LastAlternativeOwner, uint LastAlternativeResult,
    uint LastUiRecipe, uint LastUiVanillaAmount, uint LastUiCap,
    uint InstalledFlags);
public sealed record FakePair(
    uint Status, ulong Calls, ulong Rejected, ulong SampleId,
    FakeCost Costs, FakeJournal Journal);
public static class MatchedRules
{
    public static FakePair TraceRecipePayment() =>
        new(1, 29, 0, 29,
            new FakeCost(1, 0, 0, 1, 1, 1, 1, 0, 1, 1, [new(0x12a, 3)]),
            new FakeJournal(1, 0, 0, 1, 1, 0, 1, 1, 1, [new(1421, 3)]));
}
public static class MismatchedRules
{
    public static FakePair TraceRecipePayment() =>
        new(1, 29, 0, 29,
            new FakeCost(1, 0, 0, 1, 1, 1, 1, 0, 1, 1, [new(0x12a, 3)]),
            new FakeJournal(1, 0, 0, 2, 1, 0, 1, 1, 1, [new(1421, 3)]));
}
public static class InvalidPairRules
{
    public static FakePair TraceRecipePayment() =>
        new(2, 29, 1, 29,
            new FakeCost(2, 0, 0, 1, 1, 1, 1, 0, 1, 1, [new(0x12a, 3)]),
            new FakeJournal(1, 0, 0, 1, 1, 0, 1, 1, 1, [new(1421, 3)]));
}
public static class StandardRules { }
public static class EmptyRules
{
    public static FakeCost? TraceRecipeCostRows() => null;
}
public static class FaultyRules
{
    public static FakeCost TraceRecipeCostRows() => throw new InvalidOperationException("synthetic");
}
public static class FakeRules
{
    public static FakeCost TraceRecipeCostRows() =>
        new(1, 4, 0, 1, 1, 1, 3, 0, 3, 1,
            [new(0x12a, 3), new(0, 1), new(0x37, 0)]);
    public static FakeJournal TraceRecipeJournal() =>
        new(1, 2, 0, 2, 1, 0, 2, 1, 1, [new(1421, 3)]);
    public static FakePayer TraceCraftPayments() =>
        new(4, 2, 10, 1, 0, 1, 0, 0xa, 1, 5, 7);
}
public static class InvalidRules
{
    public static FakeCost TraceRecipeCostRows() =>
        new(2, 4, 1, 1, 1, 1, 1, 0, 1, 1, [new(0x12a, 100)]);
    public static FakeJournal TraceRecipeJournal() =>
        new(2, 4, 1, 1, 1, 0, 1, 1, 1, [new(1421, 3)]);
}
public static class NeverRules
{
    public static FakeCost TraceRecipeCostRows() =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 1, [new(0x12a, 100)]);
}
public static class TruncatedRules
{
    public static FakeCost TraceRecipeCostRows() =>
        new(3, 1, 0, 1, 1, 1, 1, 0, 9, 1,
            Enumerable.Range(0, 12).Select(i => new FakeCostRow((uint)(i + 1), 1)).ToArray());
}
