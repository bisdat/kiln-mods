# Base Station Magic Storage — v0.1.0 test candidate

Standalone Kiln mod for all supported autonomous production stations (FactoryStation: charcoal kilns, smelters, looms, grinders and seed beds) inside a Flame Altar base. Requires Kiln **0.2.0-dev.29.14 / mod API 0.2.6**. This is an untested candidate, not a public release.

## Behaviour

**Default OFF.** At a base you explicitly enable, every 5 seconds the mod:
- Tops up the station's **currently selected** recipe to 3 complete crafts (configurable 1–20) through the game's Stations.Fill transaction. Inputs come **only from this base's Magic Chests**, not the player backpack. Water uses the base's water sources.
- Moves finished outputs through native Stations.Collect into **explicitly marked, deposit-enabled Magic Chests** named **BSMS Output** or **BSMS Output 2**, etc. Those chests are then part of the base's normal crafting stock, making charcoal and other outputs immediately available to other production stations and to the blacksmith's native base-stock crafting. It leaves leftovers, inputs and ordinary chests alone.
- **No output destination is selected automatically.** If there is no suitably named Magic Chest, or its name cannot be read reliably, finished products stay safely in the producing station. This prevents accidental deposits into Endless Chests named **Empty**, which are deliberately cleared by that mod.
- Does not change recipe, select options, start/stop states or user decisions. Stopped, stop-pending and no-recipe stations are not auto-filled.
- Supports per-base opt-in and per-station exclusion, serializing a maximum of 128 stations per scan and throttling repeated errors.
- Fails closed when two configured altar policies merge into one base, or when its settings file is invalid.

**This is base-local, not cross-base.** The API works on a station's own Flame Altar base. Even when Building Tools shows chests from another base, that is not part of Kiln.Bases stock.

FactoryStations are not ordinary hand-crafting workbenches or NPC crafting (which already use the game's base-stock path), and not the Gem Forge (see upstream [Kiln #61](https://github.com/frostdev-ops/kiln/issues/61)). No borrowed inventory, external loader, game-memory patches or custom UI.

## Commands (admin only)

| Command | Purpose |
|---|---|
| bstation status | Read-only capability, world, configuration and enabled-base status |
| bstation bases | Read-only all-base inventory and policy overview |
| bstation list | Read-only stations in the base where you stand (up to 64) |
| bstation sinks | Read-only eligible Magic Chest output destinations in this base |
| bstation stock | Read-only current Magic Chest stock of the items produced by the base's selected station recipes |
| bstation plan [n] | **Read-only** fill plan for nearest production station within 12 metres |
| bstation enable [n] | Enable this base; optionally choose target craft count 1–20 |
| bstation disable | Disable this base's automation |
| bstation target n | Set target complete crafts, 1–20 |
| bstation fill on / off | Control auto-filling independently |
| bstation collect on / off | Control auto-collection independently |
| bstation exclude / include | Exclude or restore the nearest production station |

Commands work in in-game admin chat. The Kiln atomic file console works in a hosted solo world with exactly one active player.

## First acceptance test

1. **Back up the saved world.** Download a test ZIP only after the draft PR's Actions build succeeds; use Kiln dev.29.14+.
2. Stand inside a Flame Altar base with a charcoal kiln and a Magic Chest containing logs. Run **bstation status**, **bstation bases**, **bstation list**.
3. Stand within 12 metres of that kiln and run **bstation plan 1**. Verify the item and stock listing; confirm an ordinary chest is not included.
4. **Before enabling collection**, designate one **unbound** Magic Chest as shared stock: use Enshrouded's Rename button and name it **BSMS Output**. Do not use a chest bound to Endless Chests or named Empty. Keep this output chest eligible for area deposits (quick stack).
5. Run **bstation sinks** and confirm that your renamed output chest is listed. This is read-only.
6. Run **bstation enable 1**. Compare the Magic Chest, kiln and your backpack after a scan. Only the base Magic Chest should lose valid inputs; nothing should pass through your backpack.
7. After one production cycle, confirm finished charcoal enters **BSMS Output**. Run **bstation stock** and confirm the charcoal appears in the base's crafting stock; use a blacksmith recipe or the **bstation plan 1** for another charcoal-consuming production station to check downstream availability. Run **bstation disable**.
8. Repeat with a manually stopped station (its stop flag stays), a full destination, and then smelter, loom and water-dependent stations. Check multiple bases, restart and overlapping altars.
9. Inspect kiln/logs/kiln.log for bounded BSMS messages. Do not release until tests pass.

Disable prevents future actions, but cannot undo a previously submitted native transaction.

## Why BSMS Output is required

Endless Chests automatically **deletes anything added to a chest named Empty**, and some of your named chests are automatically refilled. The SDK's default Stations.CollectToBase chooses any deposit-enabled Magic Chest, including ones that another mod may manage. Therefore this mod deliberately does **not** call that generic collector.

Rename one or more **dedicated, unbound** Magic Chests to **BSMS Output**, **BSMS Output 2**, and so on. The mod resolves matching names **afresh at collection time**, only among Magic Chests in the same Flame Altar base which allow deposits. If a sink was renamed, removed, excluded from deposits, full or temporarily unavailable, it is skipped. The output stays at its production station if no safe sink can accept it. Name-reading capability `items.names` must be available; check **bstation status**.

The shared output chest remains a normal Magic Chest, so downstream native recipe payment and other Stations.Fill operations can use its contents. Do not manually bind BSMS Output chests in Endless Chests, or use an Endless Chests-managed chest as an output destination.

## Settings

Kiln stores world + persistent Flame Altar GUID policies in its per-mod data folder:
kiln/data/base-station-magic-storage/base-station-magic-storage.json

A file is only written after an explicit setting command. It is written through a temporary file; parse or write errors disable automation. Station exclusions use template and world position rounded to decimetres; if you move a station, exclude it again.

## Build

The draft-only workflow uses the checksum-verified official Kiln v0.2.0-dev.29.14 SDK with **scripts/build-compat.sh BaseStationMagicStorage** and uploads an experimental base-station-magic-storage-0.1.0.zip artifact. It does not publish a release or add this candidate to the scheduled latest-Kiln workflow.

Tracking: [issue #50](https://github.com/bisdat/kiln-mods/issues/50) and the [official Stations/Bases API](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/stations.md).
