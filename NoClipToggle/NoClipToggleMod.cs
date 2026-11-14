// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.NoClipToggle;

// Only a toggle request crosses the wire. There is deliberately no target-player
// argument: the server acts on the authenticated sender (NetContext.Player).
public sealed record ToggleRequest(byte Command) : INetMessage<ToggleRequest>
{
    public void Encode(NetWriter writer) => writer.Byte(Command);
    public static ToggleRequest Decode(ref NetReader reader) => new(reader.Byte());
}

public sealed record ToggleReply(bool Ok, bool On, string Text) : INetMessage<ToggleReply>
{
    public void Encode(NetWriter writer) => writer.Bool(Ok).Bool(On).String(Text);
    public static ToggleReply Decode(ref NetReader reader) =>
        new(reader.Bool(), reader.Bool(), reader.String(512) ?? "");
}

/// <summary>
/// In-game shortcut for Kiln's own NoClip switch, not an alternative flight or
/// collision implementation. Server-only NoClip API calls always run on the
/// authoritative world tick, never on the client key/input thread.
/// </summary>
public sealed class NoClipToggleMod : Mod
{
    private const string ChannelName = "noclip-toggle:control";
    private const string BindingId = "noclip-toggle.toggle";

    private NetChannel? _channel;
    private KeyBinding? _key;

    public override void OnLoad()
    {
        // Register in the same order on client and server. This channel is
        // optional: servers are not required to force the mod on every client.
        _channel = Network.Channel(ChannelName, "1", required: false)
            .Register<ToggleRequest>(OnRemoteRequest, NetDirection.ToServer)
            .Register<ToggleReply>((reply, _) => Show(reply), NetDirection.ToClient);

        if (Client.Available && Client.IsClient && Ui.Available)
            _key = KeyBindings.Register(BindingId, "Backquote", OnHotkey,
                "Toggle Kiln NoClip", "NoClip Toggle");

        Log.Info("NoClip Toggle v0.1.0 loaded; configurable key Backquote. Kiln 0.2.0-dev.27+ required.");
    }

    private void OnHotkey()
    {
        if (!Client.HasLocalPlayer || Client.LocalPlayer.IsNone)
        {
            Hud.Toast("NoClip: enter a world first.", ToastKind.Warning);
            return;
        }

        if (Client.IsHosting)
        {
            // A hosted single-player game has both the client input and an
            // authoritative server tick in this process. Do not rely on a
            // self-connection's network channel being ready.
            var player = Client.LocalPlayer;
            Context.Scheduler.Post(() =>
            {
                var reply = Toggle(player, isHost: true);
                Client.Post(() => Show(reply));
            });
        }
        else if (_channel?.SendToServer(new ToggleRequest(1)) != true)
        {
            Hud.Toast("NoClip: server has no NoClip Toggle channel.", ToastKind.Warning);
        }
    }

    private void OnRemoteRequest(ToggleRequest request, NetContext context)
    {
        if (request.Command != 1)
        {
            context.Reply(new ToggleReply(false, false, "NoClip: invalid toggle request."));
            return;
        }

        // The peer's authenticated player, never an entity supplied by a client.
        context.Reply(Toggle(context.Player, isHost: false));
    }

    private ToggleReply Toggle(EntityId player, bool isHost)
    {
        try
        {
            if (player.IsNone || !Players.Available || Players.Find(player) is null)
                return new(false, false, "NoClip: no active player.");

            // Match Kiln's built-in /noclip permission. The local hosted
            // world's host has full rights, as with Kiln Creative.
            if (!isHost && !Permissions.Has(player, NoClip.PermissionNode))
                return new(false, false, "NoClip: admin permission required.");

            var before = NoClip.Of(player);
            if (!before.On && !NoClip.Available)
                return new(false, false, "NoClip unavailable on this game build or with a conflicting flight mod.");

            var result = NoClip.Set(player, !before.On);
            if (!result.Ok)
                return new(false, result.On, result.Message);

            // When another rule set holds NoClip on, our switch cannot disable
            // it. Report Kiln's actual outcome rather than claiming success.
            if (result.On == before.On)
                return new(true, result.On, result.Message);

            if (!result.On && NoClip.Of(player).Restoring)
                return new(true, false, "NoClip OFF — restoring safe position.");

            return new(true, result.On, result.On ? "NoClip ON" : "NoClip OFF");
        }
        catch (Exception ex)
        {
            Log.Warn($"NoClip Toggle request failed: {ex}");
            return new(false, false, $"NoClip: {ex.Message}");
        }
    }

    private static void Show(ToggleReply reply) =>
        Hud.Toast(reply.Text, reply.Ok ? ToastKind.Success : ToastKind.Warning);

    public override void OnUnload()
    {
        _key?.Dispose();
        _channel?.Dispose();
        // The NoClip state belongs to Kiln, not this mod. A hot reload must
        // not silently turn off a player's separately activated /noclip.
    }
}
