// Cross-base Storage (Kiln) — read-only foundation.
// No inventory writes, no recipe bypass, and no claim to extend Enshrouded's
// vanilla crafting/building source resolver. See README.md for API blockers.
using Kiln;

namespace CrossBaseStorage;

public sealed class CrossBaseStorageMod : Mod
{
    private CommandRegistration? _commands;
    private const int MaxShown = 12;

    // Bounded, per-player in-memory traces of VANILLA game actions. Never saved.
    // These events have already occurred (or will be handled by the engine);
    // we only observe them and never set Canceled or inject another action.
    private readonly Dictionary<uint, CraftRequestTrace> _craftRequests = new();
    private readonly Dictionary<uint, CraftOutcomeTrace> _craftOutcomes = new();
    private readonly Dictionary<uint, BuildTrace> _buildOutcomes = new();

    private sealed record CraftRequestTrace(DateTimeOffset When, uint Recipe, uint Station,
        int Amount, bool Canceled, bool Injected);
    private sealed record CraftOutcomeTrace(DateTimeOffset When, uint Recipe, uint Station,
        uint Workshop, CraftResult Result, int RawResult);
    private sealed record BuildTrace(DateTimeOffset When, BuildKind Kind, uint Item,
        BuildOutcome Result, WorldPosition Position);

    public override void OnLoad()
    {
        _commands = Commands.Register(
            "cbstore", HandleCommand, CommandPermission.Admin,
            usage: "<status|scan|templates|near|links <owner>|resolve <inventory>|stock item name|quote <amount> <item>|top|recipes [filter]|plan <recipe> [count]|lastcraft|lastbuild|trace [status|pair|payer|cost|journal|all]>",
            description: "Inspect loaded magic chest inventory and native craft/build requests (read-only).");
        if (Gameplay.Available && !Gameplay.Enable())
            Log.Warn("Cross-base Storage: kiln.gameplay present but events are not ready.");
        Log.Warn("Cross-base Storage: discovery prototype loaded. Vanilla cross-base crafting/building is NOT enabled; Kiln has no storage-source integration API yet.");
    }

    public override void OnUnload()
    {
        _commands?.Dispose();
        _craftRequests.Clear();
        _craftOutcomes.Clear();
        _buildOutcomes.Clear();
    }

    // The pinned Kiln.Gameplay emits vanilla player requests here. We record
    // them only. No cancellation, recipe injection, or stock movement.
    [Subscribe(EventPriority.Monitor, ReceiveCanceled = true)]
    private void OnCraftRequested(CraftEvent e)
    {
        if (!e.ByPlayer) return;
        Remember(_craftRequests, e.Operator.Value,
            new CraftRequestTrace(DateTimeOffset.UtcNow, e.RecipeId,
                e.Station.Value, e.Amount, e.Canceled, e.Injected));
    }

    [Subscribe(EventPriority.Monitor)]
    private void OnCraftCompleted(ItemCraftedEvent e)
    {
        if (!e.ByPlayer) return;
        Remember(_craftOutcomes, e.Operator.Value,
            new CraftOutcomeTrace(DateTimeOffset.UtcNow, e.RecipeId,
                e.Station.Value, e.WorkshopId, e.Result, e.RawResult));
    }

    [Subscribe(EventPriority.Monitor)]
    private void OnBuildCompleted(BuildEvent e)
    {
        if (!e.Player.IsPlayer) return;
        Remember(_buildOutcomes, e.Player.Value,
            new BuildTrace(DateTimeOffset.UtcNow, e.Kind, e.ItemId, e.Outcome, e.Position));
    }

    private static void Remember<T>(Dictionary<uint, T> traces, uint player, T value)
    {
        // Prevent a long-running multiplayer server collecting unbounded
        // per-player keys; these are diagnostics, not an audit log.
        if (!traces.ContainsKey(player) && traces.Count >= 64) traces.Clear();
        traces[player] = value;
    }

    private sealed record Chest(
        EntityId Entity, string Template, WorldPosition Position,
        int Slots, int UsedSlots, InventoryContents Contents, bool MagicByName);

