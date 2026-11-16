// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.AutomaticDoors;

/// <summary>
/// Experimental automatic doors. Tracked state writes need in-game verification
/// of swing animation, collider and NPC path-grid effects.
/// </summary>
public sealed class AutomaticDoorsMod : Mod
{
    private ModConfig<AutomaticDoorsConfig> _config = null!;
    private readonly Dictionary<uint, long> _owned = new();
    private readonly HashSet<uint> _seen = new();
    private long _next;
    private bool _scanning, _stopping, _warned;
    private int _doors, _players, _npcs;
    // Stage counts distinguish 'no doors loaded' from a mismatched ECS signature.
    private int _switches, _directionals, _paired, _withPosition, _readable;
    private double? _nearestDoorMetres;

    public override void OnLoad()
    {
        _config = Config.Register<AutomaticDoorsConfig>();
        Context.Tick.Pre += OnTick;
        Log.Info("Automatic Doors 0.1.1 EXPERIMENTAL: verify animation, collision and pathfinding in game.");
    }

    private void OnTick(in TickInfo tick)
    {
        if (_scanning || _stopping) return;
        long now = Environment.TickCount64;
        if (now < _next) return;
        _next = now + Math.Clamp(_config.Value.PollIntervalMs, 100, 2000);
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        _scanning = true;
        try
        {
            var cfg = _config.Value;
            if (!cfg.Enabled) return;
            // During hosted-world bootstrap the mod can tick before the
            // authoritative world and its write program become available.
            // Do not report this normal startup condition as a permanent fault.
            if (!World.Available || !World.Status.WorldLive) return;
            if (!World.SupportsTrackedWrites || !Players.Available)
            {
                Warn("Kiln.World tracked writes or Kiln.Players unavailable in the live world.");
                return;
            }

            var actors = new List<WorldPosition>();
            _players = _npcs = 0;
            if (cfg.OpenForPlayers)
                foreach (var player in Players.All)
                    if (World.Position(player.Entity) is { } at)
                    {
                        actors.Add(at);
                        _players++;
                    }
            if (cfg.OpenForNpcs)
                foreach (var npc in World.Find("DynamicActiveNpcState"))
                    if (World.Position(npc) is { } at)
                    {
                        actors.Add(at);
                        _npcs++;
                    }

            double openRadius = Math.Clamp(cfg.OpenRadius, 0.5, 8.0);
            double closeRadius = Math.Max(openRadius + 0.5, Math.Clamp(cfg.CloseRadius, 1.0, 12.0));
            double openSquared = openRadius * openRadius;
            double closeSquared = closeRadius * closeRadius;
            double vertical = Math.Clamp(cfg.VerticalTolerance, 1.0, 5.0);
            int delay = Math.Clamp(cfg.CloseDelayMs, 0, 10000);
            long now = Environment.TickCount64;
            _seen.Clear();
            _doors = _paired = _withPosition = _readable = 0;
            _nearestDoorMetres = null;
            // Independent query: door entities may be represented differently
            // from ordinary toggles on this version of the game.
            _directionals = World.Find("DirectionalInteraction").Count;
            var switches = World.Query<Ecs.ServerToggleState>();
            _switches = switches.Count;

            // A door has both ServerToggleState and DirectionalInteraction.
            // InteractionToggle excludes unrelated directional-only entities.
            foreach (var door in switches)
            {
                if (_stopping) return;
                if (!World.Has(door, "DirectionalInteraction")) continue;
                _paired++;
                // Do not remove the safety filter until we have live evidence
                // that another game object genuinely represents a door.
                if (!World.Has(door, "InteractionToggle")) continue;
                if (World.Position(door) is not { } at) continue;
                _withPosition++;
                if (!World.TryRead<Ecs.ServerToggleState>(door, "ServerToggleState", out var state)) continue;
                _readable++;

                uint id = door.Value;
                _doors++;
                _seen.Add(id);
                foreach (var actor in actors)
                {
                    double dx = actor.X - at.X, dz = actor.Z - at.Z;
                    double metres = Math.Sqrt(dx * dx + dz * dz);
                    if (_nearestDoorMetres is null || metres < _nearestDoorMetres)
                        _nearestDoorMetres = metres;
                }
                bool nearOpen = false;
                bool nearClose = false;
                foreach (var actor in actors)
                {
                    if (Math.Abs(actor.Y - at.Y) > vertical) continue;
                    double dx = actor.X - at.X, dz = actor.Z - at.Z;
                    double d2 = dx * dx + dz * dz;
                    if (d2 <= closeSquared) nearClose = true;
                    if (d2 <= openSquared) { nearOpen = true; break; }
                }

                if (nearOpen)
                {
                    if (_owned.ContainsKey(id)) _owned[id] = now;
                    if (!state.state)
                    {
                        var result = await World.Update<Ecs.ServerToggleState>(
                            door, (ref Ecs.ServerToggleState s) => s.state = true);
                        if (_stopping) return;
                        if (result.Ok && result.Changed) _owned[id] = Environment.TickCount64;
                        else if (!result.Ok) Warn(result.Reason);
                    }
                    continue;
                }

                // Never close a door already open before this mod touched it.
                if (!_owned.TryGetValue(id, out long lastNear)) continue;
                if (!state.state) { _owned.Remove(id); continue; }
                if (nearClose) { _owned[id] = now; continue; }
                if (now - lastNear < delay) continue;

                var closing = await World.Update<Ecs.ServerToggleState>(
                    door, (ref Ecs.ServerToggleState s) => s.state = false);
                if (_stopping) return;
                if (closing.Ok) _owned.Remove(id);
                else Warn(closing.Reason);
            }

            // Discard missing entities: their ids may be reused later.
            foreach (uint id in _owned.Keys.ToArray())
                if (!_seen.Contains(id)) _owned.Remove(id);
        }
        catch (Exception ex) { Warn(ex.ToString()); }
        finally { _scanning = false; }
    }

    private void Warn(string reason)
    {
        if (_warned) return;
        _warned = true;
        Log.Warn("Automatic Doors: " + reason);
    }

    [Command("adoors", Description = "Automatic Doors diagnostics: /adoors status")]
    private string Status(CommandContext c, string mode = "status")
    {
        if (mode != "status") throw c.Usage("use /adoors status");
        return "Automatic Doors experimental; " + (_config.Value.Enabled ? "enabled" : "disabled") +
            "; doors=" + _doors + "; switches=" + _switches +
            "; directional=" + _directionals + "; paired=" + _paired +
            "; positioned=" + _withPosition + "; readable=" + _readable +
            "; nearest=" + (_nearestDoorMetres is { } d ? d.ToString("F1") + "m" : "none") +
            "; players=" + _players + "; NPCs=" + _npcs +
            "; mod-opened=" + _owned.Count + "; writes=" + World.SupportsTrackedWrites;
    }

    public override void OnUnload()
    {
        _stopping = true;
        Context.Tick.Pre -= OnTick;
        _owned.Clear();
    }
}
