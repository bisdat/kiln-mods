# Experimental Kiln crafting transaction / client gate trace

**Research source only. Not a playable Cross-base Storage release.** This proposal adds a passive observer to an isolated copy of Kiln's existing native `kiln.rules` service. It does not implement, hook separately, bypass or modify vanilla crafting payments or UI availability. The installed Cross-base Storage **0.1.5** mod and the user's Enshrouded game are unchanged.

## Why this particular seam

Current [Kiln main commit `77e5258`](https://github.com/frostdev-ops/kiln/tree/77e525888ac0d5f4f6b16103e945ed7ac049b50e) implements actual free crafting in `native/services/rules/creative.cpp` and `rules.cpp` by intercepting two server **recipe payment functions** and one client **`UiCraftableAmount`** function. The existing `kiln.rules` hooks already occupy those native detours, so installing competing hooks from a separate mod would risk interference. Extending them in a local upstream-source patch is the least intrusive prototype.

We do **not** want its existing free-crafting behavior (returning `0` to waive payment). A real cross-base source resolver must instead enroll authorized distant Magic Chest slots inside **the same engine-owned inventory transaction** while retaining all material counts and producing exactly one output.

The live game test from 9 November 2026 at 05:26 UTC proved only **read-only** source allocation: 3 String could be quoted from Magic Chest entity 1412 while the player carried 0 String; it did **not** cause a craft or a debit.

## What this proposal implements

- `native.py` applies a pinned-source patch at exactly commit `77e525888ac0d5f4f6b16103e945ed7ac049b50e` of `frostdev-ops/kiln`; other revisions fail closed.
- Reuses the native `kiln.rules` functions already hooked for free crafting. Records the result code of **original** `InventoryPay` or **alternative** `RecipePayAlt` calls when the existing *verified recipe-specific* call-site return address matches. Does not intercept other payments or override the return code.
- Reuses the existing `UiCraftableAmount` client detour. Records the recipe ID, original vanilla craftable amount, and requested cap before any existing free-crafting rule could raise it.
- Appends a single **optional** `craft_trace` entry to the end of the public `kiln.rules` v1 table. The result is a 64-byte struct of process-local counters and last *scalar* samples. It does not contain raw pointers, recipe ingredient bytes, chest ownership or inventory data; the last scalar fields can originate from different worker-thread calls.
- `managed.py` adds `Rules.TraceCraftPayments()` with an ABI-size gate. This returns `null` on any older/unmodified runtime.
- `verify.py` checks the exact six source files changed, preservation of original native payer calls and results, no new detours, and no direct inventory mutations.
- CI builds managed `Kiln.Rules` on Linux, verifies 64-byte ABI size, and compiles native Windows x64 `kiln-rules.dll` in an isolated checkout. **No artifacts containing patched DLLs are published for gameplay installation.**

The [GitHub Actions build](https://github.com/bisdat/kiln-mods/actions/runs/42568359006) passed both Linux managed/C ABI checks and the native Windows x64 `kiln-rules.dll` compilation. To build this older main revision under MSVC, the Windows job applies [`msvc-compat.py`](msvc-compat.py) **after** the feature-only safety validator. That adapter fixes a pre-existing `reinterpret_cast<uintptr_t>(uintptr_t)` template issue in three upstream symbol helpers; it is not a game-feature change or part of the user-installed mod.

The optional entry can indicate the stage of a **normal** craft:
1. Client calls the UI gate with original craftable count `0` and cap `1`: possible client-side rejection, but not proof why the count is zero.
2. The server receives an actual native craft request and calls the primary or alternative payment routine: the engine has reached its original payment boundary.
3. The original payer returns success or failure. This still does **not** prove remote Magic Chest inclusion; actual source slots and engine journal entries require a *different*, carefully scoped integration.

These are process-level diagnostic counters, not an auditable per-player trail. A hosting client may run both client and server worlds in one process; dedicated servers do not have client UI calls.

## Reproduce (developer CI, no game required)

```bash
git init .kiln-trace
git -C .kiln-trace remote add origin https://github.com/frostdev-ops/kiln.git
git -C .kiln-trace fetch --depth=1 origin 77e525888ac0d5f4f6b16103e945ed7ac049b50e
git -C .kiln-trace checkout --detach 77e525888ac0d5f4f6b16103e945ed7ac049b50e

python3 sdk-proposals/native-craft-trace/native.py .kiln-trace
python3 sdk-proposals/native-craft-trace/managed.py .kiln-trace
python3 sdk-proposals/native-craft-trace/verify.py .kiln-trace

dotnet build .kiln-trace/managed/services/Kiln.Rules/Kiln.Rules.csproj --configuration Release
```

Windows CI also configures the upstream `native/CMakeLists.txt` and builds target `kiln-rules` for x64 with Visual Studio. **Both scripts patch only the exact commit and may be run once per clean checkout.**

## What must happen before actual cross-base crafting

1. Identify and validate the authoritative **Magic Chest flag, Flame Altar/base membership, remote inventory owner/slot mapping**. Template-name matching used by 0.1.5 is only a heuristic.
2. Implement **typed direct-item and category ingredient resolution**, including `inputItemCategory.count` and water charges, from the engine's authoritative registry.
3. Find where original **server** recipe and build payers collect eligible source inventories and queue same-transaction debits; extend that source selection rather than skipping payment or calling `Items.Take` after output.
4. The original **client** craftability and ingredient-rows/build preview must reflect only those same eligible sources; an unconditional `return cap` is wrong.
5. Validate multiplayer double-spend, transaction failure/rollback, world saving/loading, unloaded areas, permission changes, station queues and game build mismatch. All hooks should turn off on unknown symbols/ABI.
6. Only then expose a controlled mod feature and run opt-in real-game tests using backups.

This trace moves toward step 3 by confirming the *actual call paths*, but does **not** implement the payment source resolver itself.

See [engineering design](../../CrossBaseStorage/ENGINE-BRIDGE.md) and [GitHub issue #6](https://github.com/bisdat/kiln-mods/issues/6).


## Follow-up: bounded original recipe-payment journal observation (9 November 2026)

The companion [journal.py](journal.py) patches the **already-patched disposable**
Kiln source from this proposal. It adds another optional ABI-tail entry,
`journal_trace`, and a nullable managed `Rules.TraceRecipeJournal()` API.
This does not run in the shipping mod and does not change any game's payments.

The observer brackets the original primary and alternative recipe payer calls
at their **validated recipe-specific return addresses**, after normal
free-crafting/upgrade bypass conditions. It reads the transaction's **op count
before/after** the original payer returns, then examines at most **128 newly
staged 52-byte inventory operations**. It records at most **eight distinct
underlying inventory-slot entity IDs** and the sum of staged take units per ID,
without persisting raw transaction pointers, recipe costs, item bytes, or slot
addresses. It never adds inventory links, invokes a transaction, or performs
any material withdrawal.

**Important distinction:** these operations are *staged in the engine-owned
journal*, not proof of commit or durable payment. The result can be discarded
or rolled back later. An inventory entity ID is a **linked 8-slot inventory**,
**not** necessarily the Magic Chest owner; matching it to a chest requires a
separate authoritative owner→InventorySetup-links mapping.

The native reader requires resolved `items/tx.ops=0xF0`,
`items/tx.ops_inline=0x118`, and `items/tx.operator=0xE0`, and only enables
on the **server**. Any missing/mismatched signature disables the inspection.
Malformed records, impossible counts or unreadable memory produce
`status=2` with no source sample. More than eight source IDs produce
`status=3` (sample **truncated**, never a complete source bill).
The native entry is appended after the existing 64-byte craft trace; the
new value ABI is 112 bytes. Older Kiln instances return `null` in the managed
API. The observer is intentionally **not installed in Enshrouded**, and its
native behavior has not been game-tested.

The status values are: `0` not observed, `1` complete staged sample,
`2` invalid/untrusted sample, `3` truncated sample. The returned
`PayerKind` is 1 (primary) or 2 (alternative); `Owner` and
`InventoryEntity` are current-process entity IDs only.
`Calls` and `Rejected` are process-local counters.

**CI route (source checks only):** after `native.py` and `managed.py`,
run `journal.py`, then `verify.py` and the existing managed/native
build steps. No patched DLL is uploaded or released.

The next prerequisite for an actual cross-base provider is an **authoritative
mapping from these linked inventory entities to their container owners, base
membership and Magic Chest entitlement**. Only after that can a carefully
scoped native extension investigate enrolling eligible sources in the
*original* transaction, with the client using the same eligibility rules.
