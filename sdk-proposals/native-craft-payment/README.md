# Native recipe-payment bridge: implementation begun (server transaction layer)

## Second implementation milestone — native original-transaction source reader

The next native phase has progressed beyond synthetic source lists: the
experimental [`tx-inventory-snapshot.py`](tx-inventory-snapshot.py) applies
a narrow **optional native Kiln.Items v3 ABI tail** to an exact, disposable
published Kiln checkout. Its `read_in_tx` reads the **original game's
borrowed InventoryTx** using the existing guarded
`kitems::access_from_tx`, `read_pages` and `read_slots` accessors, not the
public tick-thread-only `Items.Read`. It verifies the transaction operator
against the actor, server-only world, initialized owner inventory, complete
linked 8-slot pages, packed slot entity/index and nonzero item/quantity
consistency. It returns a **detached copy** of all slot IDs and counts or
fails closed; never writes to a game inventory or extends persistent
`InventorySetup` links.

[`tx-source-native.h`](tx-source-native.h) converts that native ABI output
into concrete owner/link/slot records for the crafting payment bridge.
[`tx-source-provider.h`](tx-source-provider.h) requires a **separate live
game-native permission grant before reading any remote owner's inventory**;
the required proof comprises actual native Magic identity, player access and
Flame Altar/base eligibility, with separate same-base ordering. It rejects
duplicate/ambiguous linked IDs across owners, wrong operator, incomplete
pages, corrupt stacks and invalid source identities. It has no right to
invent a permission from the `_Magic` filename, template, distance, or
`cbstore scan`.

