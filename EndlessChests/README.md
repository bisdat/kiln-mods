# Endless Chests (Kiln) — 0.2.0 (experimental)

A new C# implementation of Endless Chests using the **supported Kiln SDK**. No ShroudForge DLL is loaded or redistributed. The original ShroudForge mod is attributed to HipShot808.

**Status:** The initial 0.1.0 chest binding and replenishment worked in an actual solo game. The expanded 0.2.0 group catalogue, magic-chest handling and destructive `Empty` flow are **not yet gameplay tested**. Use a test world and back up its save.

## What's new in 0.2.0

- Ships the **exact user-provided [endless-chests.defaults.ini](defaults/endless-chests.defaults.ini)** containing **195 original named groups, 3,464 group entries, 3 original aliases and 45 default disabled items**. There are intentionally no renamed or invented group names.
- On first launch copies the defaults to `Context.DataDirectory/endless-chests.ini` (usually `kiln/data/endless-chests-kiln/endless-chests.ini`). Edit that **active** INI for owner settings, custom groups, aliases, disabled groups and items. `echest reload` applies changes. Existing INI files are never overwritten.
- `[DisabledGroups]` blocks the named group and every one of its items across **all** groups and aliases; `[DisabledItems]` blocks exact item names everywhere. Missing or out-of-version catalog items are skipped with a log warning rather than crashing the refill.
- Group sizes up to the chest's available slots, including original groups containing more than 16 items. `[Options] AllowMagicChests=1` enables matching placed storage templates that contain "Magic", up to 72 slots; this path needs an in-game test.
- Special selection **`Empty`**: preview the nearest chest's contents, then explicitly confirm deletion of exactly those item slots. The mod **unbinds the chest before deleting**, so it does not refill again. No other chest's contents are deleted. Item changes use `Items.TakeSlot` with exact slot and item-entity validation.

### Kiln SDK limitation: in-game chest names

