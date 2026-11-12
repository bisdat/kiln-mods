// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.DebugTools;

public enum DebugBuildAction { On, Off, Status }

/// <summary>
/// An intentionally separate debug mod: none of the user's Survival QoL
/// settings or the built-in /creative preset need to change.
/// </summary>
public sealed class DebugToolsMod : Mod
{
    private ModConfig<DebugToolsConfig> _config = null!;
    private readonly Dictionary<EntityId, RuleSet> _builders = new();

    public override void OnLoad()
    {
        _config = Config.Register<DebugToolsConfig>();
        _config.Changed += _ => RefreshActiveSessions();
        Log.Info("Debug Tools loaded; debug building is OFF until an admin uses /debugbuild on.");
    }

    public override void OnUnload()
    {
        foreach (var set in _builders.Values)
            set.Dispose();
        _builders.Clear();
    }

    [Command("debugbuild", Description = "Admin-only building outside bases, in the Shroud and in dungeon no-build zones (on/off/status).", Permission = CommandPermission.Admin)]
    private string DebugBuild(CommandContext context, DebugBuildAction action = DebugBuildAction.Status)
    {
        if (!context.Source.IsPlayer)
            context.Fail("Use /debugbuild in-game as an administrator, not from the server console.");
        if (!Rules.Available)
            context.Fail("Kiln.Rules is not available on this world.");

        var player = context.Source.Player;
        return action switch
        {
            DebugBuildAction.On => Enable(player),
            DebugBuildAction.Off => Disable(player),
            _ => Status(player),
        };
    }

    private string Enable(EntityId player)
    {
        if (Rules.ClientOf(player) is not (RulesClient.Kiln or RulesClient.Host))
            return "Cannot enable debug building: your game must run Kiln with the rules channel.";

        var hooks = Rules.Status();
        if (hooks is null || !hooks.BuildingZone || !hooks.Placement)
            return "Cannot enable debug building: Kiln's building-zone or placement hook is unavailable for this game build.";

        var rules = BuildRules();
        if (_builders.TryGetValue(player, out var set) && set.Active)
            set.Update(rules);
        else
            _builders[player] = Rules.Apply(player, rules, label: "debug-tools: build anywhere");

        Log.Info($"Debug building enabled for {player} (Shroud={rules.BuildInShroud}, dungeons={rules.IgnoreNoBuildZones}, saved={rules.KeepBuildsOutsideBases}).");
        return "Debug building ON for your character. /debugbuild off restores normal rules; /debugbuild status shows effective permissions.";
    }

    private string Disable(EntityId player)
    {
        if (_builders.Remove(player, out var set))
            set.Dispose();
        return "Debug building OFF for your character (existing placements are not removed).";
    }

    private string Status(EntityId player)
    {
        var enabled = _builders.TryGetValue(player, out var set) && set.Active;
        if (!enabled)
            return "Debug building OFF. Admin: use /debugbuild on. Options are in Kiln's debug-tools configuration.";

        var effective = Rules.Of(player);
        return $"Debug building ON. Effective: outside bases={effective.BuildOutsideBases}, Shroud={effective.BuildInShroud}, dungeon no-build zones={effective.IgnoreNoBuildZones}, enemies nearby={effective.IgnoreEnemiesNearby}, persistent outside bases={effective.KeepBuildsOutsideBases}. Separate game checks (including Flame Altar count/placement limits) are not bypassed.";
    }

    private void RefreshActiveSessions()
    {
        foreach (var (player, set) in _builders.ToArray())
        {
            if (!set.Active)
            {
                _builders.Remove(player);
                continue;
            }

            set.Update(BuildRules());
        }
    }

    private PlayerRules BuildRules()
    {
        var options = _config.Value;
        return new PlayerRules
        {
            // Needed for building outside the bounds of a Flame Altar.
            BuildOutsideBases = true,
            BuildInShroud = options.BuildInShroud ? true : null,
            IgnoreNoBuildZones = options.BuildInDungeons ? true : null,
            IgnoreEnemiesNearby = options.IgnoreEnemiesNearby ? true : null,
            KeepBuildsOutsideBases = options.KeepBuildsOutsideBases,
            BuildCost = options.FreeBuilding ? 0.0 : null,
            // Paired with zero build cost so free building cannot manufacture materials.
            BuildRefund = options.FreeBuilding ? 0.0 : null,
            // Deliberately do not override other players' base permissions.
            IgnoreBasePermissions = null,
        };
    }
}