**CI evidence (9 November 2026):**
- [Windows original native service build #42605191013](https://github.com/bisdat/kiln-mods/actions/runs/42605191013)
  built both modified **`kiln-items.dll` and `kiln-rules.dll`** from exact
  published Kiln `v0.2.0-dev.20`, without installing or uploading either.
- [Cross-platform C++ safety #42605199473](https://github.com/bisdat/kiln-mods/actions/runs/42605199473)
  passed **82/82 host cases** (17 planner + 11 native ABI + 18 transaction
  coordinator + 17 original hook + **19 new source access tests**) on GCC,
  Clang and Windows MSVC. Source tests deliberately model lost permission,
  ordinary chest, 8-slot link duplication, forged identity, missing stock
  and incomplete snapshots.

**What is still NOT implemented or verified:** The game-native provider
must enumerate eligible loaded chest owners and establish their actual
Magic/base/player grants. The authoritative selected category/alternative
ingredient cost and water requirements are still not decoded; the Workbench
second ingredient remains `itemId=0, count=0` in the managed recipe
projection. The original recipe's exact abort/rollback return semantics
still require live validation, as does the client craftability interface.
**No permission is automatically true and the real hook's resolver is still
`nullptr`.** There is no active remote item deduction, no playable feature
ZIP, and no new diagnostic-only build. Keep Cross-base Storage 0.1.9
installed with standard Kiln.

## First implementation milestone — 9 November 2026

[**`recipe_payment_bridge.h`**](recipe_payment_bridge.h) is the first
**executable server-side recipe-payment coordinator**, layered on the real
InventoryRemoveItems ABI adapter and existing exact-slot source planner.

It implements three mutually exclusive actions:

- **DelegateVanilla:** the caller must run the original payer unchanged,
  with no remote withdrawals staged (for local-only recipes, missing engine
  proofs, unsupported categories, shortages or denied permissions).
- **PaidInOriginalTx:** all local and eligible remote sources were validated
  by a *native transaction-time* authorisation callback, and the original
  `InventoryRemoveItems` staged the **exact** item, count, slot and operation
  type on the **original** recipe transaction. The original recipe—not this
  coordinator—must own its output and the eventual commit or rollback.
- **AbortOriginalRecipe:** at least one native take produced an error,
  shortage, incorrect journal delta or misdirected slot. Earlier changes
  may be staged, so **the outer recipe must roll back**, and the hook
  MUST NOT fall back to vanilla or report success.

The coordinator makes all seven proofs **mandatory** before any staging:
validated recipe-specific return address, matching operator, proven outer
RecipeApply rollback, decoded full selected ingredient bill (including
category/alternative scaling), original write-tracked ECS context,
authoritative native Magic/base/player permissions and no conflicting
creative-mode payment waiver. Merely seeing `_Magic` or trusting a
`cbstore scan` is *not* proof.

[**`recipe_payment_bridge_tests.cpp`**](recipe_payment_bridge_tests.cpp)
runs 18 synthetic original-transaction end-to-end cases: player-first,
same-base and distant payment order; no double charges; successful output
commit; output failure rollback; denial/shortfall after partial staging;
wrong slot, wrong item and wrong op-type; revoked rights before staging;
unsupported categories; stale stock; multiple ingredients; mismatched
actor and duplicate slot identities.

**Verified CI:** [native payment run #42600744651](https://github.com/bisdat/kiln-mods/actions/runs/42600744651):
**46/46 host tests per compiler** (17 planner + 11 native ABI + 18 coordinator)
passed GCC, Clang, Windows MSVC. The latest released Kiln
`v0.2.0-dev.20` still exposes the original payer and native
remove/rollback seams.

**Release gate remains CLOSED:** Host tests do not establish real
native permission retrieval, actual Workbench category-cost selection,
outer-game rollback semantics, or client UI consistency. The native
service still has no verified authoritative provider satisfying the proof
contract, so no live source can pass the bridge. **No DLL was installed
or deployed**, no new diagnostic-only Cross-base Storage build is
necessary and v0.1.9 remains installed unchanged.

The next source milestone is filling these mandatory callbacks with
engine-authenticated data and binding this executor to BOTH original
recipe payer callsites, while proving failures cause the real
RecipeApply transaction to roll back. Only then can a feature ZIP be
safely offered for isolated gameplay tests.

# Native recipe-payment bridge: isolated safety prototype

**Not installable, not an upstream Kiln API, not working cross-base crafting.**
This proposal sits outside shipped Cross-base Storage. It contains pure
C++20 payment preflight logic and a mock native transaction, with no access
to Enshrouded process memory, inventory, game transactions or saved games.

## Established by the tests

- Exact item input preflight: player's own slots first; confirmed same-base
  Magic Chests second; confirmed distant Magic Chests third.
- Remote sources contribute zero usable stock without separately verified
  loaded, true-Magic, actor permission and base-access proofs. These are
  *inputs* to this isolated planner, not an implementation of those proofs.
  Template-name heuristics do not qualify as authority.
- Malformed and duplicate packed 8-slot inventory IDs are rejected. Repeated
  ingredient rows cannot count the same stock twice. Shortages and unknown
  recipe categories return no proposed takes.
- The native InventoryRemoveItems preferred-slot argument is a hint, NOT
  an exact slot guarantee. Staged native journal operations must match
  the planned real slots, item IDs, item counts, and take op type exactly.
- The helper never commits a transaction. The original native RecipeApply
  must own the only commit and rollback. A short or incorrect staging result
  must abort the whole recipe and roll back any earlier takes.
- Real-world chest owner IDs 1412 and 1081 appear in the host fixtures, but
  the stock and transaction execution are synthetic.

Host tests (both pure planning and current-layout native take adapter):

    for suite in payment_plan_tests native_take_adapter_tests recipe_payment_bridge_tests engine_hook_tests tx-source-provider-tests; do
      g++ -std=c++20 -O2 -Wall -Wextra -Werror -pedantic \
        "sdk-proposals/native-craft-payment/$suite.cpp" -o "/tmp/$suite"
      "/tmp/$suite"
    done

The native adapter `native_take_adapter.h` now binds the actual MS-x64
`InventoryRemoveItems(out, originalTx, owner, item, preferredSlot, count)`
calling convention (through a function pointer supplied by a future
trusted native caller). It reads transaction operator at `+0xe0`,
journal pointer at `+0xf0`, operation count at `+0xf8`, and exact
appended 52-byte take records; these layouts are from the released
Kiln source, NOT invented persistent inventory links. Any unexpected
journal slot/item/quantity/op-type, native error, short pay, missing
transaction data or unexpected second operation returns a failure
requiring the *outer recipe* to roll back. **This prototype neither
owns a game transaction nor automatically rolls anything back.**

[Native payment safety run](https://github.com/bisdat/kiln-mods/actions/runs/42585229144)
passed **28 host tests** (17 pure planner + 11 native-layout adapter)
under Linux GCC, Linux Clang and Windows MSVC on 9 November 2026,
and checked its source seam against Kiln's published dev20 release.

Host test for the original planner only (also works):


    g++ -std=c++20 -O2 -Wall -Wextra -Werror -pedantic \
      sdk-proposals/native-craft-payment/payment_plan_tests.cpp -o /tmp/cbpay
    /tmp/cbpay

CI (experimental): ../../.github/workflows/kiln-native-payment.yml

CI compiles with GCC, Clang and Windows MSVC and checks the actual payer
signature anchors against the latest **published Kiln development release**.
Changes to those anchors cause a failed gate pending native review. None
of these compile checks establish that a live craft will work.

## Integration blockers that still need native implementation

1. Scope BOTH already-installed Kiln Rules payer hooks, InventoryPay and
   RecipePayAlt, to the validated RecipePaid return addresses and matching
   transaction operator. Never intercept unrelated storage upgrades,
   building costs or consumable payments. If local inventory alone pays,
   call the original payer without modification. FreeCrafting is NOT an
   acceptable replacement for real payment.
2. Decode the native payer's actual item/category input costs, selection
   details and multiplier. A recipe category placeholder (itemId 0) or
   raw zero count is **not** a free ingredient. Do not enable any
   unresolved alternative/category recipe.
3. Obtain transaction-time ownership and permissions for eligible Magic
   Chest inventory links, Flame Altar/base access, loaded chunks and
   concurrency. Never use a previous managed snapshot as authoritative
   proof. Never modify persistent InventorySetup links.
4. Use InventoryRemoveItems(out, tx, chestOwner, item, slot, qty) within
   the game RecipeApply transaction; inspect ONLY this call's newly
   appended tx journal operations for exact slot/item/count/type.
5. Prove failure of any partially staged native cost causes the outer
   recipe to roll back all local and remote takes. Never commit inside
   this planner or return "paid" on a shortfall.
6. Add the matching client source-count/recipe-ingredient resolver, then
   test true crafting, multi-craft, station queues, building, multiplayer,
   concurrent actions and save/restart in a backed-up test world.

**Safety decision:** Keep this source-only, not a deployed native plugin.
The next engine-facing work should be read-only native payer-cost and
journal instrumentation on the current released Kiln. A disabled-by-default
real transaction adapter can follow only when the above proof exists.
