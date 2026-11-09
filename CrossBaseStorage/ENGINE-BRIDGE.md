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

## Native craft/payment and building integration clues in newer Kiln `main` (8 November 2026)

While investigating this task, the newer upstream [`Kiln.Rules` creative hook implementation](https://github.com/frostdev-ops/kiln/blob/77e525888ac0d5f4f6b16103e945ed7ac049b50e/native/services/rules/creative.cpp) (main commit `77e525888ac0d5f4f6b16103e945ed7ac049b50e`) was examined. **This is not part of the pinned `v0.2.0-dev.15` SDK used by Cross-base Storage 0.1.4**, and the branch may change.

The upstream source already demonstrates two relevant low-level seams:

1. **Client craftability gate.** It wraps `UiCraftableAmount(gameUi, recipeId, cap, selections)`, first calls the game's original computation and for `FREE_CRAFTING` returns at least `cap`. For cross-base storage, a valid extension would need to compute actual available *eligible* remote stock (including category choices, water, unlocking and output constraints) rather than simply forcing `cap`. The UI's separate ingredient-row counts must also reflect the same stock.
2. **Original server recipe payer.** It hooks an alternative recipe payment function with a signature conceptually like `pay(tx, costs, owner, multiplier, extra)`, validates the recipe-specific call site, and when `FREE_CRAFTING` is enabled returns success `0` without adding a charge. This is a **transaction entry point**, not merely a post-craft notification. Cross-base storage could potentially extend **the original source inventory set / payment traversal** at this point so the normal transaction journals actual withdrawals. However, returning `0` (the existing free mode) is **not** a cross-base implementation: it deliberately waives payment. No verified way to enrol remote chests in this transaction exists yet.
3. **Building material payment.** The pinned [`Kiln.Rules` native implementation](https://github.com/frostdev-ops/kiln/blob/v0.2.0-dev.15/native/services/rules/rules.cpp) already hooks `ApplyMaterials` on the authoritative server and alters a *copy* of the material cost list for its `BuildCost` multiplier. It also has a client build-cursor missing-material waiver for `BuildCost=0`. A future bridge needs source inclusion and proper debit, not cost scaling or merely clearing UI warnings.

The newly identified payment hooks **make a transaction-aligned solution more plausible** than zeroing `RecipeRegistryResource` costs or calling `Items.Take` after crafting. They do **not** prove that remote Magic Chests can safely be attached to the borrowed transaction, that the original payer supports these sources, or that the same hook covers category inputs/automated stations. Modifying these hooks requires upstream native code, matching server and client build symbols, strict thread/lifetime checks and independent rollback/concurrency tests.

**Proposed next code investigation:** trace the genuine native `InventoryPay` and `RecipePayAlt` source enumerator and the `ApplyMaterials` building payer in an exact supported executable; determine where the engine collects eligible chest inventory IDs before it mutates the payment journal. Only add a remote-source provider **at that point** with explicit Magic Chest authority, per-world eligibility, one-transaction debits and fail-closed fallback. Do not use the existing `FREE_CRAFTING` flag or an unconditional craftable amount to simulate success.

## Read-only exact-item source allocation prototype (Cross-base Storage 0.1.5)

A separate pure [`MaterialQuotePlanner.cs`](MaterialQuotePlanner.cs) proposes **one exact item's** withdrawal across a candidate source set. It never touches a game transaction or inventory, and runs independently of Kiln so its logic can be tested by [`tests/material-quote`](../tests/material-quote/Program.cs). The server-side admin `cbstore quote <units> <exact item>` command currently feeds it a snapshot of player stock and all loaded template-matched Magic Chests. This **does not** claim base membership or native source eligibility.

Current proposed ranking is: (1) player's own inventory, (2) other candidate chests by increasing 3D distance from player, (3) stable entity ID to break ties. This is a **fallback prototype**, not the engine's local-base-first policy; the game may choose a different source order for its normal local magic access. The production bridge must use an **authoritative Flame Altar/base membership and Magic Chest flag** before computing true local/remote precedence.

Fail-closed quote rules: duplicate source entity IDs, negative quantities or invalid distances reject the proposal; total shortage yields no partial allocations; a satisfied quote allocates *exactly* the requested count and never invents stock; inputs are immutable snapshots; input order does not affect the deterministic result. Unit tests model the live Workbench ingredient (need **3 String**, player **0**, chest **1412** contains **466**, chest **1081** contains **250**), a 500-String split, shortage 717>716, duplicate IDs, source order and overflow-safe totals. **No item is removed**, and no transaction reservation is claimed.

The native payment research still controls the next step. Before a gameplay-enabled release, a source quote must be validated **inside the original game payment transaction** and applied only through original engine journalled mutations with the same craft/build success/rollback semantics. A free-crafting payer return of `0` followed by an independent `Items.Take` is **not** an atomic substitute. Multiparty concurrent crafting, server reloads, categories and water costs require separately verified behavior.

The additional safety test `python3 scripts/validate-cross-base.py` still disallows all inventory mutations in the shipping mod. The CI material quote test can run on Linux with .NET 10; no Windows native patch is installed or used by this version.

## Engine inventory-link traversal: another potential payment seam

Kiln's original [`docs/re/items.md`, sections 4 and 7](https://github.com/frostdev-ops/kiln/blob/77e525888ac0d5f4f6b16103e945ed7ac049b50e/docs/re/items.md) and the [`kiln.items` C ABI](https://github.com/frostdev-ops/kiln/blob/77e525888ac0d5f4f6b16103e945ed7ac049b50e/native/core/abi/services/kiln_items.h) document the game's existing `InventorySetup` graph. An owner has at most 16 primary links (`linksEntities[16]` and `linksCategories[16]`), where each linked `Inventory` has eight slots, and category `Virtual` can expand nested links. The regular `InventoryRemoveItems` implementation first considers the player's held slot and then traverses inventory links, including virtual links. It queues journal entries per underlying inventory slot; **committing** only on a fully satisfied request is a higher-level responsibility. A storage chest can own many linked inventory entities (e.g. a 72-slot chest), so its owner ID is **not the same** as a single source slot ID.

**Implication for Cross-base Storage:** We should investigate whether the game's native **recipe and building payment consumers** can accept a *transaction-scoped* set of additional eligible inventory owners/slots and let the **existing link traversal and journalled debit** handle source updates. A potential native provider would map each candidate chest owner to its actual underlying linked slot IDs, filter the authoritative Magic Chest/base eligibility, and deduplicate slots before creating the transaction. It must avoid the 16-link bound and preserve the original player inventory links.

**Important constraints:** These observations do **not** authorize writing into a player's persistent/replicated `InventorySetup` to append remote chest links. Such a change may affect equipment, loot, other item actions and saves, violate the fixed 16-link capacity, or persist outside a craft. A safe implementation must be scoped to the actual original payment transaction and prove that its mutable ECS accessors and rollback journal cover the additional chest owners. The current `cbstore quote` represents **owner-level candidate amounts only**, not authoritative per-slot debits.

## New testable native craft/UI payment trace (9 November 2026)

The next engineering increment has been implemented in a **disposable upstream-source experiment** at [`sdk-proposals/native-craft-trace`](../sdk-proposals/native-craft-trace/README.md). It targets exactly Kiln `main` commit `77e525888ac0d5f4f6b16103e945ed7ac049b50e`, which has the newer `rules.craft` payment hooks not present in the older game-tested Cross-base Storage SDK. **The shipping Cross-base Storage `0.1.5` mod has not been modified.**

To avoid conflicting game-function detours, this experiment **extends the existing `kiln.rules` hooks** rather than installing a second independent hook. It adds a passive ABI tail `craft_trace` and optional managed `Rules.TraceCraftPayments()`. It records:

- **Client:** `UiCraftableAmount` recipe ID, original vanilla craftable amount and requested cap; no forced UI availability.
- **Server:** original primary `InventoryPay` and alternate `RecipePayAlt` payment calls and engine return codes, only when the original validated recipe-specific callsite matches; nothing is charged or waived by the trace.
- **Safety:** no engine pointers, source slot IDs, ingredient lists, reservations, new detours or item mutations. The last-sample fields are scalar, process-local, and not transaction-correlated across workers. An older Kiln runtime returns `null` for this optional managed API.

This closes an **instrumentation gap**, not the material-source gap: an active client gate and a successful server payer are necessary but insufficient to prove remote Magic Chest consumption. The current native payment hooks waive costs in free-crafting mode; they do **not** expose a safe way to supply additional inventories to the original payment journal. The latter requires independent reverse-engineering and an API designed around one transaction's immutable validated source set and exact debit semantics. Merely adding `Items.Take` after the payer succeeds is never acceptable.

The separate CI workflow compiles the managed wrapper and Windows x64 native `kiln-rules.dll` against the pinned source, without publishing a custom game-installable binary. The upstream `main` revision contains a baseline MSVC integer-to-integer `reinterpret_cast` issue in its `sym<F>` helpers; its Windows CI job applies an **isolated compiler-compatibility adaptation after the feature-only source diff has been checked**, without changing game hooks or the feature proposal.

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

As of 0.1.5, **none of these actual native craft/build acceptance criteria has been met**.


## Follow-up native journal source probe — 9 November 2026

Following the successful live `cbstore quote 500 String` (466 proposed
from chest 1412 and 34 from chest 1081, with no item mutation), the
isolated [native-craft-trace proposal](../sdk-proposals/native-craft-trace/README.md)
now includes a second **read-only, bounded journal observation** stage.

The native `journal.py` extension observes the *original* primary or
alternative recipe payer, captures the engine-owned transaction's op-count
delta, and samples up to eight distinct **underlying inventory entity IDs**
and their staged take quantities. The observer retains no pointers and never
modifies payments, game source eligibility, inventory links or the client UI.
Its resolver requires exact `items/tx.*` layout constants from the supported
upstream revision; unknown layouts disable the probe. It is *not* part of the
released/installed server-side mod.

[GitHub Actions native + managed test](https://github.com/bisdat/kiln-mods/actions/runs/42572154780)
passed on 9 November 2026 (Linux ABI/managed and Windows x64 native compilation;
previous no-mutation checks retained). **Neither native staging nor actual
gameplay is verified at runtime.** Journal entries are tentative operations:
later validation, commit or rollback can change the result. They do not
establish that a remote Magic Chest was a valid source.

**New research dependency:** one Magic Chest owner with 72 slots can reference
multiple eight-slot inventory entities through `InventorySetup`. Therefore
observing the journal's inventory entity IDs is insufficient to identify the
container owner, its Magic property, Flame Altar membership, permissions or
base priority. A separate, authoritative owner-to-link resolver is required
before comparing observed vanilla source selection to proposed remote sources.
The correct production hook must also match UI source counts and preserve the
host game's same-transaction journalled debit; do not graft temporary links
into the player's persistent `InventorySetup` or infer a commit from a
staged payment observation.

## Supported owner-link lookup now implemented in v0.1.6 (9 November 2026)

**Upstream SDK breakthrough:** pinned Kiln.Items `v0.2.0-dev.15`
already returns `InventoryContents.Inventories` from `Items.Read(chest)`,
with the tuple `(Inventory: EntityId, Category: InventoryCategory)` for
each linked underlying eight-slot inventory. The native C API's
`KilnItemsInventory.Inventories[32]` and `Categories[32]` are copied
by the managed `Items.Read` implementation. No invented native
offsets, custom DLL, or new SDK API are needed to observe *loaded*
chest owner-to-link relationships.

Cross-base Storage **0.1.6** adds two admin-only, read-only diagnostics
`cbstore links <owner>` and `cbstore resolve <linked inventory>`.
The pure `InventoryLinkIndex` fails closed on malformed owner IDs,
duplicate link IDs within an owner, invalid link counts and duplicate
owners. Shared links across owners report `Ambiguous`; nonmatching
IDs report `NotFound` (which may simply mean an unscanned player,
station or other owner). The `magicByTemplate` flag is still the
**heuristic** name match. The mapping is a *current loaded-world
observation*, not stable across reloads or authority to craft.

The new twelve-case independent test harness is in
[`tests/inventory-links`](../tests/inventory-links/Program.cs), alongside
the existing ten quote-planner tests. Initial [v0.1.6
build](https://github.com/bisdat/kiln-mods/actions/runs/42573891495)
passed read-only validation, **22 pure-logic tests**, pinned SDK build
(0 compiler warnings/errors) and package generation. The linked-owner commands were subsequently validated in-game: `cbstore links 1412` returned nine real 8-slot links, and `cbstore resolve 1421` returned `Matched` to owner 1412 at link index 4; 52 scanned storage candidates, 464 linked inventories, zero ambiguous links (9 November 2026, 06:46:58 UTC). This is a read-only ownership check, not crafting-source authorisation.

**What this closes:** source-journal inventory entities can now be mapped
back to candidate loaded chest owners using existing managed Kiln APIs,
without mutating native `InventorySetup`. **What it does not close:**
the experimental journal observer has not been installed/tested in-game;
matching IDs from separate observations does not prove a single atomic
transaction or commit. There is no authoritative Magic property or
Flame Altar/base permission resolver, no typed category/water costs,
and no client-side crafting/building source inclusion. The PR stays
draft and v0.1.6 remains diagnostic-only.


## Upstream Kiln dev20 review — 9 November 2026 (06:25–06:27 UTC)

Compared the pinned *shipping SDK* revision
[`eba9a5e`](https://github.com/frostdev-ops/kiln/commit/eba9a5e1e0e0044a9146fdd53021a751e2957030)
(dev15), the existing native trace experiment's upstream source
[`77e5258`](https://github.com/frostdev-ops/kiln/commit/77e525888ac0d5f4f6b16103e945ed7ac049b50e),
and current [`main da1ad920`](https://github.com/frostdev-ops/kiln/commit/da1ad920fa5fc9be1696b5fa1d186399ccf8f9a8).
The upstream [v0.2.0-dev.20 prerelease](https://github.com/frostdev-ops/kiln/releases/tag/v0.2.0-dev.20)
was published 9 November 2026 at 06:27 UTC (release target `d35ee27`).
Main's subsequent `da1ad920` commit completed release work.

**Actual API changes relevant to our project:**

- **`kiln.items` is v3:** public managed `Items.Move`,
  `QuickStack`, `MoveAll`, `ContainerName`,
  `Items.Transfers.Keep/KeepItem/Deny`, post-transfer events and
  native `transfer_listen` handlers. [Inventory guide](https://github.com/frostdev-ops/kiln/blob/da1ad920/docs/kiln/modding/inventory.md),
  [C ABI](https://github.com/frostdev-ops/kiln/blob/da1ad920/native/core/abi/services/kiln_items.h),
  [managed v3 adapter](https://github.com/frostdev-ops/kiln/blob/da1ad920/managed/services/Kiln.Items/Moves.cs).
  A move between two inventory *owners* uses the game's slot transfer and an
  engine transaction **owned by that move**. It is not the recipe's own
  transaction. `transfer_listen` is restricted to the original
  quick-stack/move-all calls and is **not invoked by RecipeApply**.
  The native callback may move additional stacks in that transfer's transaction,
  but that does not make it a crafting hook.
- The managed `Items.TryGetResult(Task<ItemsResult>, out ...)` and
  `ResultPeekAvailable` allow early observation of **Kiln-submitted
  Give/Take results**, useful for save timing. They are **not** a native
  recipe-commit receipt or source-enrolment interface.
- **Declarative ECS programs** (`EcsPrograms.Bind`, `kiln.programs.json`)
  now run on worker threads with checked component access. They are separate
  scheduled ECS jobs, **not callbacks inside the engine's RecipeApply payment**.
- Building blocks for authored crafting stations and UI were expanded;
  a mod-defined station's processing is still driven by its **own inputs**,
  not a cross-base extension of vanilla material selection.
- Game hooks already existed at the **dev15 pin**: the current
  `mods/samples/HookSample/HookSampleMod.cs` is unchanged compared with
  that pin. It shows `Hooks.Detour(..., call => call.CallOriginal())`
  and shipped symbols, which can be used for *passive instrumentation*.
  This is not evidence of a newly supported transaction-aware craft API.
- No public v3 Items/Gameplay API or sample inspected here can register
  remote Magic Chest providers for **vanilla client craftability and original
  server recipe deduction**. We must add a specific upstream native bridge.

**Most useful native implementation clue:** dev20
[`native/services/items/engine.h`](https://github.com/frostdev-ops/kiln/blob/da1ad920/native/services/items/engine.h)
declares internal `access_from_tx(tx, Access*)`, which builds/checks
the component accessors from a *game-owned* transaction, and
`move_stack_in(Access, tx, from, slot, to, ...)`, which uses a
*caller-owned* transaction instead of implicitly committing a second
one. [`engine.cpp`](https://github.com/frostdev-ops/kiln/blob/da1ad920/native/services/items/engine.cpp)
validates accessors against component hashes, reads owners' inventory
links, invokes the game's slot-transfer function, and has a separate
commit/rollback path for standalone Move requests. These helpers are
**private to the native service**, not public SDK entry points. The
design shows how to preserve the game's transaction context, but
`move_stack_in` itself needs destination inventory capacity and cannot
serve as a complete remote crafting payer.

**Useful `mods/` patterns (not a solution by themselves):**

1. [`mods/tests/items-test/MovesTest.cs`](https://github.com/frostdev-ops/kiln/blob/da1ad920/mods/tests/items-test/MovesTest.cs):
   source/destination stock conservation, dry-run rollback,
   and item-entity preservation; adapt its assertions to an actual
   recipe transaction.
2. [`mods/tests/creative-test/CreativeTestMod.cs`](https://github.com/frostdev-ops/kiln/blob/da1ad920/mods/tests/creative-test/CreativeTestMod.cs):
   starts **real server craft actions** with `Gameplay.Inject.Craft`,
   then checks output and inputs; use this scaffolding but keep
   `FreeCrafting` **disabled** when validating real debit.
3. [`mods/samples/HookSample`](https://github.com/frostdev-ops/kiln/tree/da1ad920/mods/samples/HookSample):
   a pass-through native-symbol detour and a callable original; candidate
   for passive call-site tracing, **not** a safe place to implement
   transaction mutation in ordinary C#.
4. [`mods/samples/StationBlocks`](https://github.com/frostdev-ops/kiln/tree/da1ad920/mods/samples/StationBlocks)
   and [`mods/tests/stations-test`](https://github.com/frostdev-ops/kiln/tree/da1ad920/mods/tests/stations-test):
   test custom workbenches and stations with own inventory.
   They do not augment vanilla remote sources.
5. [`mods/samples/Claims`](https://github.com/frostdev-ops/kiln/tree/da1ad920/mods/samples/Claims):
   spatial `Gameplay.Protect` can *deny* crafting, not grant
   another player's Magic Chest permission. The real source security
   and Flame Altar/base membership are still unresolved.

**Recommended native implementation seam:**

- Validate `RecipeApply`'s **both** payer call sites
  (`InventoryPay` and `RecipePayAlt`), recipe inputs including
  category alternatives, the transaction operator, and the requested
  output amount. Leave the vanilla payer alone for unrelated actions.
- Resolve loaded eligible Magic Chest *owners* and their linked 8-slot
  inventory entities with native transaction-time access, **not** a
  stale managed tick-thread snapshot. Determine the authoritative Magic
  flag, base/altar scope, and ownership/permission policy before allowing
  remote debit.
- In an **isolated throwaway native test**, investigate whether the
  game's `InventoryRemoveItems(out, tx, chestOwner, item, slot, amount)`
  can append remote take operations within **that same original
  RecipeApply transaction**. This is a *hypothesis*, not an
  implemented/public API. Validate how the native payer handles
  shortfalls, category inputs, queued partial takes, duplicate links,
  transaction rollback and concurrent crafts. Do not return
  'paid' unless the complete real cost has been staged and can be
  committed atomically with the output.
- Then implement client-side source counts/ingredient choices using
  matching authority, without faking `UiCraftableAmount`. Station
  queues and building are separate later integration paths.

**Decision:** keep the shipping diagnostic Cross-base Storage **0.1.6**
on the tested/pinned dev15 SDK and leave PR #7 **draft**. No auto-bump
to dev20, no game DLL replacement, and no assertion that the
new moves API enables cross-base crafting. The next development effort
should be a disposable native original-transaction *payer/source probe*,
not another chest scanner.

## Released Kiln SDK tracking policy — updated 9 November 2026

**Supersedes the earlier "keep packaging pinned to dev15" decision
above.** Kiln's installed launcher/runtime follows frequently published
dev releases, so all three of this repository's **mod packaging** jobs
now resolve the **newest published development release** (not moving
`main`) at build time. A central `global.json` contains the
MSBuild SDK version; every mod project uses `Sdk="Kiln.Sdk"`
instead of independently pinning older versions. The
[`scripts/resolve-kiln-release.py`](../scripts/resolve-kiln-release.py)
helper validates published GitHub release metadata, SDK/API release
assets and dev tag syntax, and updates the build workspace mapping.
[`scripts/build.sh`](../scripts/build.sh) then fetches that exact Git
tag, checks that its commit matches the release target, packs matching
SDK and API NuGet packages, builds the mod, and creates the sidecar
`kiln-sdk-provenance.json`. Failure to verify release metadata/tag is
a **build failure, never a silent fallback to older Kiln**.

**Verified releases:** for
[`v0.2.0-dev.20`](https://github.com/frostdev-ops/kiln/releases/tag/v0.2.0-dev.20)
(tag commit `d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63`), all three
packages built in GitHub Actions on 9 November with 0 compiler warnings
and errors: [Cross-base Storage](https://github.com/bisdat/kiln-mods/actions/runs/42577853938),
[Endless Chests](https://github.com/bisdat/kiln-mods/actions/runs/42577853740),
[Survival QoL](https://github.com/bisdat/kiln-mods/actions/runs/42577853892).
The Cross-base package also passed the 22 quote/link tests. This
verifies **build and packaging compatibility**, not an end-to-end game
test of new runtime releases.

To track Kiln's multiple daily releases automatically, the separate
[main-branch scheduled workflow](https://github.com/bisdat/kiln-mods/blob/main/.github/workflows/kiln-latest.yml)
was merged via [PR #8](https://github.com/bisdat/kiln-mods/pull/8).
At **:17 and :47 UTC** each hour it checks the published dev release,
compiles all three mods against new versions (from this feature branch
until it merges), uploads artifacts/provenance, and caches the success
marker only after all three pass. It intentionally **does not deploy
or hot-reload user-installed mod ZIPs**.

The old native-craft-trace and recipe-input-probe files retain their
historical pinned-ABI source as **research fixtures**, not shipping
package dependencies; these must be ported to the newest released
Kiln branch before any new native functionality is deployed. This
separation does not justify promising native cross-base crafting.

## Source-only native recipe-payment preflight — 9 November 2026

Added a pure-C++20 *non-installable* experiment
[`sdk-proposals/native-craft-payment`](../sdk-proposals/native-craft-payment/README.md)
to PR #7. Its `make_plan` accepts **exact** per-item requirements,
real packed 8-slot inventory slot IDs, and explicit native-authority
gates for loaded, Magic-confirmed, player-authorised and base-authorised
remote source owners. It deliberately cannot derive any of those
permission bits from a template name or managed world scan. It
allocates player first, same-base Magic second, distant eligible Magic
third; invalid/duplicate source snapshots, unknown category ingredients,
overflow, and any ingredient shortage reject the *entire* plan.

An important property is now encoded: the actual engine's
`InventoryRemoveItems` **preferred slot is not guaranteed exact**.
`stage_plan` therefore requires the newly appended native transaction
journal to contain **only matching take operations** for each planned
slot, item and quantity. Any unsuccessful or off-slot stage produces an
`AbortNeedsRollback` result. There is no separate post-craft
`Items.Take`, no implicit transaction commit, no bypass of the
original payer for a local-only recipe, and no client craftable-amount
faking. The helper cannot itself guarantee the game will roll back,
so it must not be connected to a live recipe without proof of the
outer game's rollback path.

**CI evidence:** [native-payment workflow](https://github.com/bisdat/kiln-mods/actions/runs/42583677155)
passed **17 native host tests** on Linux GCC, Linux Clang and Windows
MSVC x64, and the upstream payer-removal source-seam gate against
**latest published Kiln dev20**. Host tests use synthetic inventory
data and mock transaction rollback: they are NOT a live-game debit.

The companion [native-craft-trace workflow](../.github/workflows/kiln-craft-trace.yml)
was updated to resolve **the latest published Kiln development release**
and use that *single exact immutable commit* in both Windows and Linux
native experiment builds. Its passive native payer/journal observers
still do not alter game payments, and compiled DLLs are not
uploaded or installed. The original static commit remains only
the default for reproducing a historical local experiment.

**Next genuine engine integration step:** determine the native payer's
cost list including selected category/alternative inputs, the
transaction-time trusted Magic and Flame Altar/base permission
resolver, and the exact authoritative rollback behavior when any
individual `InventoryRemoveItems` leaves partial staged changes.
Until those gates exist the prototype cannot be enabled for crafting;
UI and building still require separate matching source resolution.

## Payer-cost instrumentation + engine-style removal adapter — 9 November 2026

**Concrete implementation and verification now in PR #7:**

1. [`sdk-proposals/native-craft-trace/costs.py`](../sdk-proposals/native-craft-trace/costs.py)
   extends the *existing* Kiln Rules payer detours on the
   **latest published Kiln dev release** with an optional, bounded
   **120-byte `KilnRulesCostTrace` ABI** and managed
   `Rules.TraceRecipeCostRows()`. It reports player/station
   transaction operator, original recipe payer kind, owner,
   multiplier, native return, row count and up to eight raw
   item/count pairs. It copies read-only data **before** the native
   payer executes and publishes the result after the untouched
   payer returns. There are no new detours or client crafting
   overrides, and *zero resource withdrawals added by this patch*.
   Raw row offsets {+0x10 item,+0x14 count,stride 0x18} are a
   **hypothesis extrapolated from building's ApplyMaterials cost
   entries**, not verified for category inputs or RecipePayAlt.
   Pointers are capped, checked under native SEH, and never exposed
   to the managed observer. The trace cost and journal snapshots
   are **not atomically correlated across concurrent calls**.
2. [`sdk-proposals/native-craft-payment/native_take_adapter.h`](../sdk-proposals/native-craft-payment/native_take_adapter.h)
   adds an isolated current-layout **native removal adapter** that
   uses the actual `InventoryRemoveItems(Result12*, tx, owner,
   item, preferredSlot, qty)` x64 function signature rather than
   the previous abstract mock-only stage function. The adapter
   reads `tx.operator` at +0xe0 and original `tx.ops`
   pointer/count at +0xf0/+0xf8, checks *only newly appended*
   0x34-byte op records, and demands exact type=take, item,
   packed inventory slot and total quantity per take. A game
   error, shortened take, wrong slot, unexpected op or invalid
   transaction yields `AbortNeedsRollback`, never success.
   **It NEVER commits/rolls back** and must remain uninstalled
   until the real outer `RecipeApply` rollback path is proven.
   It does not collect eligible Magic Chests or decode native
   category ingredients. The read/write access to raw transaction
   bytes is simulated in CI; a live adapter additionally needs
   runtime-checked constants and SEH.
3. **Evidence:** the [latest released dev20 native trace
   build](https://github.com/bisdat/kiln-mods/actions/runs/42584680335)
   passed Linux managed/API ABI checks, native Windows x64
   `kiln-rules.dll` compilation and the observer-only safety
   validator. No patched native DLL was uploaded or deployed.
   [Native payment run
   #42585229144](https://github.com/bisdat/kiln-mods/actions/runs/42585229144)
   passed **28 synthetic host tests** (17 pure planner plus 11
   engine-style raw transaction tests) across Linux GCC,
   Linux Clang and Windows MSVC, plus the current published
   Kiln release native-seam gate. It did **not** execute an
   in-game craft or prove a production rollback.

**Additional permissions research:** dev20
[`native/services/players/log_tracker.cpp`](https://github.com/frostdev-ops/kiln/blob/d35ee27/native/services/players/log_tracker.cpp)
maps `CanAccessInventories` to player permission bit `0x02`;
the server
[`docs/re/players.md`](https://github.com/frostdev-ops/kiln/blob/d35ee27/docs/re/players.md)
corroborates it. A group permission is only one **necessary access
gate**, not proof that a player may use a particular other base's
chest. The public `Items.Read().SetupFlags` exposes Player/NPC
classification, **not a confirmed Magic crafting entitlement**.
`InventorySetup` link category `Generic` (4) or
`Virtual` (5) is an inventory structural type, **not a
remote-Magic authorisation flag**. `BaseIdComponent` currently
identifies a Flame Altar entity, not an arbitrary chest's
membership/owner. Any native resolver that assumes these fields
grant remote access is unsafe.

**Actual next in-game milestone:** validate raw native cost-list
samples for a known *ordinary* paid recipe, including category
ingredient selections and both payer paths, and compare them
against the original transaction journal in the SAME call. Then
implement an authoritative per-chest Magic/Flame Altar/rights resolver
on the server and its matching client-facing stock count. Without
those verified inputs, enabling `InventoryRemoveItems` from another
base or returning a crafting "paid" result is not justified.

**Release status:** current user-facing Cross-base Storage 0.1.6
still offers read-only diagnostics; no native runtime DLL or
experiment was installed or packaged. PR #7 stays **draft**.
