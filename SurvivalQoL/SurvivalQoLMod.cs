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
    private bool _attributesUnavailableWarned;

    private sealed class Recovery
    {
        public ulong SteamId;
        public ulong JoinedStep;
        public double HealthRemainder;
        public double ManaRemainder;
        public readonly StaminaRegenScale Stamina = new();
        public readonly ResourceScale Oxygen = new();
        public readonly ResourceScale BodyHeat = new();
    }

    // Baselines are per player identity, per attribute and per selected slider
    // value. Reconnection, a changed maximum or a changed multiplier resets
    // the baseline instead of interpreting the change as natural depletion.
    private sealed class ResourceScale
    {
        public int? PreviousAdjusted;
        public int? PreviousMax;
        public int Multiplier;
        public int Remainder;

        public void Reset()
        {
            PreviousAdjusted = null;
            PreviousMax = null;
            Multiplier = 0;
            Remainder = 0;
        }
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

        Log.Info("Survival QoL loaded. Stamina recovery, oxygen and frost multipliers use Kiln.Attributes (dev.20+).");
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
            // Also prevent a Kiln-enabled glider from disengaging due to stamina costs.
            UnlimitedGliding = options.InfiniteStamina ? true : null,
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

        if (!Players.Available)
        {
            _recovery.Clear();
            return;
        }

        var options = _config.Value;
        bool needsAttributes = options.UnderwaterBreathMultiplier > 1
            || options.FrostResistanceMultiplier > 1
            || (!options.InfiniteStamina && options.StaminaRegenMultiplier > 1);
        bool canScale = needsAttributes && Kiln.Attributes.Available;
        if (canScale)
            _attributesUnavailableWarned = false;
        else if (needsAttributes && !_attributesUnavailableWarned)
        {
            Log.Warn("Kiln.Attributes unavailable: stamina regeneration, oxygen and frost multipliers require Kiln dev.20 or newer.");
            _attributesUnavailableWarned = true;
        }

        // Run on every server Pre tick, separately from health/mana regeneration.
        // WallDelta is only used for the existing regeneration point budgets.
        double seconds = Math.Clamp(tick.WallDelta.TotalSeconds, 0.0, 0.25);
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

            if (canScale)
            {
                ScaleAttribute(player.Entity, GameAttribute.Oxygen, options.UnderwaterBreathMultiplier, state.Oxygen);
                ScaleAttribute(player.Entity, GameAttribute.BodyHeat, options.FrostResistanceMultiplier, state.BodyHeat);
                // Infinite Stamina always takes precedence. The multiplier is
                // independent and only speeds the game's observed regeneration.
                if (!options.InfiniteStamina)
                    ScaleStaminaRegen(player.Entity, options.StaminaRegenMultiplier, state.Stamina);
                else
                    state.Stamina.Reset();
            }
            else
            {
                state.Oxygen.Reset();
                state.BodyHeat.Reset();
                state.Stamina.Reset();
            }

            if (seconds <= 0.0) continue;

            if (options.FastHealthRegen && options.HealthRegenPerSecond > 0 && Combat.Available)
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

            if (options.FastManaRegen && options.ManaRegenPerSecond > 0)
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

    // Observe stamina after the game's own step. Only positive recovery since
    // the last corrected sample is multiplied: no game recovery means no bonus,
    // and normal sprinting/spending never creates synthetic stamina.
    //
    // The official API does not label the reason for a stamina increase.
    // An instant item/buff restoration can therefore also be magnified; changes
    // of maximum, player identity, setting and the infinite-stamina mode reset
    // the baseline to avoid retroactive credits.
    private static void ScaleStaminaRegen(EntityId entity, int multiplier, StaminaRegenScale state)
    {
        if (multiplier <= 1)
        {
            state.Reset(); // 1x leaves vanilla stamina regeneration untouched.
            return;
        }

        if (Kiln.Attributes.Get(entity, GameAttribute.Stamina) is not { Max: int max } reading
            || max <= 0 || reading.Current < 0 || reading.Current > max)
        {
            state.Reset();
            return;
        }

        int current = reading.Current;
        int extra = state.Extra(current, max, multiplier);
        if (extra > 0)
        {
            // The budget is capped at the game's current maximum.
            if (!Kiln.Attributes.TrySet(entity, GameAttribute.Stamina, current + extra, out var updated)
                || updated.Current < current || updated.Current > max)
            {
                state.Reset();
                return;
            }
            current = updated.Current;
        }
        state.Sample(current, max, multiplier);
    }

    // Called only on the authoritative Tick.Pre. Measure the loss since the last
    // corrected sample and refund (multiplier-1)/multiplier of it. Keeping the
    // integer remainder spreads 1-point spends correctly: at 10x, nine out of
    // every ten consumed points are eventually returned.
    //
    // This scales *net observed depletion*, not the engine's maximum, timer or
    // world settings. Surface refills, gear changes and config edits reset the
    // accumulator rather than producing spurious bonus resource points.
    private static void ScaleAttribute(EntityId entity, GameAttribute attribute,
        int multiplier, ResourceScale state)
    {
        if (multiplier <= 1)
        {
            state.Reset(); // 1x is strictly vanilla: no attribute writes.
            return;
        }

        if (Kiln.Attributes.Get(entity, attribute) is not { Max: int max } reading
            || max <= 0 || reading.Current < 0)
        {
            state.Reset();
            return;
        }

        int current = reading.Current;
        if (state.PreviousMax == max && state.Multiplier == multiplier
            && state.PreviousAdjusted is int previous)
        {
            if (current < previous)
            {
                int lost = previous - current;
                var (refund, remainder) = DepletionMath.Refund(lost, multiplier, state.Remainder);
                if (refund > 0)
                {
                    int requested = Math.Min(max - current, refund);
                    if (requested > 0
                        && Kiln.Attributes.TrySet(entity, attribute, current + requested, out var updated))
                    {
                        current = updated.Current;
                        // A different result means another clamp/formula intervened:
                        // discard the credit, never accumulate unearned refunds.
                        state.Remainder = current - reading.Current == requested ? remainder : 0;
                    }
                    else
                    {
                        state.Reset();
                        return;
                    }
                }
                else state.Remainder = remainder;
            }
            else if (current > previous)
            {
                // E.g. breathing on the surface or entering a warm area.
                state.Remainder = 0;
            }
        }
        else state.Remainder = 0; // New player, equipment maximum or multiplier.

        if (current >= max)
            state.Remainder = 0;
        state.PreviousAdjusted = current;
        state.PreviousMax = max;
        state.Multiplier = multiplier;
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
    }

    public override void OnUnload()
    {
        _resources?.Dispose();
        _resources = null;
        _durability.Restore(Log.Warn);
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
