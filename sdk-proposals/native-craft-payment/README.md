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

    for suite in payment_plan_tests native_take_adapter_tests; do
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
