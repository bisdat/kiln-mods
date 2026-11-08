# Cross-base Storage (Kiln) — 0.1.4

**Status: read-only inventory + vanilla craft/build observation prototype — not a working cross-base crafting mod.**

## Goal

> Magic Chests across all your Enshrouded bases act as one crafting supply network. Building blocks, the crafting menu and crafting stations should see the combined materials, while **actual resources are consumed once** from their original storage. Magic Chests in your current base take priority over other bases. Ordinary storage chests are not pooled.

This is the intended feature, **not what this development preview currently delivers**.

The detailed [client/server crafting and building integration design](ENGINE-BRIDGE.md) records the upstream APIs, proposed resolver/transaction contract, and acceptance test matrix.

The existing independent [Cross-Base Storage (Beta) by CharosDX on Nexus Mods](https://www.nexusmods.com/enshrouded/mods/245) reports this behaviour using a native client DLL. Its page states that all files belong to its author and prohibits redistribution or modification without permission. **No files, code, DLLs or game offsets from that mod are used here.** This is a separate Kiln SDK project.

## Why the full feature is not implemented yet

The pinned public Kiln SDK `v0.2.0-dev.15` offers:

- `Items.FindInventories`, `Items.Read`: enumerate and inspect **currently loaded** inventories.
- `Spawn.Inspect` and `World.Position`: resolve chest templates and positions.
- `Items.Give`, `Items.Take`, `Items.TakeSlot`: transact with a specific inventory.
- `Gameplay.CraftEvent` and the recipe registry: inspect or cancel a craft event on the server.

**It does not expose the vanilla client's crafting/building material source list, an API to extend the game's magic-chest source resolver, or a corresponding cross-base-aware server item-consumption path.** Changing chest contents alone would not make the building picker and crafting menus display remote materials, and unsynchronized copying would duplicate stock or lose items. Modifying native memory offsets is also not safe across game updates.

Even a complete live server scan does **not** include unloaded distant bases. The game may represent magically upgraded chests using flags or components other than a template name containing "Magic"; until verified, detecting magic by template name is only a **heuristic**.

For these reasons, the development preview **never moves, gives, takes, consumes, creates or duplicates any item**. It cannot make the normal crafting/building interfaces use other bases' Magic Chests. Enabling it does not alter vanilla storage behaviour.

## What we verified in a live Enshrouded world (8 November 2026)

- Enshrouded's known 72-slot Magic Chest template is `Prop_Decoration_T6_Storage_72_Magic`. The template-name detector reported `magicByTemplate=True` on a real chest.
- Scans from two distant bases returned the same **50 magic-by-template chests**, **558 item types** and **667,221 item units**.
- At Base B, `cbstore stock Yellow Luminescent Block` located 70,500 blocks in **two distant Base A chests** (70,250 in entity 1203 and 250 in entity 1302), confirming the server can read exact inventory counts across those bases in that session.

**Important:** These are real server inventory reads. They are **not** proof that the normal build and craft menus accept those resources, or that unloaded areas would behave the same after restarting a world.

## Newly investigated native craft/build integration (0.1.4)

The pinned Kiln.Gameplay SDK exposes the native game recipe registry and two read-only event families:

- `CraftEvent`: captured before the game's crafting routine, with its operator (player), station, recipe id and requested count. A player normally cannot send this event when the native crafting UI disables its craft button.
- `ItemCraftedEvent`: reports the game's result, including `MissingMaterials`, `NoRoomForOutput` and `Crafted`. This is not an inventory-source resolver and cannot retroactively change the result.
- `BuildEvent`: reports a block/prop/terrain operation *after* the game has attempted it. It cannot intercept the game's preflight material checks, nor can it reveal which chest supplied the building material.
- `Gameplay.Recipe` and `Gameplay.WorkshopRecipes(0)`: let the mod inspect actual recipe inputs/outputs for a known recipe id and discover available hand-crafting recipes.

These interfaces make **observation and planning** possible, not cross-base crafting. Kiln's documented [Creative Mode research](https://github.com/frostdev-ops/kiln/blob/main/docs/re/creative-mode-146.md) identifies a client-side `UiCraftableAmount` lookup that can disable craft actions before a server `CraftEvent` is ever sent. Its building research also distinguishes client material previews and server-side resource payments. A proper native resolver extension must include both sides. No game offsets or native hooks have been added to this mod.

## What this development preview does

It installs as a **server-side Kiln mod** and exposes administrator-only commands for investigating the inventory and template information required to implement the actual feature.

| Command | Result |
|---|---|
| `cbstore status` | Whether discovery services are available and whether actual bridging exists (**not available**) |
| `cbstore scan` | Counts loaded candidate storage chests, probable Magic Chests and pooled material amounts |
| `cbstore templates` | Lists templates of all loaded candidate storage chests to validate magic identification |
| `cbstore near` | Inspect nearest storage chest (within 5m), its template, position and tentative magic flag |
| `cbstore stock Wood Logs` | Show how many Wood Logs are in **loaded magic-by-template candidates**, and their positions |
| `cbstore top` | List the 12 most abundant items in those candidate magic chests |
| `cbstore recipes [filter]` | Search **hand-crafting** recipes by output name and show native recipe IDs; no station recipes included |
| `cbstore plan 0x1234abcd [count]` | Read a native recipe and compare exact ingredient needs against **player stock + all currently loaded magic-by-template chests**; warns for category inputs |
| `cbstore plan last` | Same estimate for your latest **observed vanilla craft request** |
| `cbstore lastcraft` | Show the latest vanilla crafting attempt/result observed for your player; no action recorded if the client disabled its button |
| `cbstore lastbuild` | Show the latest native building outcome for your player, if the client sent an action |
| `cbstore help` | List diagnostics |

`scan`, `stock` and `top` count only storage chest templates that contain **Magic**. This is *not* guaranteed to be the same as the game's Magic Chest property. If `cbstore near` reports `magicByTemplate=false` while standing next to a known Magic Chest, that is useful evidence that an engine-supported flag must be discovered. All commands work on the Kiln server tick thread; `near` requires an in-world player location.

### Local solo command example

With Enshrouded running and your character **inside a loaded world**, open a shell in the game installation directory:

```bash
mkdir -p kiln/control/console
printf 'cbstore scan\n' > kiln/control/console/cbscan.tmp
mv kiln/control/console/cbscan.tmp kiln/control/console/cbscan.cmd

# Kiln writes this after it has polled the command:
cat kiln/control/console/cbscan.result.json
```

For the nearest chest, stand within 5 metres, and use a **different filename**:

```bash
printf 'cbstore near\n' > kiln/control/console/cbnear.tmp
mv kiln/control/console/cbnear.tmp kiln/control/console/cbnear.cmd
cat kiln/control/console/cbnear.result.json
```

Kiln processes the console command with your character context only while you are in a world; at the title screen, commands needing an in-world character may not work. The first command is read-only and can run without chest proximity.

### Verified Workbench ingredient check (8 November 2026)

On version **0.1.3**, the user ran `cbstore plan 0x23c98255 1` in a live world. The native recipe is a hand-crafted **Workbench**, with two input records and one output. The planner found:

- **String:** 3 required, **0** in the player backpack, **716** in **two Magic Chests** (entity `1412` at `3393.6,942.0,3807.8`: 466; entity `1081` at `3383.5,937.0,3796.3`: 250). The exact-item shortage is zero.
- **Second input:** native `itemId=0`, raw `count=0`; current Kiln.Gameplay exposes neither the full recipe input entry nor the category selection details needed to interpret this field. It may represent a category-based ingredient or other placeholder. **It is not proven to be free or cost zero.**
- **Output:** 1 Workbench. **No craft was attempted or produced by the planner.**

The [pinned native recipe implementation](https://github.com/frostdev-ops/kiln/blob/v0.2.0-dev.15/native/services/gameplay/recipes.cpp) reads only `item` at offset `+0x10` and `count` at `+0x14` from each 48-byte native recipe input entry. It does **not** return the remainder of each input record to the managed API. A category/placeholder with a raw zero count therefore **cannot be treated as a complete ingredient requirement**.

**0.1.4 corrects the diagnostic wording:** instead of misleading `CATEGORY ingredient x0`, it explicitly reports `UNRESOLVED CATEGORY/PLACEHOLDER ingredient (itemId=0, rawCount=0)`, warns that the true requirement is unknown and makes no claim that the complete recipe is affordable.

### When the game will not let you craft

The in-game test on **8 November 2026 at 21:14 UTC** returned `gameplay=True`, `craftEventHook=True` and `buildEventHook=True`, but `cbstore lastcraft` found **no request or outcome** and `cbstore lastbuild` found **no event**. This means Kiln exposes the hooks but no native action reached the observer in that test. It is **consistent** with the client refusing a button/action because its ordinary material-source list doesn't include remote Magic Chests; it is not proof that a native craft ran or that server-side permissions were checked. The `cbstore plan last` command originally raised an error because no recipe could be inferred from an absent event.

From **0.1.4**, `cbstore plan last` instead gives a useful explanation and returns a successful diagnostic response when no craft request has occurred. **You do not need to craft anything to inspect recipes.** Run:

```bash
printf 'cbstore recipes\n' > kiln/control/console/cbrecipes.tmp
mv kiln/control/console/cbrecipes.tmp kiln/control/console/cbrecipes.cmd
cat kiln/control/console/cbrecipes.result.json
```

This lists up to 12 registered **hand-crafting** recipes. Select one of the `0x...` recipe IDs from the output and run `cbstore plan 0xRECIPEID 1` using a new console command filename. Alternatively, use `cbstore recipes <output-name>` to filter recipes. `plan` compares exact ingredient amounts with local player items and all server-visible Magic Chests **without creating anything**. Some recipes have category inputs (e.g. “any wood”), which are reported as unresolved.

If **even a normal vanilla craft with all ingredients in your backpack** cannot be performed, that would be a different potential issue, worth checking separately. The current mod does not cancel or inject crafting actions, and cannot tell from an absent event whether the UI, recipe unlock, station requirement or another mod blocked the attempted action.

### Recipe and building diagnostics (0.1.4)

To inspect the real crafting pipeline **without modifying inventory**, perform one ordinary vanilla craft in-game (a cheap one using materials you already have), then run:

```bash
printf 'cbstore status\ncbstore lastcraft\ncbstore plan last\ncbstore lastbuild\n' > kiln/control/console/cbcraft.tmp
mv kiln/control/console/cbcraft.tmp kiln/control/console/cbcraft.cmd
cat kiln/control/console/cbcraft.result.json
```

The result may appear a few seconds after Kiln processes the command file. `lastcraft` reports a request/outcome **only if the game sent one**; `plan last` estimates ingredients only when such a request has been recorded. Without a request, `plan last` now suggests the standalone recipe lookup commands instead. If you have not sent a craft request, use `cbstore recipes <search>` to find the ID of a hand-crafting recipe, then `cbstore plan 0x... 1`.

Use `cbstore lastbuild` **after a normal build action**. It reports only the post-action result. Some material shortages are blocked in the game client before any build/craft event reaches Kiln; missing events are meaningful diagnostic evidence, not proof of an inactive mod.

**The plan's "combined" count is informational only.** It does not resolve vanilla category ingredient selections, recipe unlocks, workshop requirements, current-base permissions, the client's UI availability or actual server-side source-consumption rules. Never treat it as an authorization to craft, nor as a guarantee that the game will craft successfully.


## Installation (diagnostic prototype)

1. Open [Cross-base Storage GitHub Actions](https://github.com/bisdat/kiln-mods/actions/workflows/cross-base-storage.yml).
2. Download `cross-base-storage-kiln-package` from a successful build.
3. Extract the Actions artifact. Import `cross-base-storage-0.1.4.zip` into Kiln Library and enable it in the active profile.
4. Restart Enshrouded, enter a world, run `cbstore scan` and `cbstore near` beside an existing Magic Chest, then use `cbstore lastcraft` / `cbstore plan last` after a normal craft to inspect the game's native recipe handling.
5. This package **does not yet enable cross-base crafting**. If you only want the finished gameplay effect, do not install this diagnostic development preview.

The mod is built against the released Kiln SDK source `eba9a5e1e0e0044a9146fdd53021a751e2957030` with SDK package version `0.2.0-dev.local.20261107.61500`, as used by Endless Chests.

## Requirements to complete the actual mod

These are **future Kiln/upstream features** that require implementation and live client/server validation before a release can honestly promise the requested functionality:

1. A version-safe server/client API for the exact **Magic Chest attribute** on placed storage entities (including upgraded ordinary chests), not a template-name guess.
2. A way to find stock in distant/unloaded base chunks, or a supported save-backed inventory index that stays consistent as worlds stream and save.
3. A hook to extend **the game's existing material-source enumeration** for the building picker, hand crafting, NPC/workbench crafting and station queues, on both client and host/server sides.
4. **Atomic consumption from the real source chest**, preserving the game's normal stack choice, player permissions and same-base-first priority, without item duplication, loss or rollbacks of unrelated inventory.
5. Multiplayer/dedicated-server consistency, including permissions, concurrent crafting, save/restart and world travel; clear fallback if any plugin component is missing.

Until those primitives exist in the installed Kiln runtime, the official crafting and building interfaces cannot be extended safely from this server-only managed SDK mod.

## Development

```bash
KILN_UPSTREAM_SHA=eba9a5e1e0e0044a9146fdd53021a751e2957030 \
KILN_SDK_VERSION=0.2.0-dev.local.20261107.61500 \
bash scripts/build.sh CrossBaseStorage
```

Output: `bin/Release/cross-base-storage-0.1.4.zip`. The workflow builds and uploads this prototype on pull requests and optionally publishes a GitHub **development pre-release** for a matching **numeric** `cross-base-storage-v0.1.4` tag. Future versions use `0.1.4`, `0.1.5`, etc., without an `-alpha.n` suffix. **It should not be presented as a playable cross-base storage release.**
