# NoClip Toggle for Enshrouded (Kiln)

A tiny client/server Kiln mod that provides a **remappable hotkey** for Kiln's
native NoClip feature. This mod changes **no movement physics, collisions or
terrain**; Kiln 0.2.0-dev.27+ performs flight and safe-position recovery.

**Version:** 0.1.0 (first test candidate; in-game hotkey untested)  
**Default shortcut:** **Ctrl+F10**

## Usage

1. Install the packaged `noclip-toggle-0.1.0.zip` through the Kiln Launcher,
   enable it in your `core-mods` profile, and start Enshrouded with Kiln dev.27+.
2. In the active profile's `config/rules.json` set `"allowNoClip": true`.
   The server/world owner must permit NoClip.
3. Enter your hosted world, press **Ctrl+F10** once to enable, again to disable.
   The HUD shows `NoClip ON`, `NoClip OFF`, or Kiln's refusal reason.
4. Open the in-game Kiln menu with **Insert**, choose **Keys**, and remap
   **NoClip Toggle** to another key. Kiln saves that in the profile's
   `config/keybinds.json`.

The mod supports a local hosted world directly on the server tick. It also
has an optional client/server channel for a Kiln multiplayer server with this
mod installed. For remote players it checks Kiln's built-in
`kiln.command.noclip` admin permission and accepts no player IDs from
the client. Multiplayer has not been game-tested.

NoClip ends when Kiln ends it (including teleport, death and world close).
The mod reads Kiln's live NoClip state on each press, so it never retains a
stale ON/OFF flag. It does not force NoClip off when the mod is unloaded.

If a key fails, use the built-in Kiln control folder's `noclip` command.
Offline single-player does not have normal chat input. For the local
`core-mods` profile, the NoClip policy file is at:
`~/.local/share/kiln/profiles/core-mods/config/rules.json`.

## Build

Run `bash scripts/build-compat.sh NoClipToggle` from the repository root.
The builder selects the latest **published** Kiln dev release, verifies its
release SHA and SDK archive checksums, compiles with .NET 10, then runs
`KilnPack`. The output is `bin/Release/noclip-toggle-0.1.0.zip`.

Alternatively use the **Package NoClip Toggle** GitHub Actions workflow
and download its `noclip-toggle-0.1.0` artifact from a successful build.

## Acceptance checklist

- Hotkey works in hosted Bazzite/Proton without opening a terminal.
- HUD reports ON and OFF, and actual movement follows each change.
- Disabling inside terrain safely restores to a free location (Kiln owns this).
- Key can be rebound through Kiln's Keys menu.
- Toggling after a teleport/death reads the true Kiln state.
- Remote-server requests cannot affect another player or bypass permissions.

NoClip itself (through Kiln's console) was accepted in-game on
14 November 2026; this convenience mod still requires its own hotkey test.
The intermittent Sneak/down movement observed in Kiln belongs to the
upstream movement implementation, not this toggle.

License: MIT (repository root).
