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

The original observations were from older builds; the server portion below
has now been **reconfirmed against the current official 26430253 dedicated
server executable**. The client build 26430625 still needs an equivalent
native collector/ABI verification, not a fake craftable-button fallback.

## Confirmed current server executable: the game already handles the payment

CI privately fetched **only** the official anonymous Steam dedicated-server
EXE (app 2278520/depot 2278521, Nov 4 2026 public manifest) and statically
analysed SHA256:
`f0d824be7fcd48ebfe2458ff5c28da26060c2074e3ec1fcac587e5a1d508aeb2`.
The proprietary executable was not uploaded, installed or committed. Only
bounded assembly call maps were retained in temporary build artifacts.

Uniquely matched *current-build* native functions and branch edges:

| Current RVA | Original-game operation | Confidence |
|---|---|---|
| `0x210f00` | Original craft owner/transaction wrapper, calls `RecipeApply` then rollback on failure | Original static call edge |
| `0x19a1d0` | `RecipeApply`; chooses ordinary/alternative payer, then output creation | Published Kiln symbol + current disassembly |
| `0x199c80` | Original ordinary recipe ingredient payer (`InventoryPay`) | Published Kiln callsite + current disassembly |
| `0x2081c0` | Original base-stock selector, invoked twice by ordinary payer | Unique current signature + two call edges |
| `0x1b09e0` | Original ordinary linked-inventory item deduction helper | Calls from base-stock item loop |
| `0x1b0bf0` | **Fuel** helper: not a normal item source, must remain separate | Dedicated fuel-list loop, op type 2 |
| `0x1af860`, `0x1b8310` | Original commit and rollback | Unique published signatures |

At the live original payer `tx+0xb1 == 2` is the stock-mode-2 test.
`0x2081c0` returns **four qwords**: an ordinary item-owner pointer/count
from each selected base record at `+0x30/+0x38`, and a separate fuel owner
pointer/count at `+0x58/+0x60`. Base record stride is `0x130`.
The payer takes from the operator first, then calls the original item
helper for the ordinary source list; the fuel list has its own helper.
This means a valid extension must append **only ordinary item-owner IDs**
to a **borrowed list scoped to the original recipe payer**, leaving all
fuel/source metadata and the game's commit/rollback untouched.

The version-pinned static verifier
[`scripts/inspect-current-crafting-stock.py`](../../scripts/inspect-current-crafting-stock.py)
requires the stock selector to be a unique signature and checks two original
payer references, separate item/fuel native ABI fields and the published
transaction symbols. [CI run #42643312142](https://github.com/bisdat/kiln-mods/actions/runs/42643312142)
passed. The old RVAs remain **research only** and must not be used as
unverified constants when Enshrouded updates.

[`native_stock_overlay.h`](native_stock_overlay.h) implements the
32-byte borrowed stock-list ABI with strict C++ layout assertions,
single authenticated player-record gate, all-proof-required item-owner
union and scoped storage that cannot be copied/moved while the original
payer holds its pointer. The original pointer is unchanged if validation
fails, and the fuel pointer/count are always unchanged. Its host tests
compile with GCC, Clang, MSVC; it is parsed by the disposable Windows
Kiln Rules native build. **It has not been activated in the live game.**


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
- `native_stock_overlay.h` + `native_stock_overlay_tests.cpp`: exact verified
  server stock-mode-2 list shape and safely scoped native ordinary source
  union with independent fuel list and solo-only actor-identity checks.
- `client_craftable.h`: original client amount hook is ready to use a
  **server-confirmed native payment capability**, but its production
  provider is null. It does not forge craftability.
- Existing original-payer bridge and settlement witness remain disabled;
  they may be useful for verifying the eventual real stock source extension.

## Precise missing native engine interfaces

1. **Server collector:** the current-build function, list layout, stock-mode-2
   branch and original-item/fuel separation are now identified. Still required:
   a safe live detour and server-native callback that obtains other-base
   **game-registered** Magic Chest owners under the same original `InventoryTx`,
   validates actor/base permissions, verifies the linked original 8-slot
   pages, and retains the appended owner list until the original payer returns.
   The 32-byte adapter exists but no detour is installed or activated.
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
