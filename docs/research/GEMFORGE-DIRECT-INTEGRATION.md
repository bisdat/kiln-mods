# Gem Forge × Magic Chest: direct native integration research

**Status:** investigation / read-only; user rejected the proximity-loan/return model after it failed inventory return.
**Repository:** `bisdat/kiln-mods`, draft PR [#48](https://github.com/bisdat/kiln-mods/pull/48).
**Last verified:** 2026-11-15; Enshrouded hosted in Kiln v0.2.0-dev.28 through Proton.
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

## 3. Required production behaviour

1. Existing Gem Forge UI presents the backpack's gems plus any **eligible Magic Chest gems**, preserving the exact gem's level/rarity/instance identity, and the user's normal weapon selection. `Kiln.Items.FindInventories` covers *loaded* chest inventories only.
2. On an action, the server resolves the chosen gem's actual chest/slot/item instance and verifies proximity/authorization/source identity. It must not trust a client-reported item, source holder, or slot without independent validation.
3. Actual insertion, extraction, recycling, and upgrading must execute the game's legitimate forge constraints (essence tier/count, maximum level, weapon socket state). Material consumption and gem state update must be **atomic**, or correctly rolled back on any refusal.
4. A completed transaction debits source Magic Chests **exactly once** and publishes an authoritative inventory/game-state update. No temporary player backpack loan, no duplicate or cloned gem, no creation/deletion of unrelated items. Cross-base *unloaded* storage is a **separate** integration dependency requiring Building Tools-specific research.
5. The mod must fail closed on unsupported game/SDK builds, permissions, a disconnected player, a moved/changed chest, or absent native hooks. Do not claim success before a source/target/essence comparison against the engine's committed result.

## 4. Research sequence and evidence needed

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
