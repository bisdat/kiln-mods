# Cross-base Storage — engine integration design

Status: research/design for an **unimplemented** native extension. Do not treat the proposed interfaces below as existing Kiln SDK APIs.

## Proven: inventory visibility (not native craftability)

In a live Enshrouded world on 2026-11-08, the pinned Kiln Items/Spawn/World services read 50 Magic Chest templates at two spatially separated bases, with 558 item types and 667,221 total item units. While at Base B (near 4908.7,799.5,4827.7), the scanner read 70,250 Yellow Luminescent Blocks from chest 1203 and 250 from chest 1302, both around 3385,938,3800 at Base A.

**Verified:** the engine's entity map and inventory API exposed both loaded chest clusters simultaneously, including exact item counts. **Not verified:** client crafting source enumeration, server eligibility, build resource deductions, persistent visibility after cold restart, or dedicated-server behavior. Current mod only reads inventories.

Magic storage template observed:
`Prop_Decoration_T6_Storage_72_Magic`; the `Magic` template suffix worked for this specific 72-slot type. Not a universal Magic Chest flag across upgrades and variants.

## Relevant pinned Kiln APIs

SDK source pinned at `frostdev-ops/kiln` commit `eba9a5e1e0e0044a9146fdd53021a751e2957030` (Kiln `v0.2.0-dev.15`).

| Public API | What it actually provides | Missing for requested goal |
|---|---|---|
| `Items.FindInventories(InventoryKinds.Containers)`, `Items.Read(chest)` | Read live storage, slots and item amounts on the game tick | Native source selection, eligibility, consistency during a craft |
| `Spawn.Inspect(chest)` | Template and position | Verified Magic Chest entitlement, base/altar ownership |
| `Gameplay.Recipe(recipeId)` | Recipe input/output items, station kind, required props, duration | Category input selection and client source availability |
| `Gameplay.WorkshopRecipes(workshopId)` | Native recipe IDs for a station kind; 0 = hand crafting | Enabled/disabled recipes based on unlocks, live UI sources |
| `CraftEvent` | Server pre-craft request (operator, station, recipe ID, count); can cancel | Cannot replace the actual ingredient inventory resolver or create a client request |
| `ItemCraftedEvent` | After core craft, outcome incl. MissingMaterials | Too late to change ingredients or guarantee remote item payment |
| `BuildEvent` | After server building/terraforming attempt | Too late for client preview/material sourcing or atomic server building payment |
| `Items.Give`, `Items.Take` | Game inventory transactions targeting one chest or player | Not a *single* atomic transaction across source chests, player, recipe and build event |

Key references (upstream):

