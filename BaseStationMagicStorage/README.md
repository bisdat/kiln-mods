# Base Station Magic Storage — v0.1.4 output modes (test candidate)

Standalone Kiln mod for production stations (FactoryStation: charcoal kilns, smelters, looms, grinders, seed beds and others) within a Flame Altar base. This candidate requires Kiln **0.2.0-dev.29.17 / mod API 0.2.9** (upstream [issue #68](https://github.com/frostdev-ops/kiln/issues/68)). It retains the 26 station-type switches accepted in-game in v0.1.3.

## Output modes: Kiln Insert menu

Open **Insert → Mods → Configs → Base Station Magic Storage**. There are **two independent checkboxes**, above the 26 station-type checkboxes:

- **Share outputs** (`ShareOutputs`, default **ON**): finished products remain *in the producing station* and become available to same-base player crafting and `Stations.Fill`. Upstream Kiln only lends eligible finished outputs, not inputs, leftovers, or products that are also inputs. Requires `stations.output_sources` for the opt-in; `stations.craft_sources` and `stations.craft_ui` govern server player crafting and Kiln-client menu counts. **In-place sharing is global:** no per-base enablement, individual opt-in or `bstation` command is needed. The 26 type switches still apply. Legacy per-base policies govern only optional automatic fill/collection.
- **Collect outputs** (`CollectOutputs`, default **OFF**): retains v0.1.3 automatic transfer to explicitly named, deposit-enabled Magic Chests beginning with `crafting output`. Also respects the existing per-base `bstation collect on|off` policy.

Both can be ON independently. **With both ON, the collector may empty the station's shared output into its named chest during the next scan.** For output to remain in producing stations, enable **Share outputs** and disable **Collect outputs**, then click **Apply**. Fresh configuration defaults to sharing without collection. Existing saved checkbox values remain authoritative, so if you tested an earlier v0.1.4 candidate, explicitly set **Share outputs ON / Collect outputs OFF** in the Insert menu.

Sharing is base-local, not cross-base. It works even when BSMS automatic filling is OFF and needs no output chest. Kiln dev.29.19 beta 2+ introduces native station-screen material counts and Fill controls for shared outputs; **this remains subject to in-game acceptance testing**. Earlier Kiln releases may still show greyed-out native Fill controls. Clients without Kiln retain vanilla crafting counts. Test with Creative Mode off, and check `bstation status`.

BSMS records which output-source opt-ins it owns by world and stable station identity, so switching sharing or a station type OFF revokes BSMS-owned sources on the next five-second scan, without switching off another mod's registrations. Native craft payment is revalidated on the server. ****In-game FAIL reported:** the smelter did not recognise newly crafted charcoal still held at the charcoal kiln with ShareOutputs enabled. Upstream documents that the native station Fill button cannot count station outputs (while mod-driven Stations.Fill can). This is unresolved; do not merge/tag this candidate until the native-UI acceptance gap is addressed and the output-source registration is verified.**

## New-world defaults

| Setting | Default |
| --- | --- |
| Share outputs | **ON** across all bases |
| Collect outputs into named Magic Chests | **OFF** |
| 26 globally supported station types | **ON** |
| Per-base activation to share | **Not required** |
| Automatic filling | **OFF** |
| Automatic collection | **OFF** |

Finished outputs become available as in-place crafting sources automatically, without `bstation enable`, `bstation all on`, or a saved base policy. The upstream Kiln API continues to enforce same-base membership, authorisation and transaction safety. Existing global checkbox values remain authoritative. Existing per-base saved policies affect only the separately optional fill/collection automation, never sharing.

## Behaviour

**Base automation default OFF.** In-place sharing is ON across all bases automatically. Auto-fill and auto-collection remain OFF until deliberately enabled for a base. Once you enable optional automation, the mod can:
- Tops up the station's **currently selected** recipe to 3 complete crafts (configurable 1–20) through the game's Stations.Fill transaction. Inputs come **only from this base's Magic Chests**, not the player backpack. Water uses the base's water sources.
- When **Collect outputs** is enabled, moves finished outputs through native Stations.Collect into **explicitly marked, deposit-enabled Magic Chests** named **crafting output**, optionally followed by any suffix. Matching ignores case and whitespace anywhere in the name. Those chests are then part of the base's normal crafting stock, making charcoal and other outputs immediately available to other production stations and to the blacksmith's native base-stock crafting. It leaves leftovers, inputs and ordinary chests alone.
- **No output destination is selected automatically.** If there is no suitably named Magic Chest, or its name cannot be read reliably, finished products stay safely in the producing station. This prevents accidental deposits into Endless Chests named **Empty**, which are deliberately cleared by that mod.
- Does not change recipe, select options, start/stop states or user decisions. Stopped, stop-pending and no-recipe stations are not auto-filled.
- **26 type-level checkboxes:** each switch controls **every instance** of a station type in every base, including which stations can share outputs. Turning **Oven** OFF disables BSMS fill and collection at all ovens: cooked food stays at the ovens, with ordinary manual crafting unchanged. Turning it ON resumes the base's chosen automation. All default ON to preserve v0.1.2 behaviour. Legacy per-base opt-ins and exclusions still govern automatic fill/collection, **not in-place output sharing**. Each scan is serialized (up to 128 stations) and repeated errors are throttled.
- Fails closed when two configured altar policies merge into one base, or when its settings file is invalid.

**This is base-local, not cross-base.** The API works on a station's own Flame Altar base. Even when Building Tools shows chests from another base, that is not part of Kiln.Bases stock.

FactoryStations are not ordinary hand-crafting workbenches or NPC crafting (which already use the game's base-stock path), and not the Gem Forge (see upstream [Kiln #61](https://github.com/frostdev-ops/kiln/issues/61)). No borrowed inventory, external loader, game-memory patches or custom UI.


## Smelter acceptance investigation (unresolved)

The reported test is **FAIL**: with Share outputs ON, charcoal was produced in a charcoal kiln, but the smelter did not recognise it as material. There are two separate checks:

1. `bstation list` (in the same base) must show the **charcoal kiln** as `output-source=True` with at least one output stack/item. If not, review `bstation status`, global Share outputs, Charcoal Kiln type switch and `stations.output_sources` capability. Base enablement and `all on` do **not** govern sharing.
2. Stand near the **smelter**, select the desired recipe, and run `bstation plan 1` (read-only). This now prints `shared station outputs=<quantity>` per ingredient. If nonzero and affordable while the native smelter UI stays greyed out, the remaining gap is **Kiln's client-side Fill button**, documented upstream in [stations.md](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/stations.md#station-outputs-as-crafting-stock). This is not an accepted replacement for normal station operation.

The smelter's *automatic production cycle* still uses its own inventory; sharing does not make a source station produce from thin air. A recipient station must first receive inputs via `Stations.Fill` or the native fill action. Existing BSMS `bstation fill on` enables its optional automatic `Stations.Fill`, but is not a substitute for fixing the native Fill interface. Preserve queued inputs and never move station outputs to arbitrary Magic Chests.

## In-game settings: one switch per production-station type

Open the game's **Kiln Mods menu** (Insert or Mods in the game menu), then open **Configs → Base Station Magic Storage**. The 26 station types are individual checkboxes, exactly like the Survival QoL options. Click **Apply** to save changes. A checked box (ON) permits BSMS automation for **all instances** of that type. An unchecked box (OFF) prevents automatic filling **and** output collection for that entire type, everywhere in the world. Manual vanilla production continues normally.

**Example:** uncheck **Oven** and Apply. All ovens retain finished food, while enabled smelters, charcoal kilns and other types can continue sending finished items to the named Magic Chest `crafting output`.

Settings are global across bases. They are stored by Kiln's config system in its active profile's `configDir`, in `base-station-magic-storage.json`, **not** the per-world/base policy JSON under the mod's data folder. Saved base policies remain relevant to **optional automation only**. The global station-type switch controls output sharing independently of any base-enabled flag; its OFF state excludes all instances of that type. Automatic fill and collection default OFF. All 26 switches default ON, so upgrading does not unexpectedly change collections. Unknown future station templates are not automated until explicitly supported and exposed in the menu.

**Name audit (10 corrections).** Names were checked against the [Official Enshrouded Wiki's production-station list](https://enshrouded.wiki.gg/wiki/Category:Production_Places) and [crafting equipment](https://enshrouded.wiki.gg/wiki/Crafting). Kiln's [client game catalogue](https://github.com/bisdat/kiln/blob/main/docs/kiln/modding/catalogs.md) provides authoritative English workshop names when generated from the user's installed game; the local catalogue is not stored in GitHub. The other 16 station types already have matching player-facing names.

| Internal game template (unchanged) | Old BSMS menu label | Updated game name | Config property migration |
|---|---|---|---|
| `T5_cardingMachine_Factory` | Carding Machine | **Spinning Machine** | `CardingMachine` → `SpinningMachine` |
| `T6_coffeemaker_brewer_Factory` | Coffee Brewer | **Coffee Brewery** | `CoffeeBrewer` → `CoffeeBrewery` |
| `T1_Seedling_Factory` | Seedling Station | **Seedbed** | `SeedlingStation` → `Seedbed` |
| `T6_Improved_Grinder_Factory` | Improved Grinder | **Mill** | `ImprovedGrinder` → `Mill` |
| `T1_HandGrinder_Factory` | Hand Grinder | **Grinding Stones** | `HandGrinder` → `GrindingStones` |
| `T4_Honey_Factory` | Honey Station | **Beehive** | `HoneyStation` → `Beehive` |
| `T7_Infusion_Station_Factory` | Infusion Station | **Infusion Altar** | `InfusionStation` → `InfusionAltar` |
| `T2_Fireplace_Factory` | Fireplace | **Cooking Station** | `Fireplace` → `CookingStation` |
| `T6_coffemaker_roaster_Factory` | Coffee Roaster | **Coffee Drum Roaster** | `CoffeeRoaster` → `CoffeeDrumRoaster` |
| `T6_Medieval_Grill_Factory` | Medieval Grill | **Seafood Grill** | `MedievalGrill` → `SeafoodGrill` |

**Preserving your choices:** BSMS reads the existing Kiln station-type config before registering the new properties. Where an old key exists and a new key does not, its saved boolean is migrated with `ConfigFile.TrySet`. Existing new-key values always win. The other 16 switches (including **Oven**) are unchanged. If the migration cannot read or persist a saved setting, BSMS disables automation rather than silently resetting that choice. Obsolete old keys might remain marked unknown by Kiln's JSONC writer, but they are no longer consulted.

**Production station options:**

- Alchemy Station (`AlchemyStation`)
- Kiln (`Kiln`)
- Spinning Machine (`SpinningMachine`)
- Blast Furnace (`BlastFurnace`)
- Coffee Brewery (`CoffeeBrewery`)
- Seedbed (`Seedbed`)
- Mill (`Mill`)
- Drying Rack (`DryingRack`)
- Forge (`Forge`)
- Oven (`Oven`)
- Smelter (`Smelter`)
- Table Saw (`TableSaw`)
- Grinding Stones (`GrindingStones`)
- Spinning Wheel (`SpinningWheel`)
- Loom (`Loom`)
- Hand Spindle (`HandSpindle`)
- Beehive (`Beehive`)
- Laboratory (`Laboratory`)
- Infusion Altar (`InfusionAltar`)
- Cooking Station (`CookingStation`)
- Coloring Station (`ColoringStation`)
- Coffee Drum Roaster (`CoffeeDrumRoaster`)
- Ectoplasm Press (`EctoplasmPress`)
- Charcoal Kiln (`CharcoalKiln`)
- Tanning Station (`TanningStation`)
- Seafood Grill (`SeafoodGrill`)

## Commands (admin only)

| Command | Purpose |
|---|---|
| bstation status | Read-only capability, world, configuration and enabled-base status |
| bstation bases | Read-only all-base inventory and policy overview |
| bstation list | Read-only stations in the base where you stand (up to 64), including `type-enabled` from Kiln Configs |
| bstation sinks | Read-only eligible Magic Chest output destinations in this base |
| bstation stock | Read-only current Magic Chest stock of the items produced by the base's selected station recipes |
| bstation plan [n] | **Read-only** fill plan for nearest production station within 12 metres |
| bstation station on / station off | Include/exclude nearest station from individual opt-in mode (12 m). Does not enable the base |
| bstation all on / all off | Deliberately enable automation for every station in this base, or return to individual opt-in |
| bstation enable [n] | Enable the base's automation policy; only individually opted-in stations run unless all on is set. Optional target crafts 1–20 |
| bstation disable | Disable this base's automation |
| bstation target n | Set target complete crafts, 1–20 |
| bstation fill on / off | Control auto-filling independently |
| bstation collect on / off | Control auto-collection independently |
| bstation exclude / include | Exclude or restore the nearest production station |

Commands work in in-game admin chat. The Kiln atomic file console works in a hosted solo world with exactly one active player.

## Legacy automatic fill/collect acceptance test (not required for sharing)

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

**Global output sharing now works without `bstation all on`.** For the optional legacy automatic filling/collection workflow only, `bstation all on` selects all supported stations in the current base. The second base containing 25 production stations is configured and enabled separately. **bstation exclude** still overrides all mode; **bstation all off** returns to the individually opted-in stations.

## Legacy per-base settings (optional automatic fill/collection only)

Kiln stores world + persistent Flame Altar GUID policies in its per-mod data folder:
kiln/data/base-station-magic-storage/base-station-magic-storage.json

This is separate from the 26 global switches in Kiln Configs. **It has no effect on in-place output sharing.** A file is only written after an explicit setting command. Existing policies remain for optional auto-fill/collection; new worlds need no per-base commands for sharing. It is written through a temporary file; parse or write errors disable automation. Station exclusions use template and world position rounded to decimetres; if you move a station, exclude it again.


## Build and test

The candidate workflow builds with checksum-verified official Kiln v0.2.0-dev.29.14 SDK via `scripts/build-compat.sh BaseStationMagicStorage`. The PR builds the installable `base-station-magic-storage-0.1.3` artifact; after acceptance and merge to `main`, the release workflow publishes tag `base-station-magic-storage-v0.1.3` with the installable ZIP and SDK provenance.

Acceptance confirmed in-game: 26 type-level checkboxes operate correctly, including ON/OFF, and the corrected station labels are displayed. With `fill=False`, output sharing or collection continues according to the two global checkboxes. No per-instance station selection or JSON editing is needed.

Tracking: [draft PR #52](https://github.com/bisdat/kiln-mods/pull/52), [original issue #50](https://github.com/bisdat/kiln-mods/issues/50), and [official Kiln Stations/Bases API](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/stations.md).