    // Read-only template identification: the only Magic Chest naming rule is
    // the exact "_Magic" suffix (case-insensitive). Inventory capacity comes
    // from Items.Read and is never assumed to be 72 slots. This is still
    // a template heuristic, NOT the native game's crafting entitlement.

    private static List<Chest> ScanLoaded()
    {
        if (!Items.Available || !Spawn.Available || !World.Available ||
            Items.Status() is not { Read: true })
            throw new CommandException("Kiln.Items, Kiln.Spawn and Kiln.World must be ready inside a loaded world.");

        if (Saves.WorldId is null)
            throw new CommandException("Enter a saved Enshrouded world before scanning chest inventories.");

        var chests = new List<Chest>();
        foreach (var id in Items.FindInventories(InventoryKinds.Containers))
        {
            var inventory = Items.Read(id);
            if (inventory is not { Initialized: true } || inventory.IsPlayer || inventory.IsNpc)
                continue;

            var info = Spawn.Inspect(id);
            if (!StorageTemplate.IsStorageCandidate(info?.TemplateName, inventory.GenericSlots))
                continue;

            var position = info!.Position;
            var template = info.TemplateName!;
            chests.Add(new Chest(id, template, position, inventory.GenericSlots,
                inventory.UsedSlots, inventory, StorageTemplate.IsMagic(template)));
        }
        return chests;
    }

    private static string Where(Chest chest) =>
        $"{chest.Position.X:F1},{chest.Position.Y:F1},{chest.Position.Z:F1}";

    private static string Describe(Chest chest) =>
        $"entity={chest.Entity}, template={chest.Template}, slots={chest.UsedSlots}/{chest.Slots}, " +
        $"magicByTemplate={chest.MagicByName}, pos={Where(chest)}";

    private static void Status(CommandContext c)
    {
        c.Reply($"Cross-base Storage (Kiln) discovery-only prototype; world={Saves.WorldId ?? "(not loaded)"}, " +
                $"items={Items.Available}, spawn={Spawn.Available}, world={World.Available}, " +
                $"gameplay={Gameplay.Available}, craftEventHook={Gameplay.Status?.CraftHook}, buildEventHook={Gameplay.Status?.BuildHooks}. " +
                "Cross-base build/craft integration=NOT AVAILABLE in this Kiln SDK. " +
                "No item transfers, crafting injection or inventory changes are performed.");
    }

    private static void Scan(CommandContext c)
    {
        var chests = ScanLoaded();
        var probable = chests.Where(x => x.MagicByName).ToArray();
        var unique = probable.SelectMany(x => x.Contents.Slots.Where(s => !s.IsEmpty))
            .Select(s => s.Item).Distinct().Count();
        var units = probable.Sum(x => x.Contents.Slots.Where(s => !s.IsEmpty).Sum(s => (long)s.Count));

        c.Reply($"LOADED world only: {chests.Count} candidate storage chests, " +
                $"{probable.Length} magic-by-template candidates; {unique} item types, " +
                $"{units} units across those candidates. " +
                "This is a READ-ONLY sample, NOT cross-base crafting access. " +
                "Unloaded bases and magic upgrades without the '_Magic' suffix in their template may be absent.");

        foreach (var chest in probable.Take(MaxShown))
            c.Reply("MAGIC CANDIDATE " + Describe(chest));
        if (probable.Length > MaxShown)
            c.Reply($"... and {probable.Length - MaxShown} additional candidate magic chests.");
    }

