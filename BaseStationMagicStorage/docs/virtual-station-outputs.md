# BSMS: virtual station outputs as Magic Chest crafting stock

Status: **SDK feature request / research only**. No runtime implementation or in-game test is claimed.
Tracking: [BSMS #57](https://github.com/bisdat/kiln-mods/issues/57), [Kiln API proposal #68](https://github.com/frostdev-ops/kiln/issues/68). Source reviewed 2026-11-17.

## What the player wants

A finished item must **remain in the station where it was produced**, but:
- native crafting benches and other supported FactoryStations in the same active Flame Altar base can use it as recipe input, just as if it were stored in a Magic Chest;
- the player can inspect and manually withdraw finished output in a storage-like in-game interface, without finding each physical production station;
- any player choice to leave output in a particular station remains authoritative (the existing 26 station-type global switches, such as Oven OFF, must still work).

The current BSMS mode transfers outputs into deliberately named, unbound Magic Chests using Stations.Collect. This is already accepted in game and must remain intact as a fallback.

## Verified API and exact gap

| Native functionality | Present in Kiln 0.2.0-dev.29.15 / mod API 0.2.7 | Consequence |
|---|---|---|
| Enumerate stations and their current outputs | Stations.InBase, Stations.Read, StationInfo.Outputs | Can display finished stock and identify real slot locations |
| Combined base stock count | Bases.StockWithStations | **Read-only** and intentionally includes outputs for inventory targets; game does not consume them |
| Native craft-stock sources | Bases.Storage, Bases.Stock | Game uses InventoryCraftingStock containers (Magic Chests, refilling plants), not station inventories |
| Native factory supply | Stations.Plan, Stations.Fill | Pulls from game's base storage; cannot request outputs as an alternative live source |
| Physical output collection | Stations.Collect, Items.Move | Safe but removes items from the producing station |
| Manual transfer | Items.Move / Stations.Collect to player | Underlying server transaction exists; client-side remote UI and permission checks still needed |
| Station-screen UI | Ui.Crafting.AddPanel with CraftingPanelOptions.AtFactories; CraftingScreen.Station | Can show current output by a station, but **not** turn it into native crafting stock |
| Game UI names | StationInfo.DisplayName, Items.DisplayName, Ui.ItemImage | Can present meaningful stock listings |

Reference: [Stations guide](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/stations.md), [StationsApi.cs](https://github.com/frostdev-ops/kiln/blob/main/managed/services/Kiln.Blocks/World/Stations/StationsApi.cs), [Items guide](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/items.md), [UI guide](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/ui.md).

Important: StationInfo.Outputs is **not a typed output compartment**. A FactoryStation's inventory has generic slots. Kiln classifies a stack as output when it belongs to the selected/running recipe's output list **and not** its input list (including input category options). When selected recipe changes, classification may change. Old-recipe outputs may be classified as leftovers. A virtual-stock implementation must fail closed or adopt a more exact output classification, not expose every slot.

## Relevant external precedent: Craft From Stations (EML)

[Craft From Stations v1.1.0](https://www.nexusmods.com/enshrouded/mods/215) makes the game view **entire station inventories** as crafting-stock containers. The author expressly notes this includes unprocessed inputs. Its optional native Queue Fix addresses the self-source bug: while filling a station's own queue the game can choose the station as the input source, remove its ingredients, then put them back. The project requires client-side EML modifications and imposes restrictions on copying or creating parallel versions.

This proves only that the game's crafting-stock eligibility mechanism can be changed. It does **not** prove a safe output-only native stock source, support with Kiln alone, or manual remote inventory UI. Do not copy or redistribute those assets.

## Implementation decision: no unsafe stock template patch

A pre-boot resource patch that adds InventoryCraftingStock to every FactoryStation would expose inputs, not just outputs, and could reproduce the EML queue bug. It would also apply to every instance of that template rather than an independently managed base/type policy, making deposits and multiplayer state harder to reason about.

We will **not** alter inventory classification globally or introduce a shadow chest that duplicates item counts. Staging items in a real chest merely reproduces the existing, supported BSMS collection behaviour.

## Requested upstream contract

Ask Kiln to provide an optional output-source provider used inside both the **native displayed craft-stock calculation** and the **game's authoritative payment/fill transaction**.

Required invariants:

1. Opt in explicitly per station/base; default off. A disabled global station-type switch must override it. Do not enrol ordinary chests or unloaded bases.
2. Only safe, completed outputs as classified at action time; never count/stake inputs, running materials, water, or leftovers.
3. Native recipe validation and inventory deductions use the **same server-authoritative sources** and revalidate source/slot/identity/quantity/recipe/base/permissions at commit time.
4. One successful craft consumes each required item once, with no item copy, loan, double-use, or partial debit on refusal.
5. Source priority: player inventory then real Magic Chests then eligible station outputs, and preferably exclude the destination station as a source for its own fill. This avoids the known self-source cycle.
6. Revalidate every state transition: recipe switch, station removal, output removed, base merged/split, world unloaded, player disconnected, multiple players or mods acting concurrently.
7. Never make factory stations an automatic destination for the Quick Stack Station or BSMS collection as an incidental effect.
8. Coordinate native client material counts with the server's payment decision. A client-only inventory list is not enough.

## BSMS integration when available

- Add a separate global mode in Kiln's **Configs → Base Station Magic Storage**, defaulting to existing behaviour. Proposed choices: **Collect to crafting output Magic Chest** (current) and **Use outputs in place** (only if supported). Avoid acting on a station twice; explicit mode preference, not both collection and virtual stock consumption at once.
- Keep current per-base enable/fill/collect switches and the 26 already accepted type-level settings, with Oven OFF respected.
- Start with one opted-in charcoal kiln; do not change unrelated station types or the normal source list.
- The existing named-sink collector remains the fallback when unsupported / disabled. Never silently use generic CollectToBase.
- For manual withdrawal, use a client-facing read-only per-base output list first, then authorised server Items.Move / Stations.Collect with revalidation. Keep this a **separate UI enhancement** from native crafting-provider support: the user wants both, but one cannot be claimed to implement the other.

## Acceptance tests (real game, hosted Proton first)

1. At base A, a kiln holds 20 finished charcoal. No charcoal is in backpack or any Magic Chest. A blacksmith's native recipe **shows** it and can craft with it; kiln loses exactly the required number.
2. An enabled downstream production station can fill from remaining charcoal **without** moving it to a chest first.
3. Kiln queue +1/+10 and Stations.Fill work normally while its own inputs remain in the station; no self-sourcing or stalled queue.
4. Remaining charcoal can be manually inspected and withdrawn by a player through the agreed inventory UI; no stack duplication or silent deletion.
5. Oven OFF keeps its outputs out of this source pool and keeps existing BSMS auto-collection disabled.
6. Recipe switch; output==input ambiguity; old recipe leftovers; full station; base merge/split; missing player permission; concurrent clients and saves; removal/restart; incomplete transactions: no loss or duplicate stock.
7. Turning virtual mode off restores **existing** BSMS v0.1.3 behavior with the named crafting output sink.

No implementation PR or release until the Kiln API and a reproducible in-game acceptance test exist.
