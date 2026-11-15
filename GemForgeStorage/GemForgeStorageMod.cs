// SPDX-License-Identifier: MIT
// Read-only investigation of Enshrouded Gem Forge operations through native Kiln APIs.
// Previous proximity-loan design was rejected and has been removed deliberately.
using System.Buffers.Binary;
using Kiln;

namespace GemForgeStorage;

/// <summary>
/// Observes gem-forge client actions and inventories without moving, creating,
/// destroying, consuming, canceling, or otherwise modifying game state.
/// </summary>
public sealed class GemForgeStorageMod : Mod
{
    // The verified placed Gem Forge is a runtime prop with UsedItem 3250908839.
    private const uint PlaceableGemForgeItem = 3250908839u;
    private const double ProbeRadiusMeters = 12.0;
    private const int MaxHistory = 40;
    private const int MaxChestScan = 256;

    private readonly HashSet<EntityId> _watchers = [];
    private readonly Queue<string> _history = new();
    private EventSubscription? _actionSubscription;
    private CommandRegistration? _command;
    private long _observed;
    private bool _unloading;

    public override void OnLoad()
    {
        _command = Commands.Register("gforge", Command, CommandPermission.Everyone,
            usage: "[status|forge|scan|watch on|watch off|recent|clear|help]",
            description: "Read-only Gem Forge integration research (never moves inventory).");
        Log.Info("Gem Forge Research v0.2.0 loaded: READ ONLY; no borrowing, returning, " +
            "inventory transactions, client-action cancellation, or patches.");
    }

    public override void OnUnload()
    {
        _unloading = true;
        _actionSubscription?.Dispose();
        _actionSubscription = null;
        _command?.Dispose();
        _watchers.Clear();
        _history.Clear();
    }

    private static bool IsPlacedGemForge(EntityId entity) =>
        World.TryRead<uint>(entity, "UsedItem", out var itemId) &&
        itemId == PlaceableGemForgeItem;

    // Verified in-game for the player's placed Premium Gem Forge. Do not use
    // ShroudForge ECS (zero found) or a template name suffix (name differs).
    private static (EntityId Id, double Distance) NearestForge(EntityId player)
    {
        if (World.Position(player) is not { } at) return (default, double.MaxValue);
        EntityId nearest = default;
        var best = double.MaxValue;
        foreach (var candidate in World.Find("UsedItem"))
        {
            if (!IsPlacedGemForge(candidate) ||
                World.Position(candidate) is not { } pos) continue;
            var distance = pos.DistanceTo(at);
            if (distance >= best) continue;
            nearest = candidate;
            best = distance;
        }
        return (nearest, best);
    }

