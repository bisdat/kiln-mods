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
    // Client-main-thread frame callbacks and server-world console commands run
    // on different threads in a hosted world. No client entity ID is ever
    // treated as an authoritative server entity ID.
    private readonly object _uiLock = new();
    private readonly Queue<string> _uiHistory = new();
    private UiValueRegistration? _slotRegistration;
    private volatile bool _uiWatching;
    private long _uiFrames;
    private long _forgeFrames;
    private long _uiCaptured;
    private string? _uiLastSignature;
    private string? _uiLastError;
    private ulong _uiLastCaptureMs;
    private const int MaxUiHistory = 30;
    private readonly Queue<string> _history = new();
    private EventSubscription? _actionSubscription;
    private CommandRegistration? _command;
    private long _observed;
    private bool _unloading;

    public override void OnLoad()
    {
        _command = Commands.Register("gforge", Command, CommandPermission.Everyone,
            usage: "[status|forge|scan|watch on|watch off|recent|ui|ui-watch on/off|ui-recent|clear|help]",
            description: "Read-only Gem Forge integration research (never moves inventory).");
        Log.Info("Gem Forge Research v0.2.1 loaded: READ ONLY; no borrowing, returning, " +
            "inventory transactions, client-action cancellation, or patches.");
    }

    public override void OnUnload()
    {
        _unloading = true;
        _actionSubscription?.Dispose();
        _actionSubscription = null;
        _uiWatching = false;
        Ui.Screens.Changed -= OnUiScreenChanged;
        _slotRegistration?.Dispose();
        _slotRegistration = null;
        _command?.Dispose();
        _watchers.Clear();
        _history.Clear();
        lock (_uiLock) _uiHistory.Clear();
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

    // UI observation exists only in dev.29's client service. No server-world
    // inventory APIs may be called from any of these client UI callbacks.
    private void UiRecord(string entry)
    {
        lock (_uiLock)
        {
            _uiHistory.Enqueue(entry);
            while (_uiHistory.Count > MaxUiHistory) _uiHistory.Dequeue();
            _uiCaptured++;
        }
        Log.Info("Gem Forge Research UI: " + entry);
    }

    private void OnUiScreenChanged(GameScreen before, GameScreen now)
    {
        if (_unloading || !_uiWatching) return;
        if (before.Hud != HudScreen.ShroudForge && now.Hud != HudScreen.ShroudForge)
            return;
        UiRecord($"screen {before.Kind}/{before.Hud} -> {now.Kind}/{now.Hud} " +
            $"state={now.State} frame={now.Frame}");
    }

    private void OnUiSlotFrame(VisibleSlotsResult result)
    {
        if (_unloading || !_uiWatching) return;
        // Called after each frame on the CLIENT MAIN THREAD. Keep it short.
        try
        {
            if (!result.Ok || result.Snapshot is not { } snapshot)
            {
                var reason = result.Reason ?? "unknown";
                if (_uiLastError != reason)
                {
                    _uiLastError = reason;
                    UiRecord("slot inspector temporarily unavailable: " + reason);
                }
                return;
            }

            Interlocked.Increment(ref _uiFrames);
            if (snapshot.Screen.Hud != HudScreen.ShroudForge) return;
            Interlocked.Increment(ref _forgeFrames);
            _uiLastError = null;

            var filled = snapshot.Slots.Where(x => x.Count > 0 && x.Item != default).ToArray();
            var pages = snapshot.Slots
                .GroupBy(x => (x.Holder, x.HolderSerial, x.InventoryPage))
                .Take(8)
                .Select(g => $"holder={g.Key.Holder} serial={g.Key.HolderSerial} " +
                    $"page={g.Key.InventoryPage} slots={g.Count()}")
                .ToArray();

            // Log only when the visible item composition changes or on the first
            // capture after a 3-second interval, not every client frame.
            var signature = string.Join(",", filled.Select(x =>
                $"{x.InventoryPage}:{x.SlotIndex}:{x.Item}:{x.Count}"));
            var enoughTime = snapshot.CaptureMs >= _uiLastCaptureMs + 3000ul;
            if (signature == _uiLastSignature && !enoughTime) return;
            _uiLastSignature = signature;
            _uiLastCaptureMs = snapshot.CaptureMs;

            var samples = filled.Take(12).Select(x =>
                $"item={x.Item} count={x.Count} holder={x.Holder} " +
                $"page={x.InventoryPage} slot={x.SlotIndex} " +
                $"clientEntity={x.ClientEntity} holderFlags={x.HolderFlags}");

            UiRecord($"forge-frame={snapshot.Frame} hud={snapshot.Screen.Hud} " +
                $"slots={snapshot.Slots.Count} filled={filled.Length} " +
                $"holders/pages=[{string.Join("; ", pages)}] " +
                $"first=[{string.Join("; ", samples)}]. " +
                "Client entity IDs are NOT server inventory IDs.");
        }
        catch (Exception ex)
        {
            if (_uiLastError != ex.Message)
            {
                _uiLastError = ex.Message;
                UiRecord("slot capture failed: " + ex.Message);
            }
        }
    }

    private void UiCommand(CommandContext command, string verb)
    {
        if (verb == "ui")
        {
            var current = Ui.Available && Ui.Screens.Available ? Ui.Screens.Current : GameScreen.None;
            long frameCount = Interlocked.Read(ref _uiFrames);
            long forgeCount = Interlocked.Read(ref _forgeFrames);
            lock (_uiLock)
                command.Reply($"Gem Forge UI v0.2.1: read-only watch={_uiWatching} " +
                    $"uiAvailable={Ui.Available}, screenAvailable={Ui.Screens.Available}, " +
                    $"screen={current.Kind}/{current.Hud}, clientFrames={frameCount}, " +
                    $"forgeFrames={forgeCount}, captures={_uiCaptured}. " +
                    "Only game-rendered slots are inspected; source inventory is unchanged.");
            return;
        }
        if (verb == "ui-recent")
        {
            string[] latest;
            lock (_uiLock) latest = _uiHistory.Reverse().Take(6).ToArray();
            if (latest.Length == 0) command.Reply("No UI observations. Use gforge ui-watch on, open the Gem Forge, then close it.");
            else foreach (var entry in latest) command.Reply(entry);
            return;
        }
        var state = command.ArgOrDefault(1, "")!.ToLowerInvariant();
        if (state == "off")
        {
            _uiWatching = false;
            Ui.Screens.Changed -= OnUiScreenChanged;
            _slotRegistration?.Dispose();
            _slotRegistration = null;
            command.Reply("Gem Forge UI watch OFF; client slot callback detached.");
            return;
        }
        if (state != "on") throw command.Usage("ui-watch on | ui-watch off");
        if (!Ui.Available || !Ui.Screens.Available)
            throw new CommandException("Kiln.Ui client screen service is unavailable. Use a hosted game with Kiln dev.29+.");
        if (_uiWatching)
        {
            command.Reply("Gem Forge UI watch is already ON.");
            return;
        }

        // Registration is explicit, so this probe does not permanently hook
        // the client slot collector just because the mod was installed.
        UiValueRegistration registration;
        try { registration = Ui.Values.SubscribeSlots(OnUiSlotFrame); }
        catch (Exception ex) { throw new CommandException("Cannot inspect UI slots: " + ex.Message); }
        if (!registration.Ok)
        {
            var why = registration.Reason ?? "no reason";
            registration.Dispose();
            throw new CommandException("Kiln refused UI slot inspection: " + why);
        }
        _slotRegistration = registration;
        _uiWatching = true;
        Ui.Screens.Changed += OnUiScreenChanged;
        command.Reply("Gem Forge UI watch ON. Open the forge, browse gems, and close it. " +
            "Then use gforge ui-recent and ui-watch off. This probe observes ONLY drawn client UI slots; " +
            "it never changes inventory or cancels an operation.");
    }

    private void Command(CommandContext command)
    {
        var player = command.Source.Player;
        if (player.IsNone)
            throw new CommandException("Use the Kiln console as a character in the hosted world.");

        var verb = command.ArgOrDefault(0, "status")!.ToLowerInvariant();
        if (verb == "help")
        {
            command.Reply("gforge status|forge|scan|watch on/off|recent|ui|ui-watch on/off|ui-recent|clear. " +
                "READ ONLY: this version never moves items or changes the Gem Forge.");
            return;
        }
        if (verb is "ui" or "ui-watch" or "ui-recent")
        {
            UiCommand(command, verb);
            return;
        }
        if (verb == "status")
        {
            var action = PlayerStates.Available
                ? PlayerStates.ClientActions.FirstOrDefault(a => a.Name == "weaponGemAction")
                : null;
            command.Reply($"Gem Forge Research v0.2.1: READ ONLY, watch={_watchers.Contains(player)}, " +
                $"observed={_observed}, saved={_history.Count}, playerstate={PlayerStates.Available}, " +
                $"weaponGemAction={(action is null ? "not exposed" : $"{action.Bytes} bytes, canCancel={action.CanCancel}")}, " +
                $"world={World.Available}, spawn={Spawn.Available}, items={Items.Available}. " +
                $"clientUi={Ui.Available}, uiWatch={_uiWatching}. No automatic inventory transfers.");
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
            lock (_uiLock) _uiHistory.Clear();
            command.Reply("Read-only action and UI histories cleared; nothing changed in the game.");
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
