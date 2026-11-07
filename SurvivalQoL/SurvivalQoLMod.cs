// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.SurvivalQoL;

/// <summary>
/// Runs only on the authoritative server world (including a single-player hosted world).
/// No memory offsets, unsupported hooks, or external game assemblies are used.
/// </summary>
public sealed class SurvivalQoLMod : Mod
{
    private ModConfig<SurvivalQoLConfig> _config = null!;
    private RuleSet? _resources;
    private readonly Dictionary<uint, Recovery> _recovery = new();
    private readonly WorldSettingOverride _durability = new("enableDurability", false);
    private readonly WorldSettingOverride _diving = new("playerDivingTimeFactor", 2.0);
    private readonly WorldSettingOverride _cold = new("playerBodyHeatFactor", 2.0);

    private sealed class Recovery
    {
        public ulong SteamId;
        public ulong JoinedStep;
        public double HealthRemainder;
        public double ManaRemainder;
    }

    public override void OnLoad()
    {
        _config = Config.Register<SurvivalQoLConfig>();
        _config.Changed += OnConfigChanged;
        Context.Tick.Pre += OnPre;

        if (Rules.Available)
            UpdateRules();
        else
            Log.Warn("Kiln.Rules unavailable: infinite stamina and mana cannot be applied.");

        Log.Info("Survival QoL loaded. Oxygen and frost settings are only 2x extensions, not infinite.");
    }

    private void OnConfigChanged(ConfigChangedEvent _)
    {
        if (Rules.Available) UpdateRules();
        ApplySettings();
        _recovery.Clear();
    }

    private void UpdateRules()
    {
        var options = _config.Value;
        if (!options.InfiniteStamina && !options.InfiniteMana)
        {
            _resources?.Dispose();
            _resources = null;
            return;
        }

        var rules = new PlayerRules
        {
            MinStamina = options.InfiniteStamina ? 1.0 : null,
            MinMana = options.InfiniteMana ? 1.0 : null,
        };

        if (_resources is null)
            _resources = Rules.Apply(RuleScope.Everyone, rules, label: "survival-qol: resource floors");
        else
            _resources.Update(rules);
    }

    private void OnPre(in TickInfo tick)
    {
        // Settings.Ready is false at initial OnLoad and becomes true after a world step.
        ApplySettings();

        if (!Players.Available) return;
        var options = _config.Value;
        if (!options.FastHealthRegen && !options.FastManaRegen) return;

        // WallDelta works on both dedicated and hosted worlds; cap long pauses to prevent bursts.
        double seconds = Math.Clamp(tick.WallDelta.TotalSeconds, 0.0, 0.25);
        if (seconds <= 0.0) return;

        var online = new HashSet<uint>();
        foreach (var player in Players.All)
        {
            uint entity = player.Entity.Value;
            online.Add(entity);

            if (!_recovery.TryGetValue(entity, out var state)
                || state.SteamId != player.SteamId
                || state.JoinedStep != player.JoinedStep)
            {
                state = new Recovery { SteamId = player.SteamId, JoinedStep = player.JoinedStep };
                _recovery[entity] = state;
            }

            if (options.FastHealthRegen && Combat.Available)
            {
                var health = Combat.GetHealth(player.Entity);
                if (health is { Dead: false } current && current.Current > 0 && current.Current < current.Max)
                {
                    state.HealthRemainder += options.HealthRegenPerSecond * seconds;
                    int points = (int)Math.Floor(state.HealthRemainder);
                    if (points > 0)
                    {
                        int requested = Math.Min(points, current.Max - current.Current);
                        Combat.Heal(player.Entity, requested);
                        state.HealthRemainder -= points;
                    }
                }
                else state.HealthRemainder = 0;
            }
            else state.HealthRemainder = 0;

            if (options.FastManaRegen)
            {
                int? mana = Abilities.Attributes.Get(player.Entity, AbilityResource.Mana);
                int? max = Abilities.Attributes.Max(player.Entity, AbilityResource.Mana);
                if (mana is int amount && max is int limit && amount >= 0 && limit > amount)
                {
                    state.ManaRemainder += options.ManaRegenPerSecond * seconds;
                    int points = (int)Math.Floor(state.ManaRemainder);
                    if (points > 0)
                    {
                        int requested = Math.Min(points, limit - amount);
                        if (Abilities.Attributes.Add(player.Entity, AbilityResource.Mana, requested))
                            state.ManaRemainder -= points;
                    }
                }
                else state.ManaRemainder = 0;
            }
            else state.ManaRemainder = 0;
        }

        // Player entity ids are reused. Do not retain recovery budgets for departed players.
        foreach (uint entity in _recovery.Keys.ToArray())
            if (!online.Contains(entity)) _recovery.Remove(entity);
    }

    [Subscribe]
    private void OnFall(EntityFallEvent e)
    {
        if (_config.Value.NoFallDamage && e.IsPlayer) e.Cancel();
    }

    private void ApplySettings()
    {
        if (!Settings.Ready) return;
        var options = _config.Value;
        _durability.Sync(options.InfiniteWeaponDurability, Log.Warn);
        _diving.Sync(options.DoubleUnderwaterBreath, Log.Warn);
        _cold.Sync(options.DoubleFrostResistance, Log.Warn);
    }

    public override void OnUnload()
    {
        _resources?.Dispose();
        _resources = null;
        _durability.Restore(Log.Warn);
        _diving.Restore(Log.Warn);
        _cold.Restore(Log.Warn);
        _recovery.Clear();
        Log.Info("Survival QoL unloaded.");
    }

    /// <summary>
    /// Owns a game-wide setting only when this mod actually changes it.
    /// Restores the previous value only if the setting still holds our value, so we
    /// don't overwrite a setting another mod or administrator changed meanwhile.
    /// Never calls Settings.Save(): the mod's change is session-only.
    /// </summary>
    private sealed class WorldSettingOverride(string name, object target)
    {
        private object? _original;
        private bool _owns;
        private bool _warned;

        public void Sync(bool enabled, Action<string> warn)
        {
            if (!Settings.Ready) return;
            if (!Settings.Has(name))
            {
                if (!_warned) warn($"Kiln.Settings does not expose '{name}' on this game build.");
                _warned = true;
                return;
            }
            try
            {
                if (!enabled)
                {
                    Restore(warn);
                    return;
                }
                if (_owns) return;

                object before = Settings.Get(name);
                if (Equals(before, target)) return;

                var result = Settings.Set(name, target);
                if (Equals(result.After, target))
                {
                    _original = result.Before;
                    _owns = true;
                }
            }
            catch (Exception ex)
            {
                if (!_warned) warn($"Cannot set '{name}': {ex.Message}");
                _warned = true;
            }
        }

        public void Restore(Action<string> warn)
        {
            if (!_owns || !Settings.Ready) return;
            try
            {
                if (Equals(Settings.Get(name), target))
                    Settings.Set(name, _original!);
                _original = null;
                _owns = false;
            }
            catch (Exception ex)
            {
                if (!_warned) warn($"Cannot restore '{name}': {ex.Message}");
                _warned = true;
            }
        }
    }
}