    private static bool IsMagicChest(EntityId entity)
    {
        var name = Spawn.Inspect(entity)?.TemplateName;
        return name is not null &&
            name.Contains("Storage", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith("_Magic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsArchaicEssence(ItemInfo? item) =>
        item is not null &&
        item.Name.StartsWith("Material_T", StringComparison.Ordinal) &&
        item.Name.EndsWith("_Fognest_Ressource", StringComparison.Ordinal);

    private void OnGemAction(PlayerClientActionEvent e)
    {
        // A passive event listener; NEVER invoke e.Cancel or write into e.Payload.
        if (_unloading || e.Name != "weaponGemAction" ||
            !_watchers.Contains(e.Player.Entity)) return;
        try
        {
            var (forge, distance) = NearestForge(e.Player.Entity);
            if (forge.IsNone || distance > ProbeRadiusMeters) return;
            var bytes = e.Payload;
            var words = new List<string>();
            for (var offset = 0; offset + 4 <= bytes.Length; offset += 4)
                words.Add($"+{offset:x2}=0x{BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)):x8}");

            var entry = $"player={e.Player.Entity} forge={forge} distance={distance:F1}m " +
                $"v={e.Version} len={bytes.Length} canCancel={e.CanCancel} " +
                $"raw={Convert.ToHexString(bytes)} dwords=[{string.Join(" ", words)}]";
            _observed++;
            _history.Enqueue(entry);
            while (_history.Count > MaxHistory) _history.Dequeue();
            Log.Info($"Gem Forge Research: weaponGemAction #{_observed} {entry}");
        }
        catch (Exception ex)
        {
            // Never affect the native operation when a read fails.
            Log.Warn($"Gem Forge Research: observing action failed: {ex.Message}");
        }
    }

    private void Command(CommandContext command)
    {
        var player = command.Source.Player;
        if (player.IsNone)
            throw new CommandException("Use the Kiln console as a character in the hosted world.");

        var verb = command.ArgOrDefault(0, "status")!.ToLowerInvariant();
        if (verb == "help")
        {
            command.Reply("gforge status|forge|scan|watch on|watch off|recent|clear. " +
                "READ ONLY: this version never moves items or changes the Gem Forge.");
            return;
        }
        if (verb == "status")
        {
            var action = PlayerStates.Available
                ? PlayerStates.ClientActions.FirstOrDefault(a => a.Name == "weaponGemAction")
                : null;
            command.Reply($"Gem Forge Research v0.2.0: READ ONLY, watch={_watchers.Contains(player)}, " +
                $"observed={_observed}, saved={_history.Count}, playerstate={PlayerStates.Available}, " +
                $"weaponGemAction={(action is null ? "not exposed" : $"{action.Bytes} bytes, canCancel={action.CanCancel}")}, " +
                $"world={World.Available}, spawn={Spawn.Available}, items={Items.Available}. " +
                "No automatic inventory transfers.");
            return;
        }
        if (verb == "recent")
        {
            var records = _history.Reverse().Take(6).ToArray();
            if (records.Length == 0)
                command.Reply("No weaponGemAction captured. Stand at the Gem Forge, run gforge watch on, " +
                    "then click a gem operation; run gforge recent afterward.");
            else
                foreach (var line in records) command.Reply(line);
            return;
        }
        if (verb == "clear")
        {
            _history.Clear();
            command.Reply("Read-only action history cleared; nothing changed in the game.");
            return;
        }
        if (verb == "watch")
        {
            var action = command.ArgOrDefault(1, "")!.ToLowerInvariant();
            if (action == "off")
            {
                _watchers.Remove(player);
                if (_watchers.Count == 0)
                {
                    _actionSubscription?.Dispose();
                    _actionSubscription = null;
                }
                command.Reply("Gem Forge Research watch OFF. Client action listener removed when no watchers remain.");
                return;
            }
            if (action != "on") throw command.Usage("watch on | watch off");
            if (!PlayerStates.Available)
                throw new CommandException("Kiln player-state service not available; no watch started.");
            var info = PlayerStates.ClientActions.FirstOrDefault(a => a.Name == "weaponGemAction");
            if (info is not { Bytes: 28 })
                throw new CommandException("This build does not report the expected 28-byte weaponGemAction; no watch started.");
            if (!World.Available)
                throw new CommandException("Kiln.World not available.");
            _watchers.Add(player);
            _actionSubscription ??= Events.On<PlayerClientActionEvent>(OnGemAction);
            command.Reply($"Gem Forge Research watch ON for player {player}. " +
                "Only weaponGemAction near a placed forge is logged, read-only. " +
                "Kiln delays observed client actions by one game step while the event subscription is active; " +
                "watch off removes the listener. No operation is canceled.");
            return;
        }

        if (!World.Available || !Spawn.Available)
            throw new CommandException("Kiln.World or Kiln.Spawn unavailable.");

        var (forge, distance) = NearestForge(player);
        if (verb == "forge")
        {
            if (forge.IsNone) throw new CommandException("No placed Gem Forge with the known UsedItem ID is loaded.");
            command.Reply($"Nearest placed Gem Forge: entity={forge}, {distance:F1}m, " +
                $"template={Spawn.Inspect(forge)?.TemplateName ?? "unknown"}. " +
                $"ShroudForge component on this entity={World.Has(forge, "ShroudForge")}. " +
                "Read-only native Kiln world lookup.");
            return;
        }
        if (verb != "scan") throw command.Usage("status | forge | scan | watch on/off | recent | clear | help");

        if (!Items.Available) throw new CommandException("Kiln.Items unavailable for read-only scan.");
        var count = 0;
        var gems = 0;
        var essence = 0;
        var samples = new List<string>();
        foreach (var chest in Items.FindInventories(InventoryKinds.Containers | InventoryKinds.NonEmpty))
        {
            if (!IsMagicChest(chest)) continue;
            if (Items.Read(chest) is not { Initialized: true, IsPlayer: false, IsNpc: false } inventory)
                continue;
            count++;
            foreach (var slot in inventory.Slots.Where(slot => !slot.IsEmpty))
            {
                var item = Items.Get(slot.Item);
                if (IsArchaicEssence(item)) essence += slot.Count;
                if (item?.Category != ItemCategory.WeaponGem) continue;
                gems++;
                if (samples.Count < 5)
                    samples.Add($"{item.Name} lvl={slot.Level} instance={slot.ItemEntity} chest={chest}");
            }
            if (count >= MaxChestScan) break;
        }
        command.Reply($"Gem Forge Research: READ ONLY; nearest forge={forge} " +
            $"({(forge.IsNone ? "unknown" : $"{distance:F1}m")}), loaded Magic Chests={count}, " +
            $"weapon gem slots={gems}, Archaic Essence units={essence}. " +
            "No transfer attempted. Unloaded remote bases are not read by Kiln.Items.");
        if (samples.Count > 0)
            command.Reply("Sample gems (identity only): " + string.Join("; ", samples));
    }
}
