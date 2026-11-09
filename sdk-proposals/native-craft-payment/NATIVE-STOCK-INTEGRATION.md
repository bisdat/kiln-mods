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

### Current server selector call boundary (9 November 2026)

The verified `0x2081c0` original selector **does not return the stock
array through RAX**. It takes four MS-x64 arguments: `RCX = status*`,
`RDX = GameStockLists*`, `R8 = primary bases*`, `R9 = fallback bases*`.
It returns the same `status*` and writes a success byte at `status+0`
alongside separate item and fuel arrays. This result shape is derived
from original-game assembly on the pinned current server EXE, *not* from
the older EMF build.

- [`server_selector_abi.h`](server_selector_abi.h): native wrapper that
  **always calls the game's original selector first**, preserves its
  status and the fuel list, and permits ordinary item-source extension only
  when the exact game caller and borrowed buffers have been verified.
- [`server_stock_scope.h`](server_stock_scope.h): scoped TLS owner-list
  lifetime across the original recipe payer. Holds **two distinct buffers**
  for the game's two observed selector calls and refuses recursion, unrelated
  transaction/actor calls, missing native grants and invalid source lists.
- Unit tests exercise the exact native call shape, all-or-nothing original
  delegation, nested lifetime, and two simultaneous result pointers.

**Not yet connected:** the actual runtime detour of the game selector,
native owner/altar/permission enumeration, and client
`cache_crafting_stock`. The wrapper deliberately has no source provider in
the released Kiln integration and never claims a working craft.

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

## 9 November — first actual server hook, compiled but not gameplay-tested

The opt-in native integration now contains an **actual original-game stock
selector detour**, not just a stand-alone source-union type:

- [`server_live_roster.h`](server_live_roster.h): on the original recipe
  transaction, enumerate the game's own 16 base crafting-stock records;
  verify real `keen::ecs::InventoryCraftingStock` **template** components
  with `GetTemplateComponent`, the actor's live `kiln.players` **record**
  and its single-player inventory/base-edit permissions, plus the linked
  inventory pages using the experimental `kiln.items.read_in_tx` native
  extension. The original payer retains ingredient selection, consumption,
  output and rollback. Local-first source union and fuel separation remain.
- [`server-stock-live-hook.py`](server-stock-live-hook.py) patches a
  **disposable checkout** of released Kiln. The stock detour is installed
  only when explicitly compiled with `CBPAY_ENABLE_SERVER_STOCK`, the
  executable SHA256 is exactly that of the verified **dedicated-server**
  26430253 binary, the original caller is verified and both original
  game selector calls match their E8 callsites. The original selector is
  called **first**. Any uncertain evidence delegates unchanged to vanilla.
- [CI #42654759767](https://github.com/bisdat/kiln-mods/actions/runs/42654759767)
  **passed** strict GCC/Clang tests and Windows x64 compilation of the real
  instrumented Kiln Items and Rules services with the opt-in selector hook
  compiled. The DLLs were neither uploaded as a playable mod nor installed.
- The recorded native base layout, component eligibility, player permissions
  and full transaction stock conservation **have not been runtime-tested**.
  A successful native compilation does not establish a correct live hook.

### Critical client-host distinction (the user's game)

The user runs Enshrouded single-player under Proton:
`enshrouded.exe` (client process, hosting its own server world).
The current compiled stock hook accepts **only** the dedicated-server
`enshrouded_server.exe`; on client process it remains OFF.

The published Kiln build metadata verifies current client executable
**26430625** as SHA256
`5d9ef2f140b8ed49e30dbee655841b89d08baec20f799a1817e171a18a850c77`
and confirms the client can host a server world. But it **does not**
provide a verified current-build client version of the `0x2081c0`
dedicated-server stock-selector callsite or of client program
`cache_crafting_stock`. Server RVAs MUST NOT be used on the client.
The next implementation target is the real client-hosted stock-selector
and UI stock-cache interface, including source-identity replication.
Do not send the user the dedicated-server lab DLL; it cannot make their
single-player crafting menu display remote stock.

## 9 November — client-host original payer callsites established

The user supplied a **read-only metadata map** generated from their
installed `enshrouded.exe` **26430625**, SHA256
`5d9ef2f140b8ed49e30dbee655841b89d08baec20f799a1817e171a18a850c77`.
The original client-hosted payer and selector are each unique:

| Client-host original function | RVA |
| --- | --- |
| Original recipe payer | `0x3f89e0` |
| Original game stock selector | `0x46fa10` |
| First payer E8 call (return `+5`) | `0x3f8aee` (return `0x3f8af3`) |
| Second payer E8 call (return `+5`) | `0x3f8d02` (return `0x3f8d07`) |
| Original RecipeApply | `0x3f8f30` |

The native experiment now stores **separate, SHA-pinned** server and
client-hosted profiles in [`native_stock_builds.h`](native_stock_builds.h)
and checks executable identity, genuine server-world session, caller E8
branches, selector prologue and original transaction operator.
[`server_live_roster.h`](server_live_roster.h) accepts a real HOST
server-world, never a remote-only client or prediction world.
[`server-stock-live-hook.py`](server-stock-live-hook.py) now selects the
correct original selector RVA and callsites for either exact executable.
These updates were compiled in disposable Windows Kiln integration and
tested with cross-platform native profile regressions.

**Critical additional discovery: the existing released Kiln Types service
already resolves game ECS systems by name.** `kiln.types`
`system_find("cache_crafting_stock")` and `system_info(index)` provide
the original client's update RVA, including build changes, without
guessing an address or running another EXE probe. The native hosted-world
`bind()` now requires that verified system descriptor and records the
entry RVA in `original_client_cache_update_rva` before the stock selector
can arm. This check is performed through `Kiln.Types`, not client
`.rdata` string addresses.

**Still not implemented:** the client program's actual post-update
`PlayerBasesSystemClient` material totals and authorised source
replication. `Kiln.Types` also provides `system_hook` with the game's
real ProgramContext and original execution, but the cache access/write
contract has not yet been verified. No blind memory patch, forced
craft-button enable, user installation or playable build is authorized.
The exact original transaction's remote stock deductions likewise still
require running-game acceptance tests.

## Precise missing native engine interfaces

1. **Server collector:** the current-build function, list layout, stock-mode-2
   branch and original-item/fuel separation are now identified. Still required:
   a safe live detour and server-native callback that obtains other-base
   **game-registered** Magic Chest owners under the same original `InventoryTx`,
   validates actor/base permissions, verifies the linked original 8-slot
   pages, and retains the appended owner list until the original payer returns.
   The actual dedicated-server detour is now present and Windows-CI-compiled,
   but has no independent running-game validation and is disabled in public
   builds; the exact client-hosted selector, caller RVAs and server-world
   session are now integrated and CI-compiled, not yet exercised in game.
2. **Game authorization:** use the game's actual Magic stock component,
   dynamic stock enable state, active altar/base relationship and
   actor/player permission checks. Client inventory/template names and base
   AABB geometry are insufficient. Do not assert authorization without
   matching game evidence.
3. **Client collector:** `Kiln.Types` can now identify the original
   `cache_crafting_stock` update by name and RVA on the live client; the
   remaining work is the **actual cache data access/update contract** for
   `PlayerBasesSystemClient`, including material category choices. The client may display remote counts only when its
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
