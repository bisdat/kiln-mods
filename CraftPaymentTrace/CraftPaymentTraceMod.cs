// SPDX-License-Identifier: MIT
// Optional managed-only view of native recipe-payment trace snapshots.
// Does not install hooks, change a craft outcome, or access raw game memory.
using Kiln;

namespace CraftPaymentTrace;

public sealed class CraftPaymentTraceMod : Mod
{
    private CommandRegistration? _command;

    public override void OnLoad()
    {
        _command = Commands.Register(
            "cbtrace", Handle, CommandPermission.Admin,
            usage: "<status|cost|journal|payer|all>",
            description: "Inspect OPTIONAL passive native recipe-payment snapshots (read-only).");
        Log.Warn("Craft Payment Trace 0.1.0: read-only OBSERVER only. " +
                 "The standard Kiln release does not contain the experimental cost/journal trace API. " +
                 "No native DLL or recipe hook is installed by this mod.");
    }

    public override void OnUnload() => _command?.Dispose();

    private static void Handle(CommandContext c)
    {
        var action = c.ArgOrDefault(0, "status")!.ToLowerInvariant();
        if (action == "status")
        {
            c.Reply("Cross-base native trace observer: read-only, no transaction interception, " +
                    "no storage mutation, no vanilla crafting override.");
            foreach (var item in new[] { "payer", "cost", "journal" })
            {
                var lines = TraceReader.Read(typeof(Rules), item);
                c.Reply($"{item}: {lines[0]}");
                if (lines[0].StartsWith("UNAVAILABLE:", StringComparison.Ordinal))
                    c.Reply(lines[0]);
            }
            c.Reply("Only a separately verified, matching experimental Kiln.Rules native+managed " +
                    "pair can supply raw traces. Do not replace your normal Kiln runtime for this command.");
            return;
        }
        if (action == "all")
        {
            foreach (var name in new[] { "payer", "cost", "journal" })
                foreach (var line in TraceReader.Read(typeof(Rules), name)) c.Reply(line);
            return;
        }
        if (action is not ("payer" or "cost" or "journal"))
            throw c.Usage($"Unknown Craft Payment Trace action '{action}'.");
        foreach (var line in TraceReader.Read(typeof(Rules), action)) c.Reply(line);
    }
}
