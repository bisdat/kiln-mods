# Endless Chests (Kiln) — v0.3.0 (experimental)

An independent C# implementation of Endless Chests using Kiln's Items, World, Spawn and Saves services. It does **not** load or redistribute the ShroudForge DLL. The original ShroudForge mod is attributed to HipShot808.

**v0.3.0 automatically recognizes exact chest names set in Enshrouded's in-game Rename UI**, including the **195 original groups**, the original **3 aliases**, and individual item names found in the defaults. No extra console binding is required for an **unbound** renamed chest.

The previous versions' basic refill (v0.1.0) and confirmed `Empty` command (v0.2.0) worked in the user's hosted game. The v0.3.0 automatic matcher is **CI-tested but still needs an in-game refill check**. Back up your world and use a test chest before relying on it.

## Why game-UI chest names now work

On 7 November 2026, three read-only `echest nameprobe` calls to one 56-slot chest (`Prop_Decoration_T4_Storage_56`, entity 1353) returned the game's 8-byte `CustomPlayerString` IDs:

| Game-UI chest name | Observed ECS value | Local FNV-1a64 UTF-8 test |
|---|---|---|
| Starter Blocks | `0x35F105E7ECC53E32` | Exact match |
| Tools | `0xC26501E3ADE7DA3C` | Exact match |

These are exactly the **FNV-1a 64-bit fingerprints** of the two strings. The mod hashes all original names once when the INI loads and compares their fingerprints with the chest's component **read-only** through `World.ComponentSize` and `World.TryRead<ulong>`. This requires **no native pointer walking, undocumented memory offsets, client-only DLL, or private string-store access**. The exact original spelling, spaces, case and Unicode punctuation matter. Unrecognized names are ignored safely. FNV collisions among candidate names are detected and excluded.

