# Gem Forge × Magic Chest — direct native integration

> **Historical SDK 0.2.5 research snapshot, superseded by Kiln v0.2.0-dev.29.15 (API 0.2.7).**
> The three previously blocked contracts (cross-holder actions, native chest-gem offers and base-storage essence payment) shipped in that release. The production v1.0.0 implementation, in-game acceptance summary and installation guide are in [GemForgeStorage/README.md](../../GemForgeStorage/README.md). The older analysis below is preserved as evidence of why item lending was rejected, **not** as a current API reference.

**Status:** design / SDK integration research; no production implementation.
**As of:** 2026-11-17. **Downstream:** [bisdat/kiln-mods draft PR #48](https://github.com/bisdat/kiln-mods/pull/48).
**Upstream:** [Kiln #42](https://github.com/frostdev-ops/kiln/issues/42) **closed (candidate snapshot delivered)**; [Kiln #61](https://github.com/frostdev-ops/kiln/issues/61) **open (remaining native Magic Chest integration)**.
**Runtime:** the last actual tested mod is **GemForgeStorage v0.2.1**, read-only, compiled for Kiln dev.29. **It has not been updated, built or tested against Kiln 0.2.0-dev.29.11 / mod API 0.2.5.**
**Non-negotiable:** no proximity/temporary inventory borrowing, no return queue, no duplicated or phantom gems, no unvalidated source/payment changes, and no replacement for the native Gem Forge UI.

## Goal and acceptance boundary

At the game's own Shroud Forge, the GEMS picker should list eligible loose gems from authorised **Magic Chests**, alongside the normal player gems and socketed-gem rows. The player should be able to select **any suitable player-held weapon** and insert one of those chest gems, without moving either item into a temporary holder. Upgrading/recycling chest gems and paying the forge's normal **Archaic Essence** price from authorised chests are also desired. Respect the game's forge tier, gem level, compatibility, socket and failure checks.

A working inventory scan or passive UI snapshot is **not** acceptance of this behaviour. We will not tag/release PR #48 while the above user-facing path is absent.

## SDK 0.2.5 — verified public contracts (source-reviewed, not yet game-tested here)

Kiln **0.2.0-dev.29.11** was announced by its developer in [#42](https://github.com/frostdev-ops/kiln/issues/42#issuecomment-6931871143), with mod API **0.2.5** and documented support for the new gem system.

| Capability / API | Supported | Critical limitation |
|---|---|---|
| `ui.gemforge.inspect`: `Ui.Values.GemForgeCandidates()`, `SubscribeGemForgeCandidates` | Immutable snapshot of the native **GEMS** list, including scroll-hidden rows, source/display views, socketed weapon carriers, selected row and selection context | **Observation only.** Chest items are not in the native list; it cannot inject a new entry or authorise any server transaction. Client-only; currently restricted to measured game client build 26430625 |
| `gems.read`: `Gems.Catalog`, `Gems.Get`, `Gems.Equipped`, `Gems.Socket(item)` | Read gem definitions/equipment and socket contents | Does not turn a UI view into an authoritative chest identity |
| `gems.craft`: `Gems.Socket(player, carrier, gem, forge, options)` / `Unsocket` / `Upgrade` / `Recycle` | Code-driven native forge actions with validated instance refs, cost, results and events | Server-side and **single source holder**; requires actual forge; not a client-grid action replacement |
| `GemCraftOptions.Holder` | Can target one authorised non-player inventory such as a chest for gem/carrier lookup | In `GemCrafting.Submit`, **both carrier and gem** are resolved from `Items.Read(options.Holder ?? player)`. Thus a loose chest gem can be upgraded/recycled by code, and socketing works if **both items are in the same holder**; **player-held weapon + chest gem** is not directly supported |
| `GemCraftOptions.Cost` | Can replace the required resource/item and count or make the action free | **Player still pays.** It is *not* a payment-inventory selector; recycle output and unsocket return also go to the player |
| `GemCraftingEvent` / `GemCraftedEvent` | Observe or cancel native actions, observe completed actions | Cancel is not a supported redirect to alternate item/source inventory; do not substitute a two-step debit/recredit workaround |

**Canonical references:** [gem UI snapshot](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/ui-gemforge.md), [forge actions](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/gems-forge.md), [implementation of `GemCrafting.Submit`](https://github.com/frostdev-ops/kiln/blob/main/managed/services/Kiln.Abilities/Gems/GemCrafting.cs), [release notes](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/release-notes/0.2.0-dev.29.11.md).

Distinguish the overload `Gems.Socket(item)` (**read socket**) from `Gems.Socket(player, carrier, gem, forge, options)` (**perform insert**). `GemForgeCandidates` becomes unavailable when the GEMS grid is closed or undrawn; its selected row is client display context, not authority for any action.

### What changed from the original reverse-engineering plan

Upstream's client disassembly identified the forge screen at RVA `0xcacb20`, list builder `0xd27cb0` and grid `0xcaefc0` for client build **26430625 only**. The builder combines loose gems in the player's Generic inventory pages with existing socketed-gem carrier rows. The grid does use the ordinary `ItemSlot` renderer (our previous hypothesis that it used a separate primitive was incorrect). The unknown coverage gap of our original slot probe is now bypassed by the supported `GemForgeCandidates` snapshot API. **Do not hook build-specific RVAs or extend the old observer.**

These findings and the official API close the previous request [Kiln #42](https://github.com/frostdev-ops/kiln/issues/42); they do **not** provide an editable native provider or chest-source transaction.

## Developer's prioritised next-patch design (17 November 2026) — **not shipped**

In [Kiln issue #61](https://github.com/frostdev-ops/kiln/issues/61#issuecomment-6940602682), the developer says **all three gaps are now prioritised for the next patch**, and proposes an additive API design. These names **are provisional** and are not present in the latest verified runtime, **0.2.0-dev.29.14 / mod API 0.2.6**.

1. `GemCraftOptions.GemHolder` (gem source) and `CarrierHolder` (weapon inventory), each defaulting to the player. The authoritative server re-reads and validates exact item/level/socket state and commits the native forge action across both inventories as one transaction. A refused transaction must move/consume **nothing**. Mod policy authorises eligible storage: **loaded** Magic Chest; use `Bases.Of(chest) == Bases.Of(player)` to restrict to the player's current base.
2. A capability-gated **native GEMS grid provider**, with short-lived **server-minted opaque chest-slot/item-instance tokens**. The UI presents real chest gems alongside vanilla candidates; selection submits a token, which the server revalidates, rejecting stale, inaccessible or duplicate entries. Never fabricate `UiItemView` client entity IDs or replace the game UI.
3. `GemCraftOptions.PayFrom` for player / specific chest / player's base Magic Chests (game's normal stock rule), and `ReturnTo` for removed-gem and recycling output destinations. The **game still decides Archaic Essence cost**; debit and item changes are atomic. A missing cost fails `NotEnough` without moving items.

The developer explicitly notes names may change until release, and will provide the final API contract when shipped. **Do not compile against provisional members**, implement temporary backpack lending, or claim the native UI is now editable. The intended policy matches our requirements and does not need a separate custom UI or a cross-base remote-loading assumption.

**Downstream action once released:** inspect the exact new `docs/kiln/services/gems-forge.md`, UI API and SDK source, then update draft PR #48 and test a single controlled socket transaction on a backed-up world, verifying source gem, weapon socket and Archaic Essence before/after. Do not merge or tag before acceptance.

## Remaining upstream contracts — tracked in Kiln #61

1. **Cross-holder gem transaction:** safely combine a player-held carrier and an authorised chest's precise gem instance, with server-side validation and an atomic native socket operation. One `GemCraftOptions.Holder` cannot express two different owners.
2. **Native GEMS provider extension:** insert eligible remote/chest candidates into the *original* selectable gem grid, and translate a selection to an authenticated server request. A read-only copied candidate list cannot insert rows; the native selected-row lookup currently knows only player inventory.
3. **Price source (Archaic Essence):** permit a separately authorised chest/inventory as the forge's normal price source, with transactional insufficient-funds/refund behaviour. `GemCraftOptions.Cost` specifies price, not its holder. Recycle rewards and unsocket destination should be defined as part of the same contract, rather than emulated through unrelated inventory moves.

**Authorisation and consistency:** on the authoritative server resolve fresh item references (including per-instance level and socket state), verify that the player can use each chest, confirm forge access, currency, compatibility, available space and current world. Fail closed on stale references, unloaded chests, refilled/full chests, simultaneous actions, lost connections and build/capability mismatches. Never accept client `UiItemView.ClientEntity` or a displayed row as a server-side item ID.

No part of 0.2.5 has been shown to offer cross-base inventory access to **unloaded** chests. Do not assume it.

## Existing real-game evidence — retained for context

User-tested on **Kiln 0.2.0-dev.29**, hosted Enshrouded under Proton, before SDK 0.2.5:

- Native placed forge: entity `5930`, 3 m away; template `Shroud_Gem_Forge_07_AncientLands_Premium_PLAYER_CRAFTED`. It did **not** carry the `ShroudForge` ECS component in that test. `HudScreen.ShroudForge=45` refers to the UI, not an ECS requirement.
- Read-only chest scan: **133** loaded Magic Chest inventories, **85** weapon-gem entries and **13,750** Archaic Essence units. These numbers describe **that session only**, not a guaranteed current count or authoritative cross-world reach.
- v0.2.1 UI probes: client `HudScreen.ShroudForge` detected; `Ui.Values.SubscribeSlots` saw 3–8 draws from the same **two already-socketed gems** (Gem of Storm's Eye and Gem of Focused Light) and up to three player-held weapons (Electric Initialiser, Dragoncore Staff, Wolf's Hydra). The user confirmed Wolf's Hydra's gem socket was empty. The long native GEMS list visible to the player was **not fully captured** by the probe.
- The old server event watcher saw **zero** `weaponGemAction` events while **browsing** only. That is not evidence about an actual committed forge operation.
- The abandoned **v0.1.x loan/return** design left items in the player's backpack if an original chest was full/refilled, hence the permanent no-loan constraint.

For raw frame logs, original function research, experimental commands and historical test results, see [Gem Forge dev.29 probe history](GEMFORGE-DEV29-PROBE-HISTORY.md). That file is archival evidence; its old next-test instructions are superseded.

## Implementation plan when #61 resolves or a supported alternative is confirmed

**Phase A — supported, non-mutating observability:** update the research mod against **SDK 0.2.5**, replacing the generic slot census for the GEMS grid with `GemForgeCandidates()` / `SubscribeGemForgeCandidates`. Confirm correct capability checks, immutable list, source vs displayed gem, carrier vs loose gem, selected row and refresh behaviour; compare with separate server-side `Items.Read` *without attempting to join client entity IDs to server IDs*. Use a disposable test profile and no inventory mutation.

**Phase B — exact inventory and payment plan:** using only **authoritative server-side** Kiln APIs and confirmed upstream contracts, check source chest, player-held carrier, cost chest, valid refs, permissions and location. Surface unsupported combinations as **not supported**, never silently move items. Demonstrate fail-closed behaviour with stale/duplicate/missing entries.

**Phase C — backed-up acceptance test, only after a safe native path exists:** test one controlled socket operation first, comparing source gem, destination socket and essence before and after. Then test upgrade, recycle, unsocket, chest refills, full inventories, concurrent actions, disconnect/rejoin and invalid destinations. No blind retries of uncertain commits.

**Production gate:** native UI really shows eligible Magic Chest gems and uses selected item/weapon as intended, no borrowing/return, game-consistent cost and success/refusal. Only then consider merging/tagging PR #48.

## Administrative state

- [Kiln issue #42](https://github.com/frostdev-ops/kiln/issues/42): completed; SDK candidate snapshot delivered.
- [Kiln issue #61](https://github.com/frostdev-ops/kiln/issues/61): upstream API discussion of three remaining contracts.
- [bisdat/kiln draft PR #1](https://github.com/bisdat/kiln/pull/1): former raw `play.models` probe, **superseded** by official snapshot API; it was never compiled/tested and should not be installed.
- [bisdat/kiln-mods draft PR #48](https://github.com/bisdat/kiln-mods/pull/48): research mod remains read-only v0.2.1; no build, release, live inventory change or merge made for SDK 0.2.5.
