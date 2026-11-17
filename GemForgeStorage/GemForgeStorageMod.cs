// SPDX-License-Identifier: MIT
// Experimental native Gem Forge integration: no inventory loans or client-side item forging.
using Kiln;

namespace GemForgeStorage;

/// <summary>
/// Authorises loaded Magic Chest gems from the player's current base for the
/// game's own Shroud Forge grid. Kiln owns the slot tokens, payment and atomic
/// forge transaction; this mod never transfers or patches inventory directly.
/// </summary>
public sealed class GemForgeStorageMod : Mod
{
    private const int MaxGemOffersPerPlayer = 256;
    private const int MaxChestScan = 256;
    private readonly Dictionary<EntityId, HashSet<EntityId>> _offered = [];
    private EventSubscription? _opened;
    private CommandRegistration? _command;
    private bool _enabled; // Deliberately opt-in while the native UI path is being accepted.
    private string _last = "inactive; use gforge on at a Shroud Forge";
    private long _events;
    private int _lastGemCount;

    public override void OnLoad()
    {
        _opened = Events.On<GemForgeOpenedEvent>(OnForgeOpened);
        _command = Commands.Register("gforge", Command, CommandPermission.Admin,
            usage: "[status|scan|on|off|help]",
            description: "Experimental same-base Magic Chest gems in the native Gem Forge.");
        Log.Info("Gem Forge Magic Storage v0.3.0 loaded (OFF by default). " +
            "Server-only native offers; no borrowing, transfers, patches or client UI replacement.");
    }

    public override void OnUnload()
    {
        // Kiln withdraws this mod's offers on unload, including when no server
        // tick remains. WithdrawChestGems itself requires the live tick.
        _opened?.Dispose();
        _opened = null;
        _command?.Dispose();
        _command = null;
        _offered.Clear();
    }

