# Native crafting-stock integration: target and activation criteria

**Working objective:** a recipe selected in the normal Enshrouded crafting UI
can pay with authorized Magic Chests at other Flame Altar bases. Native
crafting, production, building, rollback, and replication must keep their
vanilla semantics. This is an **implementation boundary**, not a feature
release.

## Why the existing recipe-payer detour is not the preferred solution

Enshrouded already has a stock-aware recipe transaction. Independent public
game analysis (EMF `docs/engine_parts/inventory_crafting.md`) describes:
- `InventoryTx` stock mode `2` for crafting (byte `tx+0xB1`);
- `removeRecipeInputs` uses `InventoryRemoveItems` on the actor and base
  crafting stock selected through the original `PlayerBasesSystemServer`;
- the owning `craftRecipe` does selected category resolution, ingredient
  and water deductions, output creation, and final commit/rollback;
- the client has a separate `cache_crafting_stock` program using
  `PlayerBasesSystemClient`. The vanilla `UiCraftableAmount` hook only
  reads a derived, asynchronous cache and cannot repair ingredient rows.

These are independent reverse-engineering observations from older game
builds, **not** ABI proof for current Enshrouded build 26430253 / 26430625.
Do not use the older game RVAs as patch targets.

Therefore extend the **native original crafting-stock collector** itself on
both server and client, preserving its existing candidates as the first
entries. Do not bypass the payer and then call `Items.Take` outside the
original transaction. Do not change the recipe counts to zero.

## Implemented on this feature branch

- `stock_sources.h`: pure native policy `extend()` that preserves
  the already-authorized vanilla source order, adds current-base then
  distant source candidates, collapses **exact** already-vanilla aliases,
  rejects conflicting linked-inventory identities, ordinary chests,
  unloaded, uninitialized, ambiguous or unauthorized sources.
- `stock_sources_tests.cpp`: host regressions on this policy.
- `client_craftable.h`: original client amount hook is ready to use a
  **server-confirmed native payment capability**, but its production
  provider is null. It does not forge craftability.
- Existing original-payer bridge and settlement witness remain disabled;
  they may be useful for verifying the eventual real stock source extension.

## Precise missing native engine interfaces

1. **Server collector:** identify the exact currently shipped function and
   calling convention by which `PlayerBasesSystemServer` supplies eligible
   `InventoryCraftingStock` owners to `removeRecipeInputs`. Expose a
   safely bounded, write-tracked source append inside the original
   `InventoryTx` access scope. Test the loaded/unloaded source policy.
2. **Game authorization:** use the game's actual Magic stock component,
   dynamic stock enable state, active altar/base relationship and
   actor/player permission checks. Client inventory/template names and base
   AABB geometry are insufficient. Do not assert authorization without
   matching game evidence.
3. **Client collector:** identify native `cache_crafting_stock`'s current
   interface and its use of `PlayerBasesSystemClient`, including material
   category choices. The client may display remote counts only when its
   connected server supports the same extension, with consistent source
   identities and a fresh state epoch.
4. **Consistency:** preserve vanilla local-first ordering, player backpack,
   currently picked category material and water charges. Make multi-craft,
   output-full rollback, two simultaneous players, save/reload and changes to
   remote chest inventories fail cleanly without duplication.
5. **Update compatibility:** native hook identity, ABI, component access
   layout and original caller must be resolved from the CURRENT immutable
   Kiln/game build. Any missing symbol or proof turns cross-base OFF, leaving
   the unmodified original game collector and stock cache.

**Go/no-go:** only ship a runnable client/server build when placing a recipe
in the normal UI with the required ingredient exclusively at Base A causes
the client at Base B to show that same ingredient as available, and the
server consumes that exact remote stack *within the original transaction*
while producing the expected output exactly once. A native compile alone,
or an enabled craft button without a matching paid output, is a NO-GO.

Sources:
- <https://github.com/matenm/emf-enshrouded/blob/main/docs/engine_parts/inventory_crafting.md>
- <https://github.com/matenm/emf-enshrouded/blob/main/docs/ecs_programs.md>
- <https://github.com/frostdev-ops/kiln/blob/v0.2.0-dev.20/docs/re/creative-mode-146.md>