The **official Kiln API does not currently expose a supported accessor/event for player-renamed chest names**. Therefore **renaming a chest in Enshrouded's own Rename UI still does not trigger this mod**. Name selection is instead assigned through the mod's `echest bind <name>` command. This is a name-based selection catalogue, **not** automatic UI rename detection. Tracked separately in [issue #4](https://github.com/bisdat/kiln-mods/issues/4).

The `Empty` operation is **one-shot**, rather than continuously deleting new items while a chest is named Empty, since a Kiln-only mod cannot detect when you rename that chest away from Empty. There is **no automatic wipe on startup**.

The defaults also contain `EnableGemLevels=1` and `FullyUpgradeGear=1`; Kiln's ordinary item transaction service does not expose their original behaviour, so these settings are currently preserved but **not implemented**. Item level/rarity and fully unlocked enhancements are not equivalent to the ShroudForge original.

## Download and installation

1. Install a compatible Kiln release in the **host** game (this mod builds against the published Kiln `v0.2.0-dev.15` commit).
2. Open [GitHub Actions — Package Endless Chests (Kiln)](https://github.com/bisdat/kiln-mods/actions/workflows/endless-chests.yml), then a successful run. Download its `endless-chests-kiln-package` artifact.
3. Extract the outer artifact archive and import its **`endless-chests-kiln-0.2.0.zip`** into Kiln's Library. Add the mod to the active profile. Never install the original ShroudForge DLL alongside it.
4. Enter a saved world, stand within **4 metres** of an **empty** ordinary storage chest and run a command.

The mod saves position/world-ID-based bindings in `chests.json` and retains them when upgrading from 0.1.0. **The old `Groups` field inside `chests.json` is now ignored:** groups and owner policy live in `endless-chests.ini`. Copy any custom 0.1.0 groups to the INI's `[Groups]` section before upgrading.

### Offline solo / locally hosted world

Enshrouded's offline mode does not expose game chat. Kiln provides a control-folder console which acts as the host player after you enter the world. In the **game installation directory** that contains `kiln/`:

```bash
mkdir -p kiln/control/console
printf 'echest bind Starter Blocks\n' > kiln/control/console/choose.tmp
mv kiln/control/console/choose.tmp kiln/control/console/choose.cmd
```

Read `kiln/control/console/choose.result.json` to see the command's result. Use a new `.cmd` filename for the next command, after its previous file has been consumed.

You can use **any original name**, including `Stone Blocks`, `Wood Blocks`, `Tools`, `Crafting A-C`, `Decorations 6`, `Magic Chests`, `Diver’s Endurance Flask`, `$$$`, or an exact item label. If the game no longer includes an item, the mod skips it rather than inventing a replacement.

### Dedicated server / host chat

An admin can type `/echest bind Stone Blocks` in the game's chat when chat is enabled. The server's own console without a character cannot identify the nearest chest. For an offline session, use the control folder above.

## Commands

| Command | Effect |
|---|---|
| `echest bind <name>` or `echest name <name>` | Bind nearby **empty** chest to an original group, individual item name or alias |
| `echest groups [search]` | List matching names from the complete group catalogue |
| `echest list` | Show bindings for the current saved world |
| `echest status` | Show Kiln services, world ID, group counts and bindings |
| `echest unbind` | Stop refilling nearest bound chest, **preserving contents** |
| `echest bind Empty` or `echest empty` | **Preview** occupied slots in the nearest chest; deletes nothing |
| `echest empty confirm` | Within 60 seconds, if the same character/chest and exact contents match, **permanently delete the previewed items**, then leave the chest unbound |
| `echest refill` | Schedule a refill check |
| `echest reload` | Reload `chests.json` and `endless-chests.ini` after an edit |

The `Empty` command can permanently destroy valuable items. **Back up your world and use a throwaway chest the first time.** Changed inventories, ambiguous chests and stale confirmations are refused. A concurrent change during deletion aborts further deletion; a completed deletion cannot be undone.

## Refilling details

The mod scans normal player-placed storage chests through `Kiln.Items`, `Kiln.World`, and `Kiln.Spawn`, then gives items through the game's own transaction, replication and save handling. It does not alter random loot chests, crafting stations, villagers, or players.

Every four seconds by default it fills one normal stack of each supported item selected by the binding, up to the chest's usable slots. It never removes unrelated items merely because a group changes. To choose a new group for a used chest, first **unbind** and empty the chest yourself, or explicitly use the two-stage `Empty` operation before binding a new group.

The current mod cannot know when a physical chest is renamed using the game UI. **If you change the visible chest name, its Kiln binding remains unchanged until you issue `echest unbind` or explicitly clear it.**

### Settings

`chests.json` stores `enabled`, `recheckSeconds` (2..120), `positionToleranceMeters` (0.25..2.0) and `chests` bindings. The active `endless-chests.ini` holds the default group/item catalogue and options exactly as provided.

To add an alias:

```ini
[Aliases]
My Building Chest = Starter Blocks
```

To disable a group globally:

```ini
[DisabledGroups]
Beds=1
```

No invalid configuration is overwritten: an error disables generation until you repair the file and run `echest reload`.

## Build and testing

The CI workflow runs `scripts/validate-endless-ini.py` to assert that the full original INI is still present, then packages the mod with the published Kiln SDK source. Local build:

```bash
KILN_UPSTREAM_SHA=eba9a5e1e0e0044a9146fdd53021a751e2957030 \
KILN_SDK_VERSION=0.2.0-dev.local.20261107.61500 \
bash scripts/build.sh EndlessChests
```

Output: `bin/Release/endless-chests-kiln-0.2.0.zip`. Version tags `endless-chests-v0.2.0` publish release ZIPs. CI compilation and the first 0.1.0 in-game report do not substitute for a 0.2.0 validation of normal vs magic chests, aliases, owner blocks and `Empty` on a backed-up test world.
