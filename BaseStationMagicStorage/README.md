# Base Station Magic Storage — v0.1.3 per-station configuration candidate

Standalone Kiln mod for all supported autonomous production stations (FactoryStation: charcoal kilns, smelters, looms, grinders and seed beds) inside a Flame Altar base. Requires Kiln **0.2.0-dev.29.14 / mod API 0.2.6**. v0.1.2 has passed in-game acceptance and remains the published stable release. v0.1.3 adds editable per-station switches and requires separate in-game acceptance before merge.

## Behaviour

**Default OFF.** At a base you explicitly enable, every 5 seconds the mod:
- Tops up the station's **currently selected** recipe to 3 complete crafts (configurable 1–20) through the game's Stations.Fill transaction. Inputs come **only from this base's Magic Chests**, not the player backpack. Water uses the base's water sources.
- Moves finished outputs through native Stations.Collect into **explicitly marked, deposit-enabled Magic Chests** named **crafting output**, optionally followed by any suffix. Matching ignores case and whitespace anywhere in the name. Those chests are then part of the base's normal crafting stock, making charcoal and other outputs immediately available to other production stations and to the blacksmith's native base-stock crafting. It leaves leftovers, inputs and ordinary chests alone.
- **No output destination is selected automatically.** If there is no suitably named Magic Chest, or its name cannot be read reliably, finished products stay safely in the producing station. This prevents accidental deposits into Endless Chests named **Empty**, which are deliberately cleared by that mod.
- Does not change recipe, select options, start/stop states or user decisions. Stopped, stop-pending and no-recipe stations are not auto-filled.
- **Two-stage opt-in for safety:** enabling a base does not activate all its production stations. Select individual stations using **bstation station on** first. After testing, explicitly activate every production station in that base with **bstation all on**. Per-station `enabled`, `fill`, and `collect` switches override the base-wide policy, so an oven can retain its products while other stations collect. Existing exclusions still apply. Each scan is serialized (up to 128 stations) and repeated errors are throttled.
- Fails closed when two configured altar policies merge into one base, or when its settings file is invalid.

**This is base-local, not cross-base.** The API works on a station's own Flame Altar base. Even when Building Tools shows chests from another base, that is not part of Kiln.Bases stock.