    private static void Templates(CommandContext c)
    {
        var chests = ScanLoaded();
        var groups = chests.GroupBy(x => x.Template, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        c.Reply($"Loaded chest templates ({groups.Length} types). '_Magic' suffix classification is provisional:");
        foreach (var group in groups.Take(24))
            c.Reply($"{group.Key}: {group.Count()} loaded chests; " +
                    $"magicByTemplate={StorageTemplate.IsMagic(group.Key)}");
        if (groups.Length > 24)
            c.Reply($"... {groups.Length - 24} more templates.");
    }

    // Kiln.Items already exposes the flattened 8-slot inventory links for
    // each chest owner. This is a diagnostic relationship, not a native
    // crafting source permission, Magic flag or proof of payment.
    private static InventoryLinkIndex LoadedLinkIndex(IReadOnlyList<Chest> chests) =>
        InventoryLinkIndex.Build(chests.Select(chest => new InventoryLinkOwner(
            chest.Entity.Value, chest.MagicByName,
            chest.Contents.Inventories.Select(link => link.Inventory.Value).ToArray())));

    private static void Links(CommandContext c)
    {
        if (c.Count != 2)
            throw c.Usage("cbstore links <chest owner entity ID>");
        var id = c.Int(1);
        if (id <= 0) throw new CommandException("Expected a positive chest owner entity ID.");

        var chests = ScanLoaded();
        var index = LoadedLinkIndex(chests);
        if (!index.Valid)
            throw new CommandException("INVALID inventory ownership snapshot; malformed or duplicate owner/link IDs.");

        var chest = chests.FirstOrDefault(x => x.Entity.Value == (uint)id);
        if (chest is null)
            throw new CommandException($"Owner {id} is not a loaded compatible storage chest in this scan.");

        var links = chest.Contents.Inventories;
        c.Reply($"READ-ONLY linked inventories for chest owner={id}: {links.Count} links, " +
                $"magicByTemplate={chest.MagicByName}, template={chest.Template}, pos={Where(chest)}. " +
                "These are actual Kiln inventory links, NOT native crafting permissions.");
        for (var i = 0; i < links.Count && i < MaxShown; ++i)
        {
            var (linked, category) = links[i];
            var usedSlots = chest.Contents.Slots.Count(slot => slot.Link == i && !slot.IsEmpty);
            var units = chest.Contents.Slots
                .Where(slot => slot.Link == i && !slot.IsEmpty)
                .Sum(slot => (long)slot.Count);
            var match = index.Resolve(linked.Value);
            c.Reply($"  link[{i}] inventoryEntity={linked}, category={category}, " +
                    $"usedSlots={usedSlots}, units={units}, ownerMapping={match.Status}");
        }
        if (links.Count > MaxShown)
            c.Reply($"... {links.Count - MaxShown} more links not displayed.");
        c.Reply("No crafting, transaction, reservation or inventory mutation occurred.");
    }

    private static void Resolve(CommandContext c)
    {
        if (c.Count != 2)
            throw c.Usage("cbstore resolve <linked inventory entity ID>");
        var id = c.Int(1);
        if (id <= 0) throw new CommandException("Expected a positive linked inventory entity ID.");

        var chests = ScanLoaded();
        var index = LoadedLinkIndex(chests);
        var result = index.Resolve((uint)id);
        c.Reply($"READ-ONLY linked inventory lookup {id}: state={result.Status}, " +
                $"candidateOwners={index.OwnerCount}, linkedInventoryIds={index.InventoryCount}, " +
                $"ambiguousLinks={index.AmbiguousCount}.");
        foreach (var match in result.Matches.Take(MaxShown))
        {
            var chest = chests.First(x => x.Entity.Value == match.Owner);
            c.Reply($"  owner={match.Owner}, linkIndex={match.LinkIndex}, " +
                    $"magicByTemplate={match.MagicByTemplate}, template={chest.Template}, pos={Where(chest)}");
        }
        if (result.Matches.Count > MaxShown)
            c.Reply($"... {result.Matches.Count - MaxShown} more possible owners.");
        c.Reply(result.Status switch
        {
            InventoryLinkStatus.Matched => "Owner LINK identified in current loaded candidate scan. " +
                "This does NOT establish engine crafting eligibility or completed resource payment.",
            InventoryLinkStatus.NotFound => "NOT FOUND in loaded candidate storage chests. " +
                "The inventory may belong to a player, station, unrelated object or unscanned chest.",
            InventoryLinkStatus.Ambiguous => "AMBIGUOUS shared link: owner cannot be uniquely determined; do not attribute payment.",
            _ => "INVALID SNAPSHOT: malformed or duplicate link identities; no owner attribution is trusted."
        });
    }

    private static void Nearest(CommandContext c)
    {
        if (c.Source.Player.IsNone || World.Position(c.Source.Player) is not { } at)
            throw new CommandException("Enter a world as a player and stand next to a chest.");
        var nearest = ScanLoaded()
            .OrderBy(x => DistanceSquared(x.Position, at))
            .FirstOrDefault();
        if (nearest is null || DistanceSquared(nearest.Position, at) > 25)
            throw new CommandException("No compatible storage chest found within 5 metres.");
        c.Reply("NEAREST READ-ONLY: " + Describe(nearest));
        c.Reply("If the game treats this as a Magic Chest but magicByTemplate=false, " +
                "the name-based detector needs an engine-supported magic-chest flag.");
    }

    private static double DistanceSquared(WorldPosition a, WorldPosition b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);

    private static void Stock(CommandContext c)
    {
        if (c.Count < 2)
            throw c.Usage("cbstore stock <exact item name or item ID>");
        var query = string.Join(" ", c.Args.Skip(1)).Trim();
        var item = Items.Lookup(query) ??
            throw new CommandException($"Unknown catalogued item '{query}'.");

        var chests = ScanLoaded().Where(x => x.MagicByName)
            .Select(x => (Chest: x, Count: x.Contents.Count(item.Id)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ToArray();
        c.Reply($"READ-ONLY loaded magic-by-template stock: {item.Label} = " +
                $"{chests.Sum(x => (long)x.Count)} units in {chests.Length} candidate chests.");
        foreach (var (chest, count) in chests.Take(MaxShown))
            c.Reply($"{count} x {item.Label} @ {Where(chest)}, entity={chest.Entity}");
        if (chests.Length > MaxShown)
            c.Reply($"... plus {chests.Length - MaxShown} other candidate chests.");
    }

    // A hypothetical exact-item allocation, not an inventory transaction.
    // The engine has NOT approved any of these sources for crafting. There
    // is no reservation, world save or stock mutation.
    private static void Quote(CommandContext c)
    {
        if (c.Count < 3)
            throw c.Usage("cbstore quote <units> <exact item name or item ID>");
        var required = c.Int(1);
        if (required < 1 || required > 1_000_000)
            throw new CommandException("Quote units must be between 1 and 1,000,000.");
        var query = string.Join(" ", c.Args.Skip(2)).Trim();
        var item = Items.Lookup(query) ??
            throw new CommandException($"Unknown catalogued item '{query}'.");
        if (c.Source.Player.IsNone)
            throw new CommandException("Enter the world as a player before quoting sources.");

        var player = c.Source.Player;
        var playerStock = Items.Read(player)?.Count(item.Id) ?? 0;
        var pos = World.Position(player);
        var chests = ScanLoaded().Where(x => x.MagicByName).ToArray();
        var candidates = new List<QuoteSource>
        {
            new(player.Value, playerStock, 0, true)
        };
        foreach (var chest in chests)
        {
            var stock = chest.Contents.Count(item.Id);
            if (stock <= 0) continue;
            candidates.Add(new QuoteSource(chest.Entity.Value, stock,
                pos is { } p ? DistanceSquared(chest.Position, p) : double.PositiveInfinity, false));
        }

        var quote = MaterialQuotePlanner.QuoteExact(required, candidates);
        c.Reply($"READ-ONLY, NON-RESERVING source quote: {required} x {item.Label}; " +
                $"player={playerStock}, visibleSources={candidates.Count}, totalVisible={quote.Visible}, " +
                $"state={quote.Status}, shortage={quote.Missing}. " +
                "Sources are TEMPLATE GUESSES, not vanilla engine-eligible inventory providers.");
        if (quote.Status == QuoteStatus.InvalidSources)
        {
            c.Reply("REJECTED: duplicate identities or invalid counts/positions. No allocation is usable.");
            return;
        }
        if (quote.Status == QuoteStatus.Insufficient)
        {
            c.Reply("NO ALLOCATION: available stock is insufficient; partial debits are forbidden.");
            return;
        }
        foreach (var debit in quote.Debits.Take(MaxShown))
        {
            if (debit.IsPlayer)
            {
                c.Reply($"  PROPOSE {debit.Units} x {item.Label} from PLAYER entity={debit.EntityId}");
                continue;
            }
            var chest = chests.FirstOrDefault(x => x.Entity.Value == debit.EntityId);
            c.Reply($"  PROPOSE {debit.Units} x {item.Label} from CHEST entity={debit.EntityId}" +
                    (chest is null ? "" : $" @ {Where(chest)}"));
        }
        if (quote.Debits.Count > MaxShown)
            c.Reply($"... and {quote.Debits.Count - MaxShown} other candidate allocations.");
        c.Reply("PROPOSAL ONLY. No game crafting, transaction, reservation or stock mutation was performed; " +
                "remote source permissions, category choices and actual payment remain unsupported.");
    }

    private static void Top(CommandContext c)
    {
        var chests = ScanLoaded().Where(x => x.MagicByName);
        var totals = chests.SelectMany(x => x.Contents.Slots.Where(s => !s.IsEmpty))
            .GroupBy(s => s.Item)
            .Select(g => (Id: g.Key, Count: g.Sum(s => (long)s.Count)))
            .OrderByDescending(g => g.Count)
            .Take(MaxShown)
            .ToArray();
        if (totals.Length == 0)
        {
            c.Reply("No stock detected in loaded magic-by-template candidates.");
            return;
        }
        c.Reply("Top items in LOADED candidate magic chests (read-only):");
        foreach (var (id, count) in totals)
            c.Reply($"{count} x {Items.Get(id)?.Label ?? id.ToString()}");
    }

    private static void EnsureGameplay()
    {
        if (!Gameplay.Available)
            throw new CommandException("Kiln.Gameplay is unavailable on this runtime: recipe introspection cannot run.");
    }

    private static uint ParseRecipeId(string arg)
    {
        if (arg.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(arg.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var hex))
            return hex;
        if (uint.TryParse(arg, out var decimalId)) return decimalId;
        throw new CommandException("Recipe ID must be decimal or hex with 0x prefix. Example: cbstore plan 0x2cd4c47e");
    }

    private uint RecipeIdFromCommand(CommandContext c, string arg)
    {
        if (arg.Equals("last", StringComparison.OrdinalIgnoreCase))
        {
            if (c.Source.Player.IsNone || !_craftRequests.TryGetValue(c.Source.Player.Value, out var trace))
                throw new CommandException("No native craft request recorded for your player yet; craft one item first.");
            return trace.Recipe;
        }
        return ParseRecipeId(arg);
    }

    private static string Label(uint item)
    {
        if (item == 0) return "(category input - precise item selection unavailable)";
        var id = new ItemId(item);
        return Items.Get(id)?.Label ?? id.ToString();
    }

    // Read-only view of registered native recipe inputs versus server-readable
    // stock. This is NOT a promise that the vanilla UI can craft with the stock.
    private void Plan(CommandContext c)
    {
        EnsureGameplay();
        if (c.Count < 2) throw c.Usage("cbstore plan <recipeId|last> [quantity]");
        if (c.Arg(1).Equals("last", StringComparison.OrdinalIgnoreCase) &&
            (c.Source.Player.IsNone || !_craftRequests.ContainsKey(c.Source.Player.Value)))
        {
            c.Reply("No craft request was recorded. When the game's crafting UI disables the " +
                    "craft button, no request reaches Kiln. This is expected without local " +
                    "craftable materials; it does not mean this mod enables remote crafting. " +
                    "Use 'cbstore recipes [output name]' to discover a hand-crafting recipe, " +
                    "then 'cbstore plan 0x<recipeId> 1' to examine its ingredients without crafting.");
            return;
        }
        var id = RecipeIdFromCommand(c, c.Arg(1));
        var quantity = c.Count > 2 ? c.Int(2) : 1;
        if (quantity is < 1 or > 100)
            throw new CommandException("Requested recipe count must be 1..100 (read-only estimate).");
        var recipe = Gameplay.Recipe(id) ??
            throw new CommandException($"Unknown game recipe 0x{id:x8}.");
        var inventory = c.Source.Player.IsNone ? null : Items.Read(c.Source.Player);
        var magic = ScanLoaded().Where(chest => chest.MagicByName).ToArray();
        var remoteOk = true;
        var unknownCategory = false;

        c.Reply($"READ-ONLY recipe 0x{id:x8} x{quantity}; workshop=0x{recipe.Workshop:x8}, " +
                $"inputs={recipe.Inputs.Count}, outputs={recipe.Outputs.Count}, " +
                $"visibleMagicChests={magic.Length}. This does NOT enable native cross-base crafting.");

        foreach (var ingredient in recipe.Inputs)
        {
            if (ingredient.ItemId == 0)
            {
                unknownCategory = true;
                c.Reply($"UNRESOLVED CATEGORY/PLACEHOLDER ingredient (itemId=0, rawCount={ingredient.Count}): " +
                        "Kiln.Gameplay exposes only item ID and count, not the category choice or its true " +
                        "quantity. A raw count of 0 does NOT establish zero material cost; " +
                        "this ingredient cannot be evaluated from the current API.");
                continue;
            }

            var item = new ItemId(ingredient.ItemId);
            var required = (long)ingredient.Count * quantity;
            var playerCount = inventory?.Count(item) ?? 0;
            var sources = magic.Select(chest => (Chest: chest, Count: chest.Contents.Count(item)))
                .Where(source => source.Count > 0)
                .OrderByDescending(source => source.Count)
                .ToArray();
            var magicCount = sources.Sum(source => (long)source.Count);
            var total = (long)playerCount + magicCount;
            if (total < required) remoteOk = false;
            c.Reply($"{Label(ingredient.ItemId)} (0x{ingredient.ItemId:x8}): " +
                    $"need={required}, player={playerCount}, magicPool={magicCount} " +
                    $"({sources.Length} loaded chests), combined={total}, remaining={Math.Max(0, required - total)}.");
            foreach (var (chest, count) in sources.Take(3))
                c.Reply($"  source {count} @ {Where(chest)} entity={chest.Entity}");
        }
        c.Reply("Outputs (native registry): " +
                string.Join(", ", recipe.Outputs.Select(item =>
                    $"{item.Count * (long)quantity} x {Label(item.ItemId)}")));
        c.Reply(unknownCategory
            ? "INCOMPLETE RECIPE INPUT DATA: at least one native category/placeholder is unresolved. " +
              "Exact-item counts above are real, but full recipe cost and native craftability " +
              "cannot be determined without upstream category metadata."
            : remoteOk
                ? "ALL EXACT INGREDIENTS VISIBLE in player + scanned magic stock. Native UI requirements and resource deduction are not changed."
                : "SOME INGREDIENTS MISSING in player + scanned magic stock. Native UI and permissions not changed.");
    }

    private static void Recipes(CommandContext c)
    {
        EnsureGameplay();
        var query = string.Join(" ", c.Args.Skip(1)).Trim();
        var ids = Gameplay.WorkshopRecipes(0);
        var rows = ids.Select(Gameplay.Recipe).Where(recipe => recipe is not null)
            .Select(recipe => recipe!)
            .Where(recipe => string.IsNullOrWhiteSpace(query) ||
                recipe.Outputs.Any(output => Label(output.ItemId)
                    .Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxShown + 1).ToArray();
        c.Reply($"READ-ONLY hand-crafting (workshop=0) recipes matching '{query}': " +
                $"{Math.Min(rows.Length, MaxShown)} shown; other crafting stations have separate workshop IDs.");
        foreach (var recipe in rows.Take(MaxShown))
            c.Reply($"0x{recipe.Id:x8}: " +
                    string.Join(", ", recipe.Outputs.Select(output => $"{output.Count} x {Label(output.ItemId)}")) +
                    $"; inputs={recipe.Inputs.Count}; use cbstore plan 0x{recipe.Id:x8}");
        if (rows.Length > MaxShown)
            c.Reply("More results available; narrow the search filter.");
    }

    private void LastCraft(CommandContext c)
    {
        if (c.Source.Player.IsNone)
            throw new CommandException("Run this as an in-world player to inspect your latest native crafting events.");
        var player = c.Source.Player.Value;
        if (_craftRequests.TryGetValue(player, out var request))
            c.Reply($"LAST vanilla CRAFT request UTC={request.When:O}, " +
                    $"recipe=0x{request.Recipe:x8}, amount={request.Amount}, " +
                    $"station={request.Station}, canceled={request.Canceled}, injected={request.Injected}. " +
                    $"Try cbstore plan 0x{request.Recipe:x8}");
        else
            c.Reply("No native craft request recorded. The client may have disabled the " +
                    "craft button before submitting any action. Use 'cbstore recipes' to " +
                    "find a recipe ID, then 'cbstore plan <recipeId>' without crafting.");

        if (_craftOutcomes.TryGetValue(player, out var outcome))
            c.Reply($"LAST native CRAFT outcome UTC={outcome.When:O}, " +
                    $"recipe=0x{outcome.Recipe:x8}, station={outcome.Station}, " +
                    $"workshop=0x{outcome.Workshop:x8}, result={outcome.Result}, raw={outcome.RawResult}.");
        else
            c.Reply("No native crafting outcome recorded yet.");
    }

    private void LastBuild(CommandContext c)
    {
        if (c.Source.Player.IsNone)
            throw new CommandException("Run this as an in-world player to inspect your latest native building event.");
        if (!_buildOutcomes.TryGetValue(c.Source.Player.Value, out var trace))
        {
            c.Reply("No building outcome recorded; the client may reject unavailable materials before sending an action.");
            return;
        }
        c.Reply($"LAST vanilla BUILD outcome UTC={trace.When:O}: " +
                $"kind={trace.Kind}, item={Label(trace.Item)} (0x{trace.Item:x8}), " +
                $"outcome={trace.Result}, at={trace.Position.X:F1},{trace.Position.Y:F1},{trace.Position.Z:F1}. " +
                "The event is POST action; it does not expose the original crafting/building source list.");
    }

    // Native trace is OPTIONAL. This source formatter is compiled directly
    // into Cross-base Storage; no standalone observer or patched DLL required.
    // Standard Kiln reports UNAVAILABLE. All reads are managed reflection,
    // bounded, fail-closed; neither game transactions nor inventories change.
    private static void Trace(CommandContext c)
    {
        if (c.Count > 2)
            throw c.Usage("cbstore trace [status|pair|payer|cost|journal|all]");
        var mode = c.ArgOrDefault(1, "status")!.ToLowerInvariant();
        if (mode == "status")
        {
            c.Reply("Cross-base Storage optional native trace: read-only. " +
                    "No native service patches installed by this mod.");
            foreach (var action in new[] { "pair", "payer", "cost", "journal" })
            {
                var lines = CraftPaymentTrace.TraceReader.Read(typeof(Rules), action);
                c.Reply($"{action}: {lines[0]}");
            }
            c.Reply("Vanilla Kiln lacks these experimental APIs. " +
                    "Do not replace your automatically updated production runtime.");
            return;
        }

        if (mode == "all")
        {
            foreach (var action in new[] { "pair", "payer", "cost", "journal" })
            {
                c.Reply($"TRACE {action}:");
                foreach (var line in CraftPaymentTrace.TraceReader.Read(typeof(Rules), action))
                    c.Reply(line);
            }
            return;
        }
        if (mode is not ("pair" or "payer" or "cost" or "journal"))
            throw c.Usage("cbstore trace [status|pair|payer|cost|journal|all]");
        foreach (var line in CraftPaymentTrace.TraceReader.Read(typeof(Rules), mode))
            c.Reply(line);
    }

    private void HandleCommand(CommandContext c)
    {
        var verb = c.ArgOrDefault(0, "help")!.ToLowerInvariant();
        switch (verb)
        {
            case "help":
                c.Reply("cbstore status | scan | templates | near | links <owner> | resolve <inventory> | stock <item> | top | " +
                        "quote <units> <item> | recipes [output filter] | plan <recipeId|last> [count] | " +
                        "lastcraft | lastbuild | trace [status|pair|payer|cost|journal|all]. " +
                        "Read-only diagnostics; no cross-base crafting/building yet.");
                break;
            case "status": Status(c); break;
            case "scan": Scan(c); break;
            case "templates": Templates(c); break;
            case "near": Nearest(c); break;
            case "links": Links(c); break;
            case "resolve": Resolve(c); break;
            case "stock": Stock(c); break;
            case "quote": Quote(c); break;
            case "top": Top(c); break;
            case "recipes": Recipes(c); break;
            case "plan": Plan(c); break;
            case "lastcraft": LastCraft(c); break;
            case "lastbuild": LastBuild(c); break;
            case "trace": Trace(c); break;
            default: throw c.Usage($"Unknown Cross-base Storage action '{verb}'.");
        }
    }
}
