# Gem Forge × Magic Chest: direct native integration research

**Status:** investigation / read-only; user rejected the proximity-loan/return model after it failed inventory return.
**Repository:** `bisdat/kiln-mods`, draft PR [#48](https://github.com/bisdat/kiln-mods/pull/48).
**Last verified:** 2026-11-16; Enshrouded hosted in Kiln v0.2.0-dev.29 through Proton, NoClip confirmed fixed by user.
**Constraint:** no borrowing into the player's backpack, no synthetic gems, no return queue, and no unverified debit/credit operations.

## 1. Observed facts (user's live hosted world)

| Test | Observation |
|---|---|
| Player | `bis_dat`, player entity 1 |
| Native Gem Forge | Entity **5930**, **3.0 m**, `Shroud_Gem_Forge_07_AncientLands_Premium_PLAYER_CRAFTED`; placed item `UsedItem=3250908839` |
| `ShroudForge` ECS | Absent on this placed forge; **do not** assume its presence |
| Magic Chest scan | **133** loaded chest inventories |
| Gem inventory | **85** weapon-gem slot entries; includes fully identified item instances |
| Archaic Essence | **13,750** units in loaded Magic Chests |
| Kiln services | `PlayerStates`, `Items`, `World`, `Spawn` available |
| `PlayerStates.ClientActions` | `weaponGemAction` exists, **28 bytes**, **CanCancel=True** |
| UI browsing with watch | **0 captured `weaponGemAction`**, while user browsed a Gem Forge and selected items |
| Inventory writes in v0.2.0 | **None**; observational code only |

Interpret the absence narrowly: selecting/previewing items in this specific test did not produce a captured **server** `weaponGemAction`. It does **not** prove where the UI is implemented or whether actually socketing, extracting, upgrading, or recycling produces that event.

## 2. Verified upstream source leads

### 2.1 Actual server operation

Source: [`docs/re/items.md` section 16](https://github.com/frostdev-ops/kiln/blob/main/docs/re/items.md).

The Kiln research team reverse-engineered the native program **`equipment_actions`**, which handles both ordinary equipment changes and the **weapon-gem action**. On the specific research server build **26430253**, it identifies:

- `equipment_actions` update RVA **`0x1472c0`**.
- Weapon-gem helper RVA **`0x144bb0`**, distinct from equip helper `0x144950`.
- Other-write components: `Inventory`, `InventoryEmptyState`, `InventorySetup`, `ItemState`, `Level`, `OwnerRelationship`, `SlotSelection`, `WeaponGemSlot`.
- Native output event **`GemCraftingEvent`** and `InventoryFull`.

**Scope of proof:** these are documentation of offline/static RE plus lab work on that named executable. They are not verified addresses on the user's client or a safe, complete transactional hook. Do not apply a hard-coded RVA or change a `weaponGemAction` payload without tracing its exact layout and validation.

The server control-event API [`PlayerClientActionEvent`](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/modding/player-states.md) exposes the raw 28-byte action from client, provided a gem operation actually sends it. It can hold/cancel the action but **does not provide a replacement Gem Forge operation** or make source inventory authoritative by itself.

### 2.2 Client-side forge picker

Source: [Kiln UI API](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/ui.md), [`Kiln.Ui/PublicAPI.Shipped.txt`](https://github.com/frostdev-ops/kiln/blob/main/managed/services/Kiln.Ui/PublicAPI.Shipped.txt).

Kiln's existing game-screen reflection includes **`HudScreen.ShroudForge=45`**, readable through `Ui.Screens.Current` on a client. **Note:** this UI enum's name is *not* evidence that a placed forge carries the `ShroudForge` ECS component; the live test found it absent.

Upstream docs also describe `Ui.Values.Slots()` and `SubscribeSlots()` to **inspect slots drawn in the last UI frame**. The doc says this feature comes from **Kiln v0.2.0-dev.29**, which was **not the latest published release on 2026-11-15** (GitHub Releases still had dev.28). It cannot inject extra Magic Chest slots or alter the underlying UI inventory-provider routine, even once available. A client-side mod/probe should use it only after an SDK release containing it, and must not equate client entity IDs with server entity IDs.

The specific *client* function that supplies Gem Forge selectable gems has **not yet been identified**. The vanilla picker is separate from the server's authoritative transaction; being able to read Magic Chest inventories on the host alone does not populate the client's list.

## 2.3. Dev.29 client forge slot capture — observed and corrected (2026-11-16 03:46 UTC)

Read-only v0.2.1 probe successfully subscribed to the native forge HUD. **18,941 client UI frames**, including **3,357 `HudScreen.ShroudForge` frames**, with 10 bounded captures. The UI listener was detached cleanly by `gforge ui-watch off`. The sampled forge display showed 3–8 slot *draws* per frame (some repeated, not additional items). Those drawn entries resolved through Kiln's dev.29 catalogue:

| Client item ID | Item name | Slot provenance shown by client |
|---|---|---|
| `0xe52a5ac0` | Gem of Storm's Eye | `holder=0, holderFlags=0` |
| `0x03c1b61e` | Gem of Focused Light | `holder=0, holderFlags=0` |
| `0x340e0dd9` | Electric Initialiser | `holder=1, holderFlags=7` |
| `0x3f512650` | Dragoncore Staff | `holder=1, holderFlags=7` |
| `0x20754f9e` | Wolf's Hydra | `holder=1, holderFlags=7` |

**Essential correction provided by the user:** Both gems are **already socketed in weapons** and shown by the forge as **available for removal only**. `Wolf's Hydra` is an intentionally selected test weapon with an **empty** gem socket. Accordingly, the two `holder=0` entries are *display views of socketed gems*, **not newly discovered external inventory sources**, and their numeric client IDs do not establish which player weapon they belong to or any Magic Chest association.

**Actual unanswered question:** Which function supplies the **new gem candidates** when the user selects the empty socket in `Wolf's Hydra` and opens the forge's insertion picker? None of the above sampled frames establishes that list's source. **Do not** implement or claim a Magic Chest UI provider based on these socketed-gem display records.

Next **safe read-only experiment with the existing v0.2.1 build**: start `gforge ui-watch on`, open the forge, choose `Wolf's Hydra`'s empty gem socket, open the **choose/insertion gem** list (without actually inserting or upgrading), close it, collect `gforge ui` + `gforge ui-recent` and turn the watch off. Compare that list's drawn slot holder/page patterns against the current socketed-gem display. If needed, collect the bounded `Gem Forge Research UI:` lines from `kiln.log` to avoid losing intermediate frames from the six-entry `ui-recent` output.

## 2.4. Complete dev.29 UI diagnostic log — main gem catalogue remains invisible to slot inspector

Source: user's **full** `grep -F 'Gem Forge Research UI:' kiln/logs/kiln.log | tail -n 45` output, 2026-11-16. The output contains both captured sessions and no other `Gem Forge Research UI:` entries in those intervals.

| UTC interval | Native screen transitions | Logged visible item IDs | Slot draws |
|---|---|---|---|
| 03:46:03–03:46:07 | `InGame/InGame -> Other/ShroudForge -> InGame/InGame` | two existing socketed gems plus three weapons | 3, 6 or 8 |
| 03:53:13–03:53:32 | same screen transitions | the **same** two existing socketed gems and three weapons | 3, 6 or 8 |

During the second session at **03:53:15–03:53:30**, the three weapon entries drop out of the *captured* slots, leaving only `0xe52a5ac0` (socketed Gem of Storm's Eye) and `0x03c1b61e` (socketed Gem of Focused Light). They reappear in the recorded slots just before the forge closes. That is evidence of *different visible slot sets*, **not** proof of which forge tab or selection state was active.

The user separately confirms the genuine game's forge UI shows its **long list of available gems immediately on opening**, including the two socketed gems. Because **no other gem IDs appear anywhere in the full observer output**, the `Ui.Values.SubscribeSlots` instrumentation has an incomplete view of the Gem Forge's item list. The captured list should not be interpreted as the forge's actual candidate set or as a sign that it cannot see inventory items.

**Upstream reference check:** Kiln's `docs/re/game-ui.md` describes the game's immediate-mode UI and generic slot drawing, while `docs/re/items.md` §16 documents the **server** `equipment_actions` path. Neither currently identifies the **client-side Gem Forge gem catalogue construction/enumeration routine**. The next dependency is native client reverse engineering of that catalogue/provider path on a supported game build; no further `ui-watch` sampling or destructive gem actions are justified by this evidence.

**Safe research boundary:** do not patch a guessed client RVA, attach an unvalidated `Kiln.Sigs` detour, insert client-only synthetic gems, treat client-rendered entities as server inventory identities, or reintroduce inventory borrowing. The native client provider and server transaction authority must both be identified before proposing implementation.

## 3. Required production behaviour

1. Existing Gem Forge UI presents the backpack's gems plus any **eligible Magic Chest gems**, preserving the exact gem's level/rarity/instance identity, and the user's normal weapon selection. `Kiln.Items.FindInventories` covers *loaded* chest inventories only.
2. On an action, the server resolves the chosen gem's actual chest/slot/item instance and verifies proximity/authorization/source identity. It must not trust a client-reported item, source holder, or slot without independent validation.
3. Actual insertion, extraction, recycling, and upgrading must execute the game's legitimate forge constraints (essence tier/count, maximum level, weapon socket state). Material consumption and gem state update must be **atomic**, or correctly rolled back on any refusal.
4. A completed transaction debits source Magic Chests **exactly once** and publishes an authoritative inventory/game-state update. No temporary player backpack loan, no duplicate or cloned gem, no creation/deletion of unrelated items. Cross-base *unloaded* storage is a **separate** integration dependency requiring Building Tools-specific research.
5. The mod must fail closed on unsupported game/SDK builds, permissions, a disconnected player, a moved/changed chest, or absent native hooks. Do not claim success before a source/target/essence comparison against the engine's committed result.

## 4. Dev.29 research checkpoint

- Kiln **0.2.0-dev.29 was published 2026-11-16 00:04 UTC** and the user confirmed it installed on their Proton-hosted game; NoClip works again (upstream's release notes credit the user's report).
- The dev.29 SDK now exposes `Ui.Values.SubscribeSlots`, `Ui.Screens.Current`, and `HudScreen.ShroudForge`. The candidate read-only v0.2.1 mod uses these **client-side** services to capture the forge's displayed slot inventory and a screen transition log. A slot drawn in the client UI is not necessarily a server item instance; client entity IDs cannot be reused as server IDs.
- Dev.29 additionally publishes `Kiln.Systems` program wraps and typed hooks, which may support **later** server-side observation of `equipment_actions` and its gem handler without hard-coded RVAs. They are not used in the client probe, and no inventory writes have been authorized.
- **Pending in-game acceptance:** confirmation that v0.2.1 reports `HudScreen.ShroudForge` and the forge's rendered slots (or an explicit capability failure). Until then, do not claim the screen's item-provider has been identified.

## 4a. Research sequence and evidence needed

- **Read-only client screen census:** verify the game reports `HudScreen.ShroudForge` while opening the user-placed forge. When an official dev.29 SDK arrives, use the **read-only** slot snapshot API to correlate drawn gem entries with the backpack. This alone does not add storage entries.
- **Server gem-operation trace:** with explicit user consent, on a backed-up throwaway item, observe an actual `weaponGemAction` and inventory deltas for an operation; do not require destructive tests merely to infer the UI picker. Label action payload dwords as **uninterpreted** until field meanings are proven.
- **Native handler analysis:** trace `equipment_actions` → gem handler `0x144bb0` on the *verified current* executable and find the validation/debit/commit path. Use Kiln's advanced hooks only after signature-based symbol qualification; do not reuse a build-specific RVA blindly.
- **Client item-provider analysis:** identify the forge gem-list and eligibility predicate. Determine whether it can enumerate remote Magic Chest slot identities under the game's own source model, or whether a client-side proxy/overlay is necessary. Vanilla-compatible UI integration must be demonstrated rather than assumed.
- **Transaction safety acceptance:** multiple visually identical gems with different levels; partially filled/full/refilled chests; spent essence versus upgrade failure; concurrent clients; disconnect/restart; same gem held in multiple worlds; insertion/extraction/recycling; limitations of unloaded bases. Fail closed and compare stable identities.

## 5. Explicit decisions

- **Reject proximity-loan design**, including retries, refill workarounds, and default/alternate chests. That architecture was in v0.1.x but is absent from the research build v0.2.0.
- **No more changes to live inventory** until the native action and client selection path are understood.
- **Keep PR #48 draft**, no merge/tag/release of a merely read-only probe or an unverified transaction implementation.
- **Do not conflate** ShroudLink's historical `gemforge` proximity-lending module, the game's `HudScreen.ShroudForge`, and the `ShroudForge` ECS component. They are different things.

## 6. Evidence and sources

- User test output, 2026-11-15 21:23–21:24 UTC; relevant command lines and results are recorded in this conversation.
- [Kiln UI API and UI slot inspection notes](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/ui.md) (dev.28 screen reflection, unreleased dev.29 slot inspection).
- [Kiln native RE dossier, items §16](https://github.com/frostdev-ops/kiln/blob/main/docs/re/items.md) (equipment_actions and weapon-gem function).
- [Kiln player action reflection](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/modding/player-states.md) (`weaponGemAction`).
- [Kiln advanced hook API](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/hooks.md) (symbol-based hooks, build safety).
