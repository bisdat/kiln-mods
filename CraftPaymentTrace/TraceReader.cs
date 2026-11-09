// SPDX-License-Identifier: MIT
// Inspect a read-only, OPTIONAL native recipe trace via public managed
// Rules APIs. No native pointers, game memory, or inventory writes.
// Reflection keeps this diagnostic binary compatible with ordinary Kiln
// releases that do not provide experimental TraceRecipe* methods.
using System.Globalization;
using System.Reflection;

namespace CraftPaymentTrace;

internal static class TraceReader
{
    public static IReadOnlyList<string> Read(Type api, string action)
    {
        ArgumentNullException.ThrowIfNull(api);
        var methodName = action switch
        {
            "cost" => "TraceRecipeCostRows",
            "journal" => "TraceRecipeJournal",
            "payer" => "TraceCraftPayments",
            "pair" => "TraceRecipePayment",
            _ => throw new ArgumentException("Expected cost, journal or payer", nameof(action))
        };
        var method = api.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static,
            binder: null, types: Type.EmptyTypes, modifiers: null);
        if (method is null)
            return [$"UNAVAILABLE: {methodName} is not in the installed Kiln.Rules managed API. " +
                    "Standard Kiln does not include our native trace experiment. " +
                    "No game DLLs have been changed by this mod."];
        object? result;
        try
        {
            result = method.Invoke(null, null);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException
                                      or TypeLoadException or InvalidOperationException)
        {
            return [$"UNAVAILABLE: {methodName} raised {ex.GetType().Name}. " +
                    "The optional trace may not match the running Kiln service; no native fallback attempted."];
        }
        if (result is null)
            return [$"UNAVAILABLE: {methodName} returned no snapshot. " +
                    "This usually means the matching instrumented native service is not loaded."];
        return action switch
        {
            "cost" => FormatCost(result),
            "journal" => FormatJournal(result),
            "pair" => FormatPair(result),
            _ => FormatPayer(result)
        };
    }

    private static string Text(object snapshot, string property)
    {
        var value = snapshot.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(snapshot);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?";
    }

    private static ulong Number(object snapshot, string property) =>
        ulong.TryParse(Text(snapshot, property), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var n) ? n : 0;

    private static IReadOnlyList<object> Entries(object snapshot, string property)
    {
        var value = snapshot.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(snapshot);
        if (value is not System.Collections.IEnumerable elements) return [];
        // No more than eight native samples can be returned; limit even
        // if a malformed provider supplies something larger or infinite.
        var output = new List<object>(8);
        foreach (var item in elements)
        {
            if (output.Count >= 8) break;
            if (item is not null) output.Add(item);
        }
        return output;
    }

    private static string PayerName(ulong kind) =>
        kind switch { 1 => "InventoryPay", 2 => "RecipePayAlt", _ => "unknown" };

    private static string Status(ulong status) =>
        status switch { 0 => "not-yet-observed", 1 => "readable", 2 => "invalid",
                        3 => "truncated", _ => "unknown" };

    private static IReadOnlyList<string> FormatCost(object s)
    {
        var status = Number(s, "Status");
        var rows = new List<string>
        {
            "READ-ONLY raw payer-cost snapshot (NOT authoritative ingredients, NOT debits).",
            $"status={Status(status)} calls={Text(s,"Calls")} rejected={Text(s,"Rejected")} " +
                $"payer={PayerName(Number(s,"PayerKind"))} owner={Text(s,"Owner")} " +
                $"operator={Text(s,"TransactionOperator")} multiplier={Text(s,"Multiplier")} " +
                $"nativeResult={Text(s,"NativeResult")} rawRows={Text(s,"RawEntryCount")}"
        };
        if (status == 2 || status == 0)
        {
            rows.Add("No reliable cost rows to display.");
            return rows;
        }
        var sample = Entries(s, "Rows");
        for (var i = 0; i < sample.Count; ++i)
        {
            var item = Number(sample[i], "RawItemId");
            var count = Text(sample[i], "RawCount");
            rows.Add($"rawCost[{i}] item=0x{item:x8} count={count}" +
                     (item == 0 || count == "0" ? " UNRESOLVED CATEGORY/PLACEHOLDER (not free)" : ""));
        }
        if (status == 3) rows.Add("TRUNCATED: at most eight raw rows shown.");
        rows.Add("Cost row offsets remain a hypothesis until observed against known paid crafts.");
        return rows;
    }

    private static IReadOnlyList<string> FormatJournal(object s)
    {
        var status = Number(s, "Status");
        var rows = new List<string>
        {
            "READ-ONLY native payer journal (STAGED operations, NOT committed payment).",
            $"status={Status(status)} calls={Text(s,"Calls")} rejected={Text(s,"Rejected")} " +
                $"payer={PayerName(Number(s,"PayerKind"))} owner={Text(s,"Owner")} " +
                $"nativeResult={Text(s,"NativeResult")} newOps={Text(s,"NewOperations")} " +
                $"takeOps={Text(s,"TakeOperations")} distinctInventoryEntities={Text(s,"DistinctInventories")}"
        };
        if (status is 0 or 2)
        {
            rows.Add("No reliable staged source operations to display.");
            return rows;
        }
        var sources = Entries(s, "Sources");
        foreach (var src in sources)
            rows.Add($"staged inventoryEntity={Text(src,"InventoryEntity")} " +
                     $"units={Text(src,"StagedUnits")} (not a chest owner)");
        if (status == 3) rows.Add("TRUNCATED: at most eight native inventory entities shown.");
        rows.Add("Native cost and journal snapshots can be from different concurrent calls.");
        return rows;
    }

    private static IReadOnlyList<string> FormatPair(object snapshot)
    {
        // The native trace copies both inputs from ONE original payer invocation.
        // Reject mismatched or malformed structures rather than silently
        // re-pairing separately sampled native cost and journal globals.
        var cost = snapshot.GetType().GetProperty("Costs")?.GetValue(snapshot);
        var journal = snapshot.GetType().GetProperty("Journal")?.GetValue(snapshot);
        var status = Number(snapshot, "Status");
        var lines = new List<string>
        {
            "READ-ONLY SAME-CALL recipe payment trace; raw costs are UNVERIFIED and " +
            "journal takes are STAGED (not proven committed).",
            $"pairStatus={Status(status)} sampleId={Text(snapshot,"SampleId")} " +
            $"calls={Text(snapshot,"Calls")} rejected={Text(snapshot,"Rejected")}"
        };
        if (cost is null || journal is null ||
            status is not (1 or 3) ||
            Number(cost, "PayerKind") != Number(journal, "PayerKind") ||
            Number(cost, "Owner") != Number(journal, "Owner") ||
            Number(cost, "NativeResult") != Number(journal, "NativeResult") ||
            Number(cost, "Status") is not (1 or 3) ||
            Number(journal, "Status") is not (1 or 3))
        {
            lines.Add("INVALID OR UNAVAILABLE: no trustworthy same-call costs/journal to display.");
            return lines;
        }
        lines.AddRange(FormatCost(cost));
        lines.AddRange(FormatJournal(journal));
        lines.Add("Matched by native call stack, NOT by comparing separate last-seen globals. " +
                  "Final game transaction COMMIT/ROLLBACK remains unknown.");
        return lines;
    }

    private static IReadOnlyList<string> FormatPayer(object s) =>
    [
        "READ-ONLY original payer invocation counters (no native hook installed by this mod).",
        $"InventoryPay calls={Text(s,"PrimaryCalls")} lastOwner={Text(s,"LastPrimaryOwner")} " +
            $"lastResult={Text(s,"LastPrimaryResult")}",
        $"RecipePayAlt calls={Text(s,"AlternativeCalls")} lastOwner={Text(s,"LastAlternativeOwner")} " +
            $"lastResult={Text(s,"LastAlternativeResult")}",
        $"client UI calls={Text(s,"UiCalls")} lastRecipe=0x{Number(s,"LastUiRecipe"):x8} " +
            $"vanillaAmount={Text(s,"LastUiVanillaAmount")} cap={Text(s,"LastUiCap")}",
        "Counters and last values are NOT correlated atomic transaction traces."
    ];
}
