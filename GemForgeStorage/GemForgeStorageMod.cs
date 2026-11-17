// SPDX-License-Identifier: MIT
using Kiln;

namespace GemForgeStorage;

/// <summary>
/// Automatically offers loose gems from the player's current base's loaded Magic
/// Chests to Enshrouded's native Gem Forge. Kiln validates every offered item
/// and performs the game's inventory transaction; no loan/return workaround.
/// </summary>
public sealed class GemForgeStorageMod : Mod
{
    private const int MaxGemOffersPerPlayer = 256;
    private const int MaxChestScan = 256;
    private static readonly GemSourceOptions SourceOptions = new()
    {
        // Pay from the player first, then the game's base crafting stock.
        PayFrom = GemPayer.PlayerThenBaseStorage,
        // Kiln sends a refresh every 5 s while the forge is open.
        Lifetime = TimeSpan.FromSeconds(10)
        // Recycle rewards go into the backpack, as in the native game.
    };

    private readonly Dictionary<EntityId, HashSet<EntityId>> _offered = [];
    private EventSubscription? _opened;
    private string? _lastWarning;

    public override void OnLoad()
    {
        _opened = Events.On<GemForgeOpenedEvent>(OnForgeOpened);
        Log.Info("Gem Forge Magic Storage 1.0.0 loaded: automatic native " +
            "Magic Chest gems and same-base essence payments (no commands or inventory loans).");
    }

    public override void OnUnload()
    {
        _opened?.Dispose();
        _opened = null;
        // Kiln itself withdraws the mod's offers when unloading, including
        // when the server world is already gone.
        _offered.Clear();
    }

    private static bool IsMagicChest(EntityId chest)
    {
        var name = Spawn.Inspect(chest)?.TemplateName;
        return name is not null &&
            name.Contains("Storage", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith("_Magic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCurrentBase(EntityId player, out BaseId id)
    {
        id = BaseId.None;
        if (!Players.Available || !Bases.Available || !Items.Available || !Spawn.Available)
            return false;

        var p = Players.Find(player);
        if (p is not { InGame: true } || !p.Can(PlayerPermissions.AccessInventories))
            return false;

        var baseInfo = Bases.Of(player);
        if (baseInfo is null || baseInfo.Id.IsNone)
            return false;

        id = baseInfo.Id;
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

        // Use only the base the player stands in, and the game's own crafting
        // stock list. That list can include plants: exclude everything except
        // genuine Magic Chests.
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

    private void WarnOnce(string message)
    {
        if (message == _lastWarning) return;
        _lastWarning = message;
        Log.Warn("Gem Forge Magic Storage: " + message);
    }

    private void OnForgeOpened(GemForgeOpenedEvent e)
    {
        try
        {
            // No more configuration or user-facing commands: integration is
            // automatic whenever the native forge is open.
            if (!Capabilities.Is(Gems.SourcesCapability) ||
                !Capabilities.Is(Gems.PayersCapability))
            {
                Withdraw(e.Player);
                WarnOnce("Kiln gems.sources or gems.payers unavailable; no chest gems offered");
                return;
            }

            if (e.Forge.IsNone ||
                !TryCurrentBase(e.Player, out var baseId) ||
                Bases.Of(e.Forge)?.Id != baseId)
            {
                Withdraw(e.Player);
                return;
            }

            var next = new HashSet<EntityId>();
            var count = 0;
            foreach (var (chest, gems) in EligibleChests(baseId))
            {
                // Never exceed Kiln's 256-offer limit for the player.
                // If one chest would exceed it, skip that whole chest.
                if (gems > MaxGemOffersPerPlayer - count) continue;

                // Kiln creates short-lived, player/session-bound offers and
                // validates the native action against the chest's real slot.
                var offer = Gems.OfferChestGems(e.Player, chest, SourceOptions);
                if (!offer.Ok)
                {
                    WarnOnce("chest offer refused: " + offer.Refusal);
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
            _lastWarning = null; // allow a future distinct failure to be logged once
        }
        catch (Exception ex)
        {
            try { Withdraw(e.Player); }
            catch (Exception) { /* Kiln expires the session-bound offers. */ }
            WarnOnce("offer failed (no inventory moved by this mod): " + ex.Message);
        }
    }
}