[Greeneye0's Quick Deposit](https://github.com/Greeneye0/enshrouded-quick-deposit/blob/main/src/inventory.cpp) inspired the component search; the Kiln-native hash recognition is independently implemented.

## Automatic name-based refills (NEW)

1. Install v0.3.0 and enter your hosted world.
2. Place an **ordinary empty storage chest** (24/32/40/56 slots), or select an existing one with free slots. A magic chest is included only if the template/slot checks and `AllowMagicChests` permit it.
3. Use Enshrouded's **Rename** interface to name the chest **exactly** `Starter Blocks`, `Tools`, `Stone Blocks`, `Crafting A-C`, or any original group/item/alias name. The game may use an existing nonempty chest; the mod **does not delete unrelated items**.
4. The chest automatically receives one normal stack of each supported item, where room exists. Withdraw items and the mod refills the missing quantities (every 4 seconds by default).
5. **To stop** automatic refills, rename the chest to something *not* in the original group/item/alias list (or remove it). To change groups, rename to the exact new group name. Old items are **not** automatically removed.

A manually bound chest from v0.1/v0.2 retains its explicit `echest bind <name>` selection **ahead of** the game-UI name. Use `echest unbind` beside that chest to switch to automatic name-based behaviour; the contents are preserved. All old `chests.json` bindings are retained during upgrades.

To disable automatic names, set `"autoNames": false` in the mod's active `chests.json` and run `echest reload`. Manual binding remains functional.

### Important: `Empty` is destructive, and still requires confirmation

**Renaming a chest to `Empty` never deletes anything automatically.** It suspends refilling and requires an administrator to preview and confirm the exact chest contents. This deliberately differs from the ShroudForge edition to prevent irreversible losses from an accidental rename.

In Kiln's command interface, while standing next to the intended chest:

```text
echest bind Empty
echest empty confirm
```

The first command **only previews** the occupied slot count and total items. The second must be issued within 60 seconds by the same player, at the same chest in the same world, with the exact contents unchanged. It then deletes the previewed slots through `Kiln.Items.TakeSlot` and unbinds the chest before deleting. If any slot changes during deletion, further deletion stops. There is **no undo**. Back up your world and test with disposable items. You can also preview with `echest empty`.

## Original catalogue and configuration

The bundled [endless-chests.defaults.ini](defaults/endless-chests.defaults.ini) is the supplied original default set, containing **195 groups**, **3,464 group entries**, **3 aliases**, and **45 disabled-item entries**. On first launch the mod copies it to `Context.DataDirectory/endless-chests.ini` (typically `kiln/data/endless-chests-kiln/endless-chests.ini`); an existing active file is **never overwritten**. All original group names are retained exactly.

The active INI supports `[Groups]`, `[Aliases]`, `[DisabledGroups]`, `[DisabledItems]` and `[Options]`. Disabled groups prohibit their member items in other groups as well. Unavailable game item labels are skipped and logged, rather than invented or replaced. A catalogue group is limited to the chest's available storage slots.

The original INI options `EnableGemLevels` and `FullyUpgradeGear` remain **unimplemented**, as they need specialised item construction APIs beyond the current normal transaction service. Magic chest support needs a live test.

`chests.json` stores `enabled`, `autoNames` (defaults true), `recheckSeconds` (2..120), `positionToleranceMeters` (0.25..2) and any manual chest bindings. Broken configuration disables refills instead of overwriting user data.

## Installation

1. Use a compatible Kiln runtime (this repo pins the published `v0.2.0-dev.15` SDK source).
2. Open [Package Endless Chests (Kiln) workflow](https://github.com/bisdat/kiln-mods/actions/workflows/endless-chests.yml) and download `endless-chests-kiln-package` from a **successful** run.
3. Extract that GitHub artifact, and import the enclosed **`endless-chests-kiln-0.3.0.zip`** into Kiln Library. Enable it in your game profile.
4. Restart Enshrouded and enter a world. Do not install the original ShroudForge DLL.

## Kiln console commands

In an **offline solo / locally hosted world**, Kiln runs console files as your player after you enter the game. Open a terminal **in the game's installation folder**, where `kiln/` exists:

```bash
mkdir -p kiln/control/console
printf 'echest nameprobe\n' > kiln/control/console/nameprobe.tmp
mv kiln/control/console/nameprobe.tmp kiln/control/console/nameprobe.cmd
cat kiln/control/console/nameprobe.result.json
```

If the result is not present immediately, the game has not polled the file yet. Create a new `.cmd` file for each new command and read its matching `.result.json`. For chat-enabled multiplayer, admins can use `/echest nameprobe`.

| Command | Purpose |
|---|---|
| `echest nameprobe` | **Read-only**: show nearest chest's ECS hash, matched original name, and any manual binding |
| `echest status` | Show services, group and hash-map counts, autoNames status, active world |
| `echest groups [search]` | Find names in the full original group catalogue |
| `echest bind Starter Blocks` | Explicitly bind an empty nearby chest (overrides game-UI name until unbound) |
| `echest unbind` | Remove manual binding and leave items untouched; game-name matching takes over |
| `echest bind Empty` / `echest empty` | Preview one-time irreversible deletion of nearby chest's contents |
| `echest empty confirm` | Confirm exact preview within 60 seconds; deletes content and removes manual binding |
| `echest refill` | Ask for a refill scan next world step |
| `echest reload` | Reload active INI and JSON config |

All commands are administrator-only. On dedicated servers a console with no player position cannot use commands requiring the *nearest* chest; use an authorised player instead.

### Limitations and safety

- Exact known-name matching has been observed for **two names** on one hosted chest; it is not a general-purpose decoder for arbitrary chest text.
- The hash is not cryptographic. The mod refuses collisions among names in its configured catalogue; a deliberately chosen unknown collision is theoretically possible, but this path only **gives** items, never automatically removes them.
- The source uses only Kiln's read-only component API for names, and transactional `Items.Give` / confirmed `Items.TakeSlot` for inventory operations.
- A name change stops subsequent automatic refill transactions as soon as the next hash check detects it. Already-submitted transactions may finish.
- Magic chests, equipment levels, custom spelling variants, aliases with Unicode punctuation, dedicated-server name replication, and name persistence after restart need field testing before we mark full parity.
- **Never assume renaming to `Empty` will clear a chest without confirmation.**

## Development / CI

The workflow verifies the bundled INI counts, checks the observed FNV fingerprints against `Starter Blocks` and `Tools`, checks all known candidate names for collisions, and builds against a **pinned** official Kiln SDK commit. Both mods have separate CI workflows.

Local Linux build:

```bash
KILN_UPSTREAM_SHA=eba9a5e1e0e0044a9146fdd53021a751e2957030 \
KILN_SDK_VERSION=0.2.0-dev.local.20261107.61500 \
bash scripts/build.sh EndlessChests
```

Output: `bin/Release/endless-chests-kiln-0.3.0.zip`. A GitHub release can be published by pushing tag `endless-chests-v0.3.0`. Development remains in draft [PR #3](https://github.com/bisdat/kiln-mods/pull/3); remaining parity work is in [issue #4](https://github.com/bisdat/kiln-mods/issues/4).
