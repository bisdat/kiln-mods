// SPDX-License-Identifier: MIT
// Independently implemented for Kiln. ShroudLink gemforge (MIT, bjck) informed the
// proximity/loan approach; all inventory operations use Kiln's transactional Items.Move.
using Kiln;

namespace GemForgeStorage;

/// <summary>
/// Loans gem instances and Archaic Essence from loaded Magic Chests while a player
/// uses a Gem Forge. Nothing is spawned or destroyed by this mod.
/// </summary>
public sealed class GemForgeStorageMod : Mod
{
    // Placed props can have an origin well behind their interaction point.
    private const double ReachMeters = 8;
    private const double LeaveMeters = 16;
    // Item ID of the placeable Gem Forge, verified in Kiln's game item catalogue.
    // A placed prop may expose UsedItem without a GemForge template name.
    private const uint PlaceableGemForgeItem = 3250908839u;
    private const int MaxEssencePerType = 2000;
    private const int MaxGemLoans = 24;
    private const int ReserveGenericSlots = 4;
    private const int MaxChestScan = 256;

    private sealed record TransferPlan(
        EntityId Chest, SlotId SourceSlot, ItemId Item, int Count, EntityId Instance, bool Gem);

    private sealed class Loan(TransferPlan plan, int lent)
    {
        public TransferPlan Plan { get; } = plan;
        public int Lent { get; } = lent;
        public int Returned { get; set; }
        public int Failures { get; set; }
        public DateTimeOffset RetryAfter { get; set; }
        public bool OriginalRejected { get; set; }
        public HashSet<EntityId> RejectedFallbacks { get; } = [];
        public string? LastError { get; set; }
    }

    private sealed class Session(Player player, EntityId forge, string world, Queue<TransferPlan> plans,
        Dictionary<ItemId, int> own)
    {
        public EntityId Player { get; } = player.Entity;
        public uint Handle { get; } = player.Handle;
        public ulong SteamId { get; } = player.SteamId;
        public EntityId Forge { get; } = forge;
        public string World { get; } = world;
        public Queue<TransferPlan> Plans { get; } = plans;
        public Dictionary<ItemId, int> Original { get; } = own;
        public List<Loan> Loans { get; } = [];
        public bool Returning { get; set; }
        public bool ReturnInitialized { get; set; }
        // A rejected move must stay in the queue instead of losing its ledger.
        public Queue<Loan> ReturnQueue { get; } = new();
        public bool AllowOtherChests { get; set; }
        public Task<MoveResult>? Pending { get; set; }
        public TransferPlan? PendingLoan { get; set; }
        public Loan? PendingReturn { get; set; }
        public EntityId PendingDestination { get; set; }
        public int RequestedReturn { get; set; }
    }

    private readonly Dictionary<EntityId, Session> _sessions = [];
    private readonly HashSet<EntityId> _manualReturn = [];
    private CommandRegistration? _command;
    private string? _world;
    private double _elapsed;
    private bool _unloading;
    private bool _warned;

    public override void OnLoad()
    {
        _command = Commands.Register("gforge", Command, CommandPermission.Everyone,
            usage: "[status|scan|probe|return|return-any]", description: "Magic Chest inventory access at Gem Forges.");
        Context.Tick.Pre += Tick;
        Log.Info("Gem Forge Storage v0.1.3 loaded: loaded Magic Chests only; Archaic Essence + weapon gems, Kiln.Items.Move.");
    }

    public override void OnUnload()
    {
        _unloading = true;
        _command?.Dispose();
        if (_sessions.Values.Any(s => s.Loans.Count > 0))
            Log.Warn("Gem Forge Storage unloaded with active loans. Items remain in player backpacks; no items were deleted.");
        _sessions.Clear();
        _manualReturn.Clear();
    }

    private static bool Essence(ItemInfo? item) =>
        item is not null &&
        item.Name.StartsWith("Material_T", StringComparison.Ordinal) &&
        item.Name.EndsWith("_Fognest_Ressource", StringComparison.Ordinal);