FactoryStations are not ordinary hand-crafting workbenches or NPC crafting (which already use the game's base-stock path), and not the Gem Forge (see upstream [Kiln #61](https://github.com/frostdev-ops/kiln/issues/61)). No borrowed inventory, external loader, game-memory patches or custom UI.

## Commands (admin only)

| Command | Purpose |
|---|---|
| bstation status | Read-only capability, world, configuration and enabled-base status |
| bstation bases | Read-only all-base inventory and policy overview |
| bstation list | Read-only station listing with persistent keys, status, and per-station switches (up to 64) |
| bstation config | Show settings path and currently registered stations |
| bstation config sync | Generate missing station entries in JSON from all discovered production stations in current base; preserve previous settings |
| bstation reload | Reload edited JSON immediately; invalid files safely disable automation |
| bstation sinks | Read-only eligible Magic Chest output destinations in this base |
| bstation stock | Read-only current Magic Chest stock of the items produced by the base's selected station recipes |
| bstation plan [n] | **Read-only** fill plan for nearest production station within 12 metres |
| bstation station on / station off | Enable/disable all BSMS actions at nearest station, even with all-stations mode (12 m) |
| bstation station fill on / off | Per-station automatic ingredient replenishment switch |
| bstation station collect on / off | Per-station output collection switch (e.g. oven output stays in the oven) |
| bstation all on / all off | Deliberately enable automation for every station in this base, or return to individual opt-in |
| bstation enable [n] | Enable the base's automation policy; only individually opted-in stations run unless all on is set. Optional target crafts 1–20 |
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
4. Run **bstation station on** while standing by the kiln, and **bstation list**: the charcoal kiln must show opted-in=True, and all the other 25 stations must remain opted-in=False. **Do not run bstation all on** in this initial test.
5. **Before enabling collection**, designate one **unbound** Magic Chest as shared stock: use Enshrouded's Rename button and name it **crafting output**. Do not use a chest bound to Endless Chests or named Empty. Keep this output chest eligible for area deposits (quick stack).
6. Run **bstation sinks** and confirm that your renamed output chest is listed. This is read-only.
7. Run **bstation enable 1**. Compare the Magic Chest, kiln and your backpack after a scan. Only the base Magic Chest should lose valid inputs; nothing should pass through your backpack.
8. After one production cycle, confirm finished charcoal enters **crafting output**. Run **bstation stock** and confirm the charcoal appears in the base's crafting stock; use a blacksmith recipe or the **bstation plan 1** for another charcoal-consuming production station to check downstream availability. Run **bstation disable**.
9. v0.1.1 passed automatic fill/collect across 16 distinct station types in a 26-station base; the user accepted the shared-stock charcoal test. Verify additional cases (stopped station, full sink, restarts, other bases) as future regression checks.
10. Inspect kiln/logs/kiln.log for bounded BSMS messages. The v0.1.2 naming change was accepted in-game.

Disable prevents future actions, but cannot undo a previously submitted native transaction.

## Why crafting output is required

Endless Chests automatically **deletes anything added to a chest named Empty**, and some of your named chests are automatically refilled. The SDK's default Stations.CollectToBase chooses any deposit-enabled Magic Chest, including ones that another mod may manage. Therefore this mod deliberately does **not** call that generic collector.

Rename one or more **dedicated, unbound** Magic Chests to **crafting output**, optionally with any suffix (for example **CRAFTING OUTPUT - Smelter**, **craftingoutput2**, or **cra ft ing out put extras**). Matching ignores case and all whitespace anywhere. The old **BSMS Output** name is deliberately no longer recognized; rename existing output chests before installing v0.1.2. The mod resolves matching names **afresh at collection time**, only among Magic Chests in the same Flame Altar base which allow deposits. If a sink was renamed, removed, excluded from deposits, full or temporarily unavailable, it is skipped. The output stays at its production station if no safe sink can accept it. Name-reading capability `items.names` must be available; check **bstation status**.

The shared output chest remains a normal Magic Chest, so downstream native recipe payment and other Stations.Fill operations can use its contents. Do not manually bind crafting output chests in Endless Chests, or use an Endless Chests-managed chest as an output destination.

After a single kiln is accepted, **bstation all on** enables every supported production station in the current base, allowing finished products from any station to feed other stations and manual blacksmith crafting. The second base containing 25 production stations is configured and enabled separately. **bstation exclude** still overrides all mode; **bstation all off** returns to the individually opted-in stations.

## Settings — each production station

Settings file (per-world and persistent Flame Altar base):

\`kiln/data/base-station-magic-storage/base-station-magic-storage.json\`

**To keep finished products in the oven while other stations still collect:**

1. Enter the base and run \`bstation config sync\`. This adds a JSON entry for **each discovered station** without overwriting existing choices.
2. Open the settings file. Search for \`Oven\` in the base's \`stations\` array, and change **only** that station's \`"collect"\` to \`false\`.
3. Save the JSON, run \`bstation reload\`, and verify with \`bstation list\`. This setting survives game restarts.

Example entry (illustrative coordinates; use the generated key, do not copy the example's coordinates):

\`\`\`json
{
  "key": "T3_Oven_Factory@10.0,20.0,30.0",
  "enabled": true,
  "fill": true,
  "collect": false
}
\`\`\`

\`enabled: false\` stops both BSMS actions for that instance; \`fill: false\` disables only its automatic ingredient loading; \`collect: false\` leaves completed items at that station. These are **per-station permissions**, not independent activators: the base must still be enabled, the station opted in (or \`allStations: true\`), and global \`autoFill\`/\`autoCollect\` must be enabled for that respective operation. **With your current \`autoFill: false\`, manual production remains unchanged** while collection is controlled per station.

**Without editing JSON:** while standing by the oven (within 12 metres), run \`bstation station collect off\`; run \`bstation station collect on\` to reverse. \`bstation station off\` disables both actions for the nearest station, including in \`bstation all on\` mode. \`bstation config\` displays saved switches and the exact file path.

Existing v0.1.2 JSON files remain valid; missing station entries default to \`enabled=true, fill=true, collect=true\`, preserving previous behaviour. A new station can be added with another \`bstation config sync\`. A moved station gets a new key (template plus position rounded to 0.1 m); stale keys are retained to avoid unintended changes. Settings are written atomically after explicit commands. Invalid JSON or save failures disable automation rather than risk routing outputs incorrectly. A reload aborts subsequent actions from scans using the old configuration, after any already-started native transaction completes.


## Build

The candidate workflow uses the checksum-verified official Kiln v0.2.0-dev.29.14 SDK with **scripts/build-compat.sh BaseStationMagicStorage**. PR builds upload **base-station-magic-storage-0.1.3.zip** as a test artifact. The independent **base-station-magic-storage-v0.1.3** release tag and installable ZIP will be published only after in-game acceptance and merge. The scheduled latest-Kiln compatibility matrix also includes this mod.

Tracking: [issue #50](https://github.com/bisdat/kiln-mods/issues/50) and the [official Stations/Bases API](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/stations.md).
