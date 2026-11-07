# Endless Chests (Kiln) — 0.1.0 experimental port

A **new C# mod using Kiln.Sdk**, inspired by the behaviour of the Endless Chests ShroudForge Solo archive. This is **not** a loader shim for its DLL, and **not** yet feature-equivalent to that mod.

The original Endless Chests package is attributed to HipShot808. Its included third-party ShroudForge MIT notice is not a licence to republish its closed-source native chest implementation. This project uses no code, DLL or native hook from that binary; its inventory logic is built from the documented Kiln API.

## Working functionality implemented in source

- Finds ordinary 24/32/40/56-slot placed storage chests through `Items.FindInventories`, `Spawn.Inspect` and `World.Position`. Magic chests and crafting stations are excluded.
- Binds an **empty** chest to a group or exact item label; persists its **world GUID and position**, not an unstable entity ID.
- Fills/replenishes one vanilla-sized stack of each selected item, every four seconds by default, using `Items.Read` and `await Items.Give(..., GiveOptions.AllOrNothing)`. These go through the game's own inventory transaction/save/replication path.
- Leaves existing and unrelated contents untouched, never wipes a chest on rename, and stops refilling after unbinding.
- Configurable groups, refill interval, on/off setting, hot reload via command. Default group names: **Starter Blocks**, Stone Blocks, Wood Blocks, Metal Blocks, Arrows, Tools. Any item known to `Kiln.Items.Lookup` can be bound by its display name.

**Not implemented:** reading chest names set using the game's Rename UI, the original 1,055 groups, auto-clearing/renaming, `Empty` deletion, arbitrary mixed layouts, special fixed-tier gear handling, and automatic import of the ShroudForge INI file. Kiln does not currently document a safe chest-name read/rename event. Do not assume this version responds to in-game chest renames.

## Install

1. Install a compatible Kiln release in the hosting game's Enshrouded installation. This version targets the source commit of Kiln **v0.2.0-dev.15**.
2. In [GitHub Actions — Package Endless Chests (Kiln)](https://github.com/bisdat/kiln-mods/actions/workflows/endless-chests.yml), download a successful `endless-chests-kiln-package` artifact. Inside is `endless-chests-kiln-0.1.0.zip`.
3. Import that inner ZIP into Kiln's **Library** and add it to the active profile, or extract it to `<game>/kiln/mods/`. Do not add the ShroudForge DLL.
4. Enter the offline/hosted world. Stand within four metres of an **empty ordinary chest**, preferably without another chest nearby.

### Offline solo / player-hosted worlds

Kiln documentation states that game chat is unavailable in offline mode. Use its **console folder**. From a shell at the game installation (the one containing `kiln/`), run:

```bash
mkdir -p kiln/control/console
printf 'echest bind Starter Blocks\n' > kiln/control/console/bind.tmp
mv kiln/control/console/bind.tmp kiln/control/console/bind.cmd
```

Kiln processes the file while the host character is in the world. Read `kiln/control/console/bind.result.json` for the outcome; new supplies should appear in the chest after the next refill check.

To bind a single item instead, use:

```text
echest bind Wood Logs
```

Write each new command to a new `.cmd` file (using a temporary file first), or reuse the name after the prior one has been consumed.

### Multiplayer hosted server

When chat is enabled and you have administrator permission, run **`/echest bind Starter Blocks`** while next to an empty chest. On a dedicated server the console has no player position, so use a player-issued command for binding.

## Commands

| Command | Effect |
|---|---|
| `echest bind Starter Blocks` | Bind closest empty ordinary chest to the preset |
| `echest bind Wood Logs` | Bind closest chest to an item from Kiln's catalog |
| `echest list` | List position-based bindings in this saved world |
| `echest status` | Check services, world ID, and stored bindings |
| `echest refill` | Request an immediate scheduled refill check |
| `echest unbind` | Stop refilling nearest bound chest; does **not** remove its contents |
| `echest reload` | Reload the JSON configuration |
| `echest help` | Command summary |

Commands require administrator permission. In an offline world the Kiln host-console carries those rights.

## Settings and troubleshooting

After first load, edit `kiln/data/endless-chests-kiln/chests.json` (or the data directory shown for this mod by Kiln). There are `enabled`, `recheckSeconds` (2..120), `positionToleranceMeters` (0.25..2) and `groups` fields. Each group supports at most 16 distinct item entries and uses exact item-catalog labels. **Do not manually edit `chests` positions or world IDs unless you have backed up the file.**

The mod never silently repairs an invalid config: it disables refilling until the file is fixed and reloaded. If a chest is moved or dismantled the persisted binding remains dormant; stand next to the old chest location and use `echest unbind` before moving the replacement to that spot.

This is a **source/CI build, not yet tested in a live hosted world**. Use a throwaway save and chest first. Check `kiln/logs/kiln.log` and the console command `.result.json` for errors. There are no raw memory offsets or direct item component writes; item changes use the game's normal inventory transaction service. Item entities may be created by the game on its next frame.

## Build

From the repository root on Linux/WSL with .NET 10 and git:

```bash
KILN_UPSTREAM_SHA=eba9a5e1e0e0044a9146fdd53021a751e2957030 \
KILN_SDK_VERSION=0.2.0-dev.local.20261107.61500 \
bash scripts/build.sh EndlessChests
```

This packages the pinned Kiln v0.2.0-dev.15 SDK and produces `bin/Release/endless-chests-kiln-0.1.0.zip`. The [CI workflow](../.github/workflows/endless-chests.yml) does the same, uploads an artifact and publishes a GitHub Release on tag `endless-chests-v0.1.0`.