    private static bool MagicChest(EntityId entity)
    {
        var name = Spawn.Inspect(entity)?.TemplateName;
        return name is not null &&
            name.Contains("Storage", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith("_Magic", StringComparison.OrdinalIgnoreCase);
    }

    // Only inventories present in the active world can be moved through Kiln.Items.
    // Building Tools' remote unloaded-chest resolver is not part of Kiln.Items.
    private static List<(EntityId Id, InventoryContents Inventory)> LoadedMagicChests(WorldPosition forge)
    {
        var candidates = new List<(EntityId, InventoryContents, double)>();
        foreach (var entity in Items.FindInventories(InventoryKinds.Containers | InventoryKinds.NonEmpty))
        {
            if (!MagicChest(entity)) continue;
            if (Items.Read(entity) is not { Initialized: true, IsPlayer: false, IsNpc: false } inv)
                continue;
            var dist = World.Position(entity)?.DistanceTo(forge) ?? double.MaxValue;
            candidates.Add((entity, inv, dist));
        }
        return candidates.OrderBy(c => c.Item3).Take(MaxChestScan)
            .Select(c => (c.Item1, c.Item2)).ToList();
    }

    private static Session BuildSession(Player player, EntityId forge, string world)
    {
        var playerInventory = Items.Read(player.Entity)
            ?? throw new InvalidOperationException("Player inventory not loaded.");
        var at = World.Position(forge)
            ?? throw new InvalidOperationException("Gem Forge position not available.");
        var own = new Dictionary<ItemId, int>();
        foreach (var slot in playerInventory.Slots.Where(s => !s.IsEmpty))
        {
            if (Essence(Items.Get(slot.Item)))
                own[slot.Item] = own.GetValueOrDefault(slot.Item) + slot.Count;
        }

        var plannedEssence = new Dictionary<ItemId, int>();
        var gemCount = 0;
        var plans = new Queue<TransferPlan>();
        foreach (var (chest, inventory) in LoadedMagicChests(at))
        {
            foreach (var slot in inventory.Slots.Where(s => !s.IsEmpty))
            {
                var info = Items.Get(slot.Item);
                if (Essence(info))
                {
                    var remaining = MaxEssencePerType - plannedEssence.GetValueOrDefault(slot.Item);
                    var amount = Math.Min(slot.Count, Math.Max(0, remaining));
                    if (amount <= 0) continue;
                    plans.Enqueue(new(chest, slot.Slot, slot.Item, amount, default, false));
                    plannedEssence[slot.Item] = plannedEssence.GetValueOrDefault(slot.Item) + amount;
                }
                else if (info?.Category == ItemCategory.WeaponGem &&
                    !slot.ItemEntity.IsNone && gemCount < MaxGemLoans)
                {
                    // Only a distinct game item entity can be safely returned as the same gem.
                    plans.Enqueue(new(chest, slot.Slot, slot.Item, 1, slot.ItemEntity, true));
                    gemCount++;
                }
            }
        }
        return new Session(player, forge, world, plans, own);
    }

    private static bool IsPlacedGemForge(EntityId entity) =>
        World.TryRead<uint>(entity, "UsedItem", out var itemId) && itemId == PlaceableGemForgeItem;

    private static EntityId NearestForge(EntityId player)
    {
        // Use the native Kiln ECS and placed-prop inventory data independently.
        // The earlier ShroudLink-inspired component-only lookup missed real
        // placed forges in the hosted client world.
        var at = World.Position(player);
        if (at is null) return default;
        var byComponent = Nearby.Entities(player, ReachMeters, "ShroudForge").FirstOrDefault();
        if (!byComponent.IsNone) return byComponent;

        var byTemplate = Spawn.FindByName("GemForge", at, ReachMeters, max: 64)
            .FirstOrDefault(e => Spawn.Inspect(e)?.TemplateName is { } name &&
                (name.EndsWith("_Player_GemForge", StringComparison.OrdinalIgnoreCase) ||
                 name.EndsWith("_GemForge", StringComparison.OrdinalIgnoreCase)));
        if (!byTemplate.IsNone) return byTemplate;

        // Fallback: player-placed game props carry UsedItem (four-byte item ID).
        // Their runtime template may not have the same name as their placeable.
        var closest = default(EntityId);
        var minDistance = ReachMeters;
        foreach (var entity in World.Find("UsedItem"))
        {
            if (!IsPlacedGemForge(entity) || World.Position(entity) is not { } pos)
                continue;
            var distance = pos.DistanceTo(at.Value);
            if (distance <= minDistance)
            {
                minDistance = distance;
                closest = entity;
            }
        }
        return closest;
    }

    /// <summary>Read-only ECS census. This does not require an active loan or a detected forge.</summary>
    private static void ProbeForge(CommandContext command, EntityId player)
    {
        var at = World.Position(player);
        if (at is null)
        {
            command.Reply("Gem Forge probe: your character has no server-world position.");
            return;
        }

        var components = World.Find("ShroudForge");
        var templates = Spawn.FindByName("GemForge", max: 256);
        var placed = World.Find("UsedItem").Where(IsPlacedGemForge).ToArray();
        command.Reply($"Gem Forge probe: player={player}, position={at}; " +
            $"ShroudForge component={components.Count}, GemForge templates={templates.Count}, " +
            $"GemForge placed-item={placed.Length}, reach={ReachMeters} m.");

        var evidence = new Dictionary<EntityId, string>();
        foreach (var e in components) evidence.TryAdd(e, "ShroudForge");
        foreach (var e in templates) evidence.TryAdd(e, "template");
        foreach (var e in placed) evidence.TryAdd(e, "UsedItem");
        var nearest = evidence
            .Select(entry => (Id: entry.Key, Source: entry.Value,
                Position: World.Position(entry.Key)))
            .OrderBy(entry => entry.Position is { } pos ? pos.DistanceTo(at.Value) : double.MaxValue)
            .Take(6)
            .Select(entry =>
                $"{entry.Id}: " +
                (entry.Position is { } pos ? $"{pos.DistanceTo(at.Value):0.0}m" : "no position") +
                $" [{entry.Source}] {Spawn.Inspect(entry.Id)?.TemplateName ?? "(no template)"}")
            .ToArray();
        command.Reply(nearest.Length > 0 ? $"Nearest forge candidates: {string.Join("; ", nearest)}" :
            "No forge candidates by ECS component, template or placeable item. Check the placed entity in Kiln.");
    }

    private void Tick(in TickInfo tick)
    {
        if (_unloading) return;
        _elapsed += tick.WallDelta.TotalSeconds;
        try
        {
            if (!Items.MovesAvailable || !World.Available || !Spawn.Available || !Players.Available)
            {
                if (!_warned)
                {
                    Log.Warn($"Gem Forge Storage waiting for services: moves={Items.MovesAvailable}, " +
                        $"world={World.Available}, spawn={Spawn.Available}, players={Players.Available}.");
                    _warned = true;
                }
                return;
            }
            if (_warned)
                Log.Info("Gem Forge Storage: all required Kiln services are now available.");
            _warned = false;
            var world = Saves.WorldId;
            if (world is null)
            {
                if (_sessions.Count > 0) DropSessions("world closed");
                return;
            }
            if (_world != world)
            {
                if (_world is not null) DropSessions("world changed");
                _world = world;
            }

            if (_elapsed >= 1)
            {
                _elapsed = 0;
                Discover(world);
            }
            foreach (var session in _sessions.Values.ToArray())
                Pump(session);
        }
        catch (Exception e)
        {
            // Never let a failure in an optional inventory bridge fault other mods.
            Log.Warn($"Gem Forge Storage tick: {e.Message}");
        }
    }

    private void DropSessions(string why)
    {
        foreach (var s in _sessions.Values.Where(s => s.Loans.Count > 0))
            Log.Warn($"Gem Forge Storage: {why}; player {s.Player} keeps loaned inventory. No compensating items created.");
        _sessions.Clear();
        _manualReturn.Clear();
    }

    private void Discover(string world)
    {
        foreach (var session in _sessions.Values.ToArray())
        {
            var player = Players.Find(session.Player);
            if (player is not { InGame: true } ||
                (session.Handle != 0 && session.Handle != player.Handle) ||
                (session.SteamId != 0 && player.SteamId != 0 && session.SteamId != player.SteamId))
            {
                // The game saves a departing player's inventory before its leave event.
                // Do not attempt to debit a disconnected character's old entity ID.
                Log.Warn($"Gem Forge Storage: player {session.Player} left; any loaned inventory remains in their backpack.");
                _sessions.Remove(session.Player);
                continue;
            }
            if (!session.Returning && World.Position(session.Player) is { } at &&
                (World.Position(session.Forge) is not { } forge ||
                 at.DistanceTo(forge) > LeaveMeters))
            {
                session.Returning = true;
                session.Plans.Clear();
            }
        }

        foreach (var player in Players.All.Where(p => p.InGame))
        {
            if (_sessions.ContainsKey(player.Entity)) continue;
            var forge = NearestForge(player.Entity);
            if (forge.IsNone)
            {
                _manualReturn.Remove(player.Entity);
                continue;
            }
            if (_manualReturn.Contains(player.Entity)) continue;
            if (Items.Read(player.Entity) is not { Initialized: true }) continue;
            var session = BuildSession(player, forge, world);
            _sessions[player.Entity] = session;
            if (session.Plans.Count > 0)
                Log.Info($"Gem Forge Storage: player {player.Entity} near forge {forge}; {session.Plans.Count} eligible source stacks; starting loans.");
            else
                Log.Info($"Gem Forge Storage: player {player.Entity} near forge {forge}; no eligible items in loaded Magic Chests. Use gforge scan.");
        }
    }

    private void Pump(Session session)
    {
        if (!_sessions.ContainsKey(session.Player)) return;
        if (session.Pending is { } pending)
        {
            if (!pending.IsCompleted) return;
            session.Pending = null;
            try
            {
                var result = pending.GetAwaiter().GetResult(); // completed: does not wait or leave the tick
                if (session.PendingLoan is { } plan)
                {
                    if (result.Done > 0)
                        session.Loans.Add(new Loan(plan, result.Done));
                    if (!result.Succeeded)
                        Log.Warn($"Gem Forge Storage: borrow from {plan.Chest} refused: {result.Status} {result.Message}");
                }
                else if (session.PendingReturn is { } loan)
                {
                    loan.Returned += result.Done;
                    if (result.Done != session.RequestedReturn || !result.Succeeded)
                    {
                        ReturnFailure(loan, session.PendingDestination,
                            $"{result.Status}: {result.Message} ({result.Done}/{session.RequestedReturn} moved)");
                    }
                    else
                    {
                        loan.LastError = null;
                        loan.Failures = 0;
                    }
                    if (result.Done > 0 && session.PendingDestination != loan.Plan.Chest)
                        Log.Warn($"Gem Forge Storage: returned {result.Done} x {loan.Plan.Item} " +
                            $"to ALTERNATIVE Magic Chest {session.PendingDestination} (original {loan.Plan.Chest} unavailable or full).");
                }
            }
            catch (Exception ex)
            {
                if (session.PendingReturn is { } failedLoan)
                    ReturnFailure(failedLoan, session.PendingDestination, ex.Message);
                else
                    Log.Warn($"Gem Forge Storage: inventory transfer failed; no replacement items created: {ex.Message}");
            }
            finally
            {
                session.PendingLoan = null;
                session.PendingReturn = null;
                session.PendingDestination = default;
                session.RequestedReturn = 0;
            }
        }

        if (session.Returning)
            ReturnNext(session);
        else
            BorrowNext(session);
    }

    private void BorrowNext(Session session)
    {
        while (session.Plans.TryDequeue(out var plan))
        {
            if (plan.Gem && Items.Read(session.Player)?.FreeSlots(InventoryCategory.Generic) <= ReserveGenericSlots)
                continue;
            var current = Items.Read(plan.Chest)?.Slots.FirstOrDefault(s => s.Slot == plan.SourceSlot);
            if (current is null || current.IsEmpty || current.Item != plan.Item ||
                (plan.Gem && current.ItemEntity != plan.Instance))
                continue;
            var count = Math.Min(current.Count, plan.Count);
            if (count <= 0) continue;
            session.PendingLoan = plan;
            session.Pending = Items.Move(plan.Chest, current.Slot, session.Player, count);
            return;
        }
        // Remain active, but never re-borrow consumed items during the same visit.
    }

    // Baseline protects material already held by the player at the start of a visit.
    // Item stacks are fungible: without an engine cost hook, exact debit order is unknown.
    internal static int Returnable(int carried, int initiallyOwned, int remainingLoan) =>
        Math.Min(Math.Max(0, remainingLoan), Math.Max(0, carried - initiallyOwned));

    private void ReturnFailure(Loan loan, EntityId destination, string reason)
    {
        loan.Failures++;
        loan.LastError = reason;
        loan.RetryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
        if (destination == loan.Plan.Chest)
            loan.OriginalRejected = true;
        else if (!destination.IsNone)
            loan.RejectedFallbacks.Add(destination);

        // The ledger remains live: retry after the chest becomes usable, never
        // silently classify a failed move as a spent or consumed gem.
        if (loan.Failures == 1 || loan.Failures % 12 == 0)
            Log.Warn($"Gem Forge Storage: return attempt {loan.Failures} for item {loan.Plan.Item} " +
                $"(instance {loan.Plan.Instance}, original chest {loan.Plan.Chest}) failed: {reason}. " +
                "Loan kept for retry. Make room in the original chest or use gforge return-any.");
    }

    private static EntityId ReturnDestination(Session session, Loan loan)
    {
        var original = loan.Plan.Chest;
        bool originalReady = World.Exists(original) && MagicChest(original) &&
            Items.Read(original) is { Initialized: true, IsPlayer: false, IsNpc: false };

        if (originalReady && !loan.OriginalRejected) return original;

        // Only with explicit consent may a gem go to a DIFFERENT Magic Chest.
        // Prefer a free generic slot so we don't constantly retry full targets.
        if (session.AllowOtherChests)
        {
            foreach (var candidate in Items.FindInventories(InventoryKinds.Containers))
            {
                if (candidate == original || loan.RejectedFallbacks.Contains(candidate) ||
                    !MagicChest(candidate))
                    continue;
                if (Items.Read(candidate) is not { Initialized: true, IsPlayer: false, IsNpc: false } inv ||
                    inv.FreeSlots(InventoryCategory.Generic) <= 0)
                    continue;
                return candidate;
            }
        }

        // Recheck the original on the next cycle; it may have been unloaded,
        // temporarily busy, or since freed by the player.
        loan.OriginalRejected = false;
        loan.RejectedFallbacks.Clear();
        return originalReady ? original : default;
    }

    private void ReturnNext(Session session)
    {
        if (!session.ReturnInitialized)
        {
            foreach (var loan in session.Loans.AsEnumerable().Reverse())
                session.ReturnQueue.Enqueue(loan);
            session.ReturnInitialized = true;
            Log.Info($"Gem Forge Storage: starting return for player {session.Player}, " +
                $"{session.ReturnQueue.Count} source loans.");
        }

        // Round-robin: one chest being full must not prevent other gems
        // returning to their respective, still-available origin chests.
        var checks = session.ReturnQueue.Count;
        while (checks-- > 0 && session.ReturnQueue.TryDequeue(out var loan))
        {
            if (loan.Returned >= loan.Lent) continue;
            if (loan.RetryAfter > DateTimeOffset.UtcNow)
            {
                session.ReturnQueue.Enqueue(loan);
                continue;
            }

            var inv = Items.Read(session.Player);
            if (inv is not { Initialized: true })
            {
                loan.RetryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
                session.ReturnQueue.Enqueue(loan);
                continue;
            }

            InventorySlot? slot;
            int count;
            if (loan.Plan.Gem)
            {
                // Keep the instance ID: never take a different gem of the same
                // type just to satisfy the return ledger.
                slot = inv.Slots.FirstOrDefault(s => !s.IsEmpty &&
                    s.Item == loan.Plan.Item && s.ItemEntity == loan.Plan.Instance);
                count = slot is null ? 0 : 1;
            }
            else
            {
                var own = session.Original.GetValueOrDefault(loan.Plan.Item);
                var available = Returnable(inv.Count(loan.Plan.Item), own, loan.Lent - loan.Returned);
                slot = inv.Slots.FirstOrDefault(s => !s.IsEmpty && s.Item == loan.Plan.Item &&
                    !s.IsEquipped && s.Count > 0);
                count = Math.Min(slot?.Count ?? 0, available);
            }

            if (slot is null || count <= 0)
            {
                // The player actually spent, socketed, or moved this item.
                // There is no matching returnable item left in the backpack.
                if (loan.Plan.Gem && loan.Failures > 0)
                    Log.Info($"Gem Forge Storage: gem instance {loan.Plan.Instance} is no longer in " +
                        "the player backpack; it will not be taken from another inventory.");
                continue;
            }

            var destination = ReturnDestination(session, loan);
            if (destination.IsNone)
            {
                ReturnFailure(loan, loan.Plan.Chest, "original Magic Chest not loaded");
                session.ReturnQueue.Enqueue(loan);
                continue;
            }

            // Requeue *before* submitting the asynchronous transaction. It will
            // only be taken off the queue once the exact item has returned or
            // ceased to be held by this player. A failed/partial move stays live.
            session.ReturnQueue.Enqueue(loan);
            session.PendingReturn = loan;
            session.PendingDestination = destination;
            session.RequestedReturn = count;
            session.Pending = Items.Move(session.Player, slot.Slot, destination, count);
            return;
        }

        if (session.ReturnQueue.Count > 0) return;

        var total = session.Loans.Sum(x => x.Lent);
        var returned = session.Loans.Sum(x => x.Returned);
        if (total > returned)
            Log.Info($"Gem Forge Storage: player {session.Player} completed return: " +
                $"{returned}/{total} items returned; the remainder were used, socketed, or moved away.");
        else if (total > 0)
            Log.Info($"Gem Forge Storage: player {session.Player} returned all {returned} loaned item(s).");
        _sessions.Remove(session.Player);
    }

    private void Command(CommandContext command)
    {
        var verb = command.ArgOrDefault(0, "status")!.ToLowerInvariant();
        if (verb is not ("scan" or "status" or "probe" or "return" or "return-any" or "help"))
            throw command.Usage("status | scan | probe | return | return-any | help");
        if (verb == "help")
        {
            command.Reply("gforge status (current loan), scan (read-only sources), probe (forge detection), " +
                "return (retry original chests), return-any (explicitly permit other Magic Chests). " +
                "Transfers start within 8 m and return beyond 16 m.");
            return;
        }
        var player = command.Source.Player;
        if (player.IsNone) throw new CommandException("Run this as a player inside the world.");
        if (verb == "probe")
        {
            if (!World.Available || !Spawn.Available)
                throw new CommandException("Kiln World or Spawn is not available.");
            ProbeForge(command, player);
            return;
        }
        if (verb == "status")
        {
            command.Reply(_sessions.TryGetValue(player, out var session)
                ? $"Gem Forge Storage: {(session.Returning ? "returning" : "active")}; " +
                  $"plans={session.Plans.Count}, loans={session.Loans.Count}, " +
                  $"borrowed={session.Loans.Sum(x => x.Lent)}, returned={session.Loans.Sum(x => x.Returned)}, " +
                  $"pendingReturn={session.ReturnQueue.Count}, failedLoans={session.Loans.Count(x => x.Failures > 0)}, " +
                  $"allowOtherChests={session.AllowOtherChests}."
                : $"Gem Forge Storage: no active loan; moves={Items.MovesAvailable}, magic chests loaded in current world only.");
            return;
        }
        if (verb is "return" or "return-any")
        {
            if (!_sessions.TryGetValue(player, out var s))
            {
                command.Reply("No active Gem Forge loan. This version cannot reconstruct loans from a prior game session.");
                return;
            }
            _manualReturn.Add(player);
            s.Returning = true;
            s.Plans.Clear();
            if (verb == "return-any") s.AllowOtherChests = true;
            foreach (var loan in s.Loans)
                loan.RetryAfter = default; // user may have freed space; retry immediately
            command.Reply(verb == "return-any"
                ? "Returning unused loaned items now. If original chests are full/unavailable, " +
                  "you ALLOW alternative loaded Magic Chests with free slots; destinations are logged."
                : "Returning unused loaned items to ORIGINAL chests. Failed transfers remain pending " +
                  "and retry every five seconds; use return-any only to authorize alternative chests.");
            return;
        }
        if (!Items.MovesAvailable || !Spawn.Available || !World.Available)
            throw new CommandException("Kiln item transfer, Spawn or World service is not ready.");
        var forgeId = NearestForge(player);
        if (forgeId.IsNone)
            throw new CommandException("No Gem Forge within 8 m. Run gforge probe for the world-entity census and distances.");
        var at = World.Position(forgeId) ?? throw new CommandException("Gem Forge has no position.");
        var chests = LoadedMagicChests(at);
        var gems = 0;
        var untrackedGems = 0;
        var essence = 0;
        foreach (var (_, inv) in chests)
            foreach (var slot in inv.Slots.Where(x => !x.IsEmpty))
            {
                var info = Items.Get(slot.Item);
                if (Essence(info)) essence += slot.Count;
                if (info?.Category == ItemCategory.WeaponGem)
                {
                    gems++;
                    if (slot.ItemEntity.IsNone) untrackedGems++;
                }
            }
        command.Reply($"Gem Forge Storage scan: forge={forgeId}, loaded Magic Chests={chests.Count}, " +
            $"Archaic Essence={essence}, weapon gems={gems} (untracked instances={untrackedGems}), " +
            $"move-service={Items.MovesAvailable}. Nothing moved. " +
            "Distant unloaded chests are not accessible via Kiln.Items.");
    }
}
