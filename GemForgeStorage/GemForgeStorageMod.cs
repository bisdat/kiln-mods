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
    private const double ReachMeters = 4;
    private const double LeaveMeters = 10;
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
        public int ReturnIndex { get; set; } = -1;
        public bool ReturnInitialized { get; set; }
        public Task<MoveResult>? Pending { get; set; }
        public TransferPlan? PendingLoan { get; set; }
        public Loan? PendingReturn { get; set; }
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
            usage: "[status|scan|return]", description: "Magic Chest inventory access at Gem Forges.");
        Context.Tick.Pre += Tick;
        Log.Info("Gem Forge Storage v0.1.0 loaded: loaded Magic Chests only; Archaic Essence + weapon gems, Kiln.Items.Move.");
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

    private static EntityId NearestForge(EntityId player) =>
        Nearby.Entities(player, ReachMeters, "ShroudForge").FirstOrDefault();

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
                    Log.Warn("Gem Forge Storage is inactive: Kiln.Items moves, World, Spawn or Players unavailable.");
                    _warned = true;
                }
                return;
            }
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
                Log.Info($"Gem Forge Storage: player {player.Entity} near forge {forge}; {session.Plans.Count} source stacks available.");
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
                    if (result.Done != session.RequestedReturn)
                        Log.Warn($"Gem Forge Storage: could return {result.Done}/{session.RequestedReturn} x {loan.Plan.Item} " +
                            $"to original chest {loan.Plan.Chest}; remaining items stay with the player ({result.Status}).");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Gem Forge Storage: inventory transfer failed; no replacement items created: {ex.Message}");
            }
            finally
            {
                session.PendingLoan = null;
                session.PendingReturn = null;
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

    private void ReturnNext(Session session)
    {
        if (!session.ReturnInitialized)
        {
            session.ReturnIndex = session.Loans.Count - 1;
            session.ReturnInitialized = true;
        }
        while (session.ReturnIndex >= 0)
        {
            var loan = session.Loans[session.ReturnIndex--];
            var inv = Items.Read(session.Player);
            if (inv is null) continue;

            InventorySlot? slot;
            int count;
            if (loan.Plan.Gem)
            {
                slot = inv.Slots.FirstOrDefault(s => !s.IsEmpty && s.Item == loan.Plan.Item &&
                    s.ItemEntity == loan.Plan.Instance);
                count = slot is null ? 0 : 1;
            }
            else
            {
                slot = inv.Slots.FirstOrDefault(s => !s.IsEmpty && s.Item == loan.Plan.Item &&
                    !s.IsEquipped && s.Count > 0);
                var own = session.Original.GetValueOrDefault(loan.Plan.Item);
                count = Math.Min(slot?.Count ?? 0, Returnable(inv.Count(loan.Plan.Item), own, loan.Lent - loan.Returned));
            }
            if (slot is null || count <= 0) continue;
            if (!World.Exists(loan.Plan.Chest) || !MagicChest(loan.Plan.Chest)) continue;
            session.PendingReturn = loan;
            session.RequestedReturn = count;
            session.Pending = Items.Move(session.Player, slot.Slot, loan.Plan.Chest, count);
            return;
        }
        var total = session.Loans.Sum(x => x.Lent);
        var returned = session.Loans.Sum(x => x.Returned);
        if (total > returned)
            Log.Warn($"Gem Forge Storage: player {session.Player} finished forge visit; {returned}/{total} items returned. " +
                "Unreturned items were spent, inserted into equipment, or remain in the backpack (e.g. chest full).");
        else if (total > 0)
            Log.Info($"Gem Forge Storage: player {session.Player} returned {returned} loaned item(s).");
        _sessions.Remove(session.Player);
    }

    private void Command(CommandContext command)
    {
        var verb = command.ArgOrDefault(0, "status")!.ToLowerInvariant();
        if (verb is not ("scan" or "status" or "return" or "help"))
            throw command.Usage("status | scan | return | help");
        if (verb == "help")
        {
            command.Reply("gforge status (current loan), scan (read-only sources), return (return now and suspend until leaving). " +
                "Transfers start automatically within 4 m of a Gem Forge and return beyond 10 m.");
            return;
        }
        var player = command.Source.Player;
        if (player.IsNone) throw new CommandException("Run this as a player inside the world.");
        if (verb == "status")
        {
            command.Reply(_sessions.TryGetValue(player, out var session)
                ? $"Gem Forge Storage: {(session.Returning ? "returning" : "active")}; " +
                  $"plans={session.Plans.Count}, loans={session.Loans.Count}, " +
                  $"borrowed={session.Loans.Sum(x => x.Lent)}, returned={session.Loans.Sum(x => x.Returned)}."
                : $"Gem Forge Storage: no active loan; moves={Items.MovesAvailable}, magic chests loaded in current world only.");
            return;
        }
        if (verb == "return")
        {
            if (!_sessions.TryGetValue(player, out var s))
            {
                command.Reply("No active Gem Forge loan.");
                return;
            }
            _manualReturn.Add(player);
            s.Returning = true;
            s.Plans.Clear();
            command.Reply("Gem Forge Storage: returning loaned items now; borrowing resumes after you leave and approach the forge again.");
            return;
        }
        if (!Items.MovesAvailable || !Spawn.Available || !World.Available)
            throw new CommandException("Kiln item transfer, Spawn or World service is not ready.");
        var forgeId = NearestForge(player);
        if (forgeId.IsNone) throw new CommandException("No Gem Forge within 4 m.");
        var at = World.Position(forgeId) ?? throw new CommandException("Gem Forge has no position.");
        var chests = LoadedMagicChests(at);
        var gems = 0;
        var essence = 0;
        foreach (var (_, inv) in chests)
            foreach (var slot in inv.Slots.Where(x => !x.IsEmpty))
            {
                var info = Items.Get(slot.Item);
                if (Essence(info)) essence += slot.Count;
                if (info?.Category == ItemCategory.WeaponGem) gems++;
            }
        command.Reply($"Gem Forge Storage scan: forge={forgeId}, loaded Magic Chests={chests.Count}, " +
            $"Archaic Essence={essence}, weapon gems={gems}. Nothing moved. " +
            "Distant unloaded chests are not accessible via Kiln.Items.");
    }
}