    private static bool IsMagicChest(EntityId chest)
    {
        var name = Spawn.Inspect(chest)?.TemplateName;
        return name is not null &&
            name.Contains("Storage", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith("_Magic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCurrentBase(EntityId player, out BaseId id, out string reason)
    {
        id = BaseId.None;
        if (!Players.Available || !Bases.Available || !Items.Available || !Spawn.Available)
        {
            reason = "required Players, Bases, Items or Spawn service unavailable";
            return false;
        }

        var p = Players.Find(player);
        if (p is not { InGame: true })
        {
            reason = "player is not in the current world";
            return false;
        }
        if (!p.Can(PlayerPermissions.AccessInventories))
        {
            reason = "player does not have the game's AccessInventories right";
            return false;
        }

        var baseInfo = Bases.Of(player);
        if (baseInfo is null || baseInfo.Id.IsNone)
        {
            reason = "player is outside a loaded flame-altar base";
            return false;
        }

        id = baseInfo.Id;
        reason = "";
        return true;
    }

    private static int CountLooseGems(EntityId chest)
    {
        var contents = Items.Read(chest);
        if (contents is not { Initialized: true, IsPlayer: false, IsNpc: false })
            return 0;
        var found = 0;
        foreach (var slot in contents.Slots)
            if (!slot.IsEmpty && Items.Get(slot.Item)?.Category == ItemCategory.WeaponGem)
                found++;
        return found;
    }

    private static IReadOnlyList<(EntityId Chest, int Gems)> EligibleChests(BaseId baseId)
    {
        var result = new List<(EntityId Chest, int Gems)>();
        var checkedChests = 0;
        // This is the game's crafting-stock list for this base, not every loaded
        // container in the world. It also contains some plants: filter those out.
        foreach (var chest in Bases.Storage(baseId))
        {
            if (++checkedChests > MaxChestScan) break;
            if (!IsMagicChest(chest)) continue;
            var count = CountLooseGems(chest);
            if (count > 0) result.Add((chest, count));
        }
        return result;
    }

    private void Withdraw(EntityId player)
    {
        if (!_offered.Remove(player)) return;
        if (Capabilities.Is(Gems.SourcesCapability))
            Gems.WithdrawChestGems(player);
    }

    private void SetLast(string message)
    {
        if (message != _last)
            Log.Info("Gem Forge Magic Storage: " + message);
        _last = message;
    }

    private void OnForgeOpened(GemForgeOpenedEvent e)
    {
        if (!_enabled) return;
        _events++;
        try
        {
            if (!Capabilities.Is(Gems.SourcesCapability) ||
                !Capabilities.Is(Gems.PayersCapability))
            {
                Withdraw(e.Player);
                SetLast("gems.sources or gems.payers unavailable; no offers");
                return;
            }
            BaseId baseId = BaseId.None;
            string reason = "";
            if (e.Forge.IsNone ||
                !TryCurrentBase(e.Player, out baseId, out reason) ||
                Bases.Of(e.Forge)?.Id != baseId)
            {
                Withdraw(e.Player);
                SetLast(e.Forge.IsNone
                    ? "no Shroud Forge within 12 m"
                    : string.IsNullOrEmpty(reason)
                        ? "the forge is not in the player's current base"
                        : reason);
                return;
            }

            var next = new HashSet<EntityId>();
            var count = 0;
            var refused = 0;
            foreach (var (chest, gems) in EligibleChests(baseId))
            {
                // Never offer a partial chest; Kiln limits one player's live
                // offers to 256. Prefer the game's stable storage order.
                if (gems > MaxGemOffersPerPlayer - count)
                {
                    refused++;
                    continue;
                }
                // Player pays first, then their base Magic Chests. Recycle's
                // essence returns to the player (native behaviour); never to a
                // potentially full source chest.
                var offer = Gems.OfferChestGems(e.Player, chest,
                    new GemSourceOptions
                    {
                        PayFrom = GemPayer.PlayerThenBaseStorage,
                        Lifetime = TimeSpan.FromSeconds(10)
                    });
                if (!offer.Ok)
                {
                    refused++;
                    continue;
                }
                if (offer.Gems.Count == 0) continue;
                count += offer.Gems.Count;
                next.Add(chest);
            }

            if (_offered.TryGetValue(e.Player, out var old))
                foreach (var chest in old)
                    if (!next.Contains(chest))
                        Gems.WithdrawChestGems(e.Player, chest);

            if (next.Count > 0) _offered[e.Player] = next;
            else _offered.Remove(e.Player);

            _lastGemCount = count;
            SetLast($"player={e.Player}: offered {count} chest gems from {next.Count} same-base Magic Chests" +
                (refused == 0 ? "" : $"; {refused} chest(s) skipped/refused"));
        }
        catch (Exception ex)
        {
            try { Withdraw(e.Player); }
            catch (Exception) { /* Remaining offers expire; Kiln owns the session. */ }
            SetLast("offer failed (nothing moved): " + ex.Message);
        }
    }

    private void Command(CommandContext command)
    {
        var player = command.Source.Player;
        if (player.IsNone)
            throw new CommandException("Run gforge as a character in the hosted world.");

        var verb = command.ArgOrDefault(0, "status")!.ToLowerInvariant();
        switch (verb)
        {
            case "help":
                command.Reply("gforge status | scan | on | off. OFF after each mod load. " +
                    "ON offers same-base Magic Chest gems in the native Gem Forge. " +
                    "Archaic Essence: player first, then base storage. Recycle returns to the player.");
                return;
            case "status":
                command.Reply($"Gem Forge v0.3.0: enabled={_enabled}, " +
                    $"gems.sources={Capabilities.Is(Gems.SourcesCapability)}, " +
                    $"gems.payers={Capabilities.Is(Gems.PayersCapability)}, " +
                    $"bases={Bases.Available}, observedOpenEvents={_events}, " +
                    $"lastOfferedGems={_lastGemCount}, activePlayers={_offered.Count}. Last: {_last}");
                return;
            case "scan":
                if (!TryCurrentBase(player, out var id, out var why))
                    throw new CommandException("Cannot scan: " + why);
                var chests = EligibleChests(id);
                command.Reply($"Current base {id}: {chests.Count} loaded Magic Chests " +
                    $"with loose gems, {chests.Sum(c => c.Gems)} loose gem slots. " +
                    "READ ONLY; nothing offered or moved by scan.");
                return;
            case "on":
                if (!TryCurrentBase(player, out _, out var reason))
                    throw new CommandException("Cannot enable: " + reason);
                if (!Capabilities.Is(Gems.SourcesCapability) ||
                    !Capabilities.Is(Gems.PayersCapability))
                    throw new CommandException("Requires Kiln 0.2.0-dev.29.15+ with gems.sources and gems.payers.");
                _enabled = true;
                SetLast("enabled by server administrator; waiting for forge-open/refresh event");
                command.Reply("Gem Forge chest integration ON. Open the Shroud Forge; " +
                    "gforge status reports offered gem count. Use gforge off to withdraw offers.");
                return;
            case "off":
                _enabled = false;
                // Console command dispatch is on the server tick.
                foreach (var p in _offered.Keys.ToArray()) Withdraw(p);
                _lastGemCount = 0;
                SetLast("disabled by server administrator");
                command.Reply("Gem Forge chest integration OFF. Existing offers withdrawn.");
                return;
            default:
                throw command.Usage("status | scan | on | off | help");
        }
    }
}