- [`Kiln.Items` API](https://github.com/frostdev-ops/kiln/blob/v0.2.0-dev.15/docs/kiln/services/items.md)
- [`Kiln.Gameplay` API and event timing](https://github.com/frostdev-ops/kiln/blob/v0.2.0-dev.15/docs/kiln/services/gameplay.md)
- [Native crafting flow](https://github.com/frostdev-ops/kiln/blob/main/docs/re/gameplay.md) — `player_crafting` emits `CraftRecipeEvent` and `handle_crafting_events` invokes the game craft core. `CraftEvent` observes the request before that core runs.
- [Client crafting UI](https://github.com/frostdev-ops/kiln/blob/main/docs/re/creative-mode-146.md) — `UiCraftableAmount` can disable the craft button *before* the player sends the request. UI ingredient rows have separate counts; patching only the button would be an incomplete illusion.
- [Build material and payment research](https://github.com/frostdev-ops/kiln/blob/main/docs/re/building.md) — building costs depend on actual placed cells, not just a recipe's nominal `requiredCraftingIngredients` amount.

No game offsets from these research notes are hardcoded into the mod. They identify conceptual integration points only.

## Workbench recipe test exposes a second upstream API gap

At 21:27 UTC on 8 November 2026 the user ran `cbstore plan 0x23c98255 1` in Enshrouded with **50** visible Magic Chests. The game's hand-crafting registry returned a Workbench recipe with **2** inputs and **1** output:

- Input `String` / `0x18feb214`: **3** required; player **0**; Magic Chest pool **716** across chest **1412** (466) and **1081** (250). Exact-item deficit: **0**.
- Second input: `itemId=0`, raw `count=0`. **Undetermined** category/placeholder; raw zero does not establish zero cost.
- Output: **1 Workbench**. The calculation made **no inventory changes** and did not craft a Workbench.

The pinned [native Kiln.Gameplay recipe copier](https://github.com/frostdev-ops/kiln/blob/v0.2.0-dev.15/native/services/gameplay/recipes.cpp) extracts only 8 bytes (item ID at +0x10 and count at +0x14) from each **48-byte** native input record. The remaining field values, potentially needed for category material selection, are lost at the public API boundary. The managed `GameplayRecipe.Inputs` therefore cannot fully describe or price the second input. In the proposed upstream bridge, add a **version-safe read-only category input descriptor** and the authoritative ingredient resolution rule before trying to compute craftable amounts or consume remote resources.

The local **0.1.4** diagnostic changes the output from `CATEGORY ingredient x0` to `UNRESOLVED CATEGORY/PLACEHOLDER ingredient (itemId=0, rawCount=0)` and explicitly declines to infer the missing cost.

## ShroudForge `sf-crafting-essentials` 1.7.9 — source review (8 November 2026)

Reviewed the [original source at tag `1.7.9`](https://github.com/bonsaibauer/shroudforge/tree/1.7.9/mods/sf-crafting-essentials), particularly [`src/mod.lua`](https://github.com/bonsaibauer/shroudforge/blob/1.7.9/mods/sf-crafting-essentials/src/mod.lua), [`README.md`](https://github.com/bonsaibauer/shroudforge/blob/1.7.9/mods/sf-crafting-essentials/README.md), the [manifest](https://github.com/bonsaibauer/shroudforge/blob/1.7.9/mods/sf-crafting-essentials/extended.mod.json), and ShroudForge's detailed [native transaction investigation](https://github.com/bonsaibauer/shroudforge/blob/1.7.9/docs/evidence/runtime/2026-10-18-crafting-native-transactions.en.md).

### What their free-crafting implementation actually does

In **pregame** phase (requires the declared `patch` capability and restart), it reads typed `keen::RecipeRegistryResource` assets using `game.types.get` and `game.assets.get_resources_by_type`. For every recipe it sets the following existing data fields to **zero**:

```lua
recipe.input[j].itemStack.count = 0
recipe.input[j].inputItemCategory.count = 0
recipe.waterCharges = 0
```

The source writes each changed `resource.data` after preparing the batch. The mod's `extended.mod.json` declares `targets: ["client", "server"]` and `noResourceCost` as an **opt-in** setting (default `false`); the source's fallback `true` is not the setting's configured default when the manifest value is supplied. The mod also changes recipe duration and item-usage event flags for independent settings. Its README explicitly excludes **building, storage upgrades and other shared inventory costs** from free recipe costs, and says it uses typed resource edits rather than function-hook offsets. **This is not cross-base source selection.**

ShroudForge's native investigation traces `CraftRecipeEvent → handle_crafting_events → recipe transaction`. Its evidence states direct ingredient counts originate from `itemStack.count`, category counts from `inputItemCategory.count` (with category scaling / rounding), and water from `RecipeInfo.waterCharges`. Its October 2026 capture recorded zeroed costs across 2,536 recipes and the user reported a recipe worked without ingredients. These are **ShroudForge's reported validation data**, not reproduced in our Kiln environment.

### Direct link to our Workbench anomaly

Our `cbstore plan 0x23c98255 1` reported **String x3** and a second native input with `itemId=0, rawCount=0`. ShroudForge's source gives a **specific plausible reason** for the unexplained zero: category-based requirements are recorded in a **distinct `inputItemCategory.count` field**, while the pinned Kiln.Gameplay recipe copier exports only `item` and `count` from each 48-byte input record. `itemId=0, count=0` is therefore **not enough to infer zero category cost**. We have not confirmed that this specific Workbench input is in fact a category input versus another placeholder; the missing typed fields must be read before concluding.

**Actionable upstream Kiln enhancement:** extend `Gameplay.Recipe` with a version-safe descriptor per native ingredient: explicit direct item ID/count, category ID/count (and resolution/scaling rules where applicable), plus water charges. Inspect `keen::RecipeRegistryResource.recipes[].input[].itemStack`, `inputItemCategory` and `waterCharges` as the authoritative reflected model. Preserve existing `GameplayRecipe.Inputs` compatibility if possible; make unresolved fields explicit, not zero-cost defaults. Test on known recipes including the Workbench's second input.

### Why this does not yet solve cross-base crafting

**Zeroing costs changes the recipe's meaning.** The normal game does not consume resources from any chest when the recipe requirements are zero. It may also change UI requirement counts, automated station input filling and cancellation refunds. This cannot implement the requested “materials stored at Base A pay for craft/build at Base B” behavior. Trying to let the game do free crafting then separately `Items.Take` remote stock **after** `ItemCraftedEvent` would be non-atomic and could produce items without paying, fail during server crash, bypass category choices and leave building costs untouched.

The ShroudForge transaction report independently warns that the original inventory transaction's native journal and ECS writable context are borrowed engine internals; manipulating them separately from Lua is not a safe substitute for one authoritative source-selection and consumption operation.

**Best next engineering target:** keep the existing recipe costs intact, add Kiln's missing reflected ingredient/category metadata as a safe read-only API, then extend the game's **client material-source resolver** and the **host's original transaction** so cross-base Magic Chests participate as genuine sources. This is an upstream Kiln engine/API enhancement, not a change to the existing managed read-only scanner. Do not port ShroudForge's free-crafting cost-zeroing behavior as the cross-base solution.

## Prototyped Kiln SDK recipe-input inspection extension (8 November 2026)

The next step has a source-level implementation in [`sdk-proposals/recipe-input-probe`](../sdk-proposals/recipe-input-probe/README.md), based on the pinned `frostdev-ops/kiln` revision. These scripts patch a **disposable source checkout** (not the user's installed game or Kiln runtime) and add:

- A **new optional tail** on the `kiln.gameplay` v1 native table, checked by its size. The `recipe_raw_inputs` entry copies at most eight **48-byte native RecipeInfo input records** through Kiln's existing SEH-protected lookup and simulation-thread guard into a **400-byte** C ABI value. The existing `Gameplay.Recipe` and public table entries remain unchanged.
- A managed `Gameplay.RawRecipe(id)` API that returns detached `ReadOnlyMemory<byte>` inputs; it fails closed if the matched native DLL lacks the optional function.
- [Isolated GitHub Actions checks](https://github.com/bisdat/kiln-mods/actions/workflows/kiln-recipe-probe.yml): **Linux managed build, C ABI size check and Windows x64 `kiln-gameplay.dll` build passed** on 8 November 2026. [Successful run](https://github.com/bisdat/kiln-mods/actions/runs/42527820064).

The tool only exports *opaque bytes* at this stage. This **does not interpret a category ID, the category quantity or water charges**; its purpose is to avoid guessing unverified native offsets. The raw record could contain internal pointers, so it should not be published in a diagnostic log or exposed over a network. Do **not** deploy either custom DLL to a normal Kiln installation or add the experimental API to the shipping Cross-base Storage mod. No version bump from `0.1.4` is warranted.

For a production-ready upstream change, first validate the exact native category input layout for the known game build against the authoritative `RecipeRegistryResource` fields and replace opaque bytes with **typed**, validated descriptors. Following that, the client source resolver and atomic server material payment still need their own native implementation and end-to-end tests.

## Proposed contract for future upstream service (API sketch, not executable)

A future optional Kiln native service might expose:

1. **Magic chest registry:** the game's actual authoritative Magic Chest flag and base membership, with stable identity across saves and chunk changes.
2. **Material-source enumeration:** when a player opens crafting/building UI, compose the game's normal local player/local magic sources with eligible loaded remote Magic Chests, respecting mode/permissions. A server-side validated snapshot or revision must accompany client-visible counts.
3. **Pay from selected sources inside the original game transaction:** normal crafting/building needs to know which *real* inventories participate in one operation, including categories and specific chosen item stacks. If the world changes between quote and payment, fail/retry without producing output. Don't separately `Take` and then `Give` a crafted item or rearrange copies into the backpack.
4. **Client UI:** display combined eligible resource counts in recipe rows, craft buttons and building material previews, and send the ordinary action request; no server-only mod can activate a craft button the client has disabled. Any custom networking must validate on the server.
5. **Station support:** hand crafting, NPC and crafting benches, automated factory queues, flame altars, and build/terraform actions may use different material paths. Implement and test independently.

Example *logical* ordering, not a public Kiln API:

```text
player's normal inventory
  → local Magic Chests that the vanilla game already allows
  → other eligible Magic Chests in the same world, nearest / stable entity order
  → if unavailable: reject, without creating output
```

The resolver should avoid double-counting a Magic Chest already present in the game's default local sources. It should exclude ordinary chests and should not count item entity IDs from stale saves.

### Non-goals / unsafe shortcuts

- **Do not** copy all distant ingredients into the backpack each tick to fool the UI.
- **Do not** inject crafted outputs while independently taking remote stock; this bypasses vanilla unlocks, station constraints, output capacity and single-transaction guarantees.
- **Do not** implement the native source resolver with hardcoded executable addresses in the Kiln managed mod.
- **Do not** claim `cbstore plan`'s sum is craftable inventory. Category input choices, native GUI restrictions, and game permissions still govern craftability.
- **Do not** assume distant chests are permanently loaded; read their current state at the moment of payment. Investigate lifecycle under cold restarts, dedicated servers and chunk streaming.
- **Do not** run deduction tests while Endless Chests is refilling the source containers; auto-refill would obscure whether remote payment actually happened. Disable its auto refill for the test or use unbound Magic Chests.

## Observed missing craft requests and client gating (8 November 2026)

Live test of the native event diagnostic prototype (then versioned `0.1.0-alpha.2`) at 21:14:39 UTC showed:
- `cbstore status`: `gameplay=True`, `craftEventHook=True`, `buildEventHook=True`.
- `cbstore lastcraft`: no recorded craft request and no native crafting outcome.
- `cbstore plan last`: no recorded native request, so no recipe ID was available; the earlier version returned a command error.
- `cbstore lastbuild`: no observed native build outcome.

This is **consistent with** a client-side crafting button being disabled before sending a request; it is **not conclusive proof** that the client's material-source resolver alone blocked crafting. Recipe unlocks, station state, missing local materials and other mod state weren't isolated in that test. The client crafting research describes this as a possible gate, but diagnosing why requires a native client trace or comparing with a normal craft with all inputs in the player backpack.

**0.1.4 diagnostic improvement:** `cbstore plan last` now returns a non-error explanatory message if no request exists. `cbstore recipes [output-filter]` can enumerate hand-crafting recipe IDs without performing a craft, and `cbstore plan <recipeId> [count]` can compare inputs against Magic Chest stock without relying on a craft request. This is read-only and cannot activate a disabled game UI control.

## Reproducible read-only tests using current 0.1.4

In Enshrouded, enter a world, and first try the **zero-crafting** recipe discovery path. This works even when the client craft button is disabled:

```bash
printf 'cbstore recipes\ncbstore lastcraft\ncbstore plan last\ncbstore lastbuild\n' > kiln/control/console/cbbridge.tmp
mv kiln/control/console/cbbridge.tmp kiln/control/console/cbbridge.cmd
cat kiln/control/console/cbbridge.result.json
```

`lastcraft` reports a craft request and its outcome **only if the engine received one**. If it recorded none, `plan last` explains why the ID is unavailable rather than failing. Select a hand-crafting recipe ID shown by `recipes` and run `cbstore plan 0xRECIPEID` as another console command to inspect exact native inputs and aggregated loaded Magic Chest supplies without crafting. `lastbuild` reports only native build attempts seen in the same world session; client-side rejections may not emit events. Use `cbstore recipes <output-name>` to find available hand crafting IDs.

All these operations are **read-only**. If tests show a client never submits a craft/build request when remote stock is the only stock, that's evidence the client source resolver must change, not a problem with server inventory scanning.

## Feature acceptance criteria (future)

1. With **empty player backpack** and **empty local Magic Chest**, 100 Wood Logs in an ordinary base's distant Magic Chest should show as available in the normal Base B crafting/building UI.
2. A normal craft consuming 5 Wood Logs succeeds **once**, produces the game's normal output, and leaves 95 Wood Logs in the distant source chest (and none magically copied elsewhere).
3. Repeat with category ingredient selection (e.g. "any wood"), insufficient quantities and multiple source chests (local first).
4. An actual building placement consumes the engine-computed block cost from the remote source, or refuses correctly with no block changes and no stock loss.
5. Simultaneous players cannot spend the same source stack twice.
6. Repeat after disconnect/reconnect, server world save/restart, long-distance travel and chunk streaming.
7. Repeat with the relevant Kiln runtime and both client-side/Kiln host parts installed; fail closed if mismatched.

As of 0.1.4, **none of these actual native craft/build acceptance criteria has been met**.
