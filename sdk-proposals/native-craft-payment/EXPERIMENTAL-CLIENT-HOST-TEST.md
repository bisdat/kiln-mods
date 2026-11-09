# Cross-base Storage — native single-player candidate (experimental)

This is a **functional integration candidate**, not the passive settlement
observer and **not a public mod release**. It compiles a temporary,
SHA256-pinned native client-hosted original stock selector and the game's
original client crafting-stock iterator. It is not proven in a running game.

## Required exact versions

- Enshrouded **client 26430625**, SHA256
  `5d9ef2f140b8ed49e30dbee655841b89d08baec20f799a1817e171a18a850c77`;
  the user's read-only executable mapping established its native payer and
  stock selector on 9 November 2026.
- Kiln **v0.2.0-dev.20** exactly, source
  `d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63`.
- Existing Cross-base Storage **0.1.9** is allowed to remain installed; it
  does not itself debit anything remotely.
- Test only a **single-player HOST** world with one authenticated player;
  neither joined-client multiplayer nor a dedicated server is within this
  candidate's acceptance scope. Use a disposable copy of your game save.

## Safe installation in Linux Steam/Proton

The attached ZIP contains **both** `kiln-items.dll` and `kiln-rules.dll`
because `kiln-items.dll` has an experimental native-only original-transaction
inventory snapshot ABI. These files **replace**, not accompany, the matching
`kiln/services` DLLs for this test only. Never mix one test DLL with a normal
DLL from a different build.

1. Exit Enshrouded completely. **Back up your whole save/world first**, and
   make a disposable test copy that cannot overwrite your regular save.
2. Verify that the installed EXE SHA256 and Kiln version match the exact
   requirements above. If they differ, STOP.
3. Locate each installed `kiln-items.dll` and `kiln-rules.dll` under the
   game's `kiln` tree. Back up **both originals to a separate directory**.
4. Replace only those two exact locations with the two matching DLLs inside
   this test ZIP. Do not replace `dinput8.dll`, `kiln-core.dll`, or another
   mod. Disable conflicting mods that detour the original chest selector,
   client stock iterator, and free-crafting payment for this test.
5. In the disposable world, place at least **one Magic Chest at Base B**
   (target base); it can be empty. At Base A place another Magic Chest with
   all materials for a **fixed-item, normal workbench recipe**, while none
   exist in the backpack or any Base B chest. Stand at the workbench in B.
6. Verify the normal vanilla crafting menu now counts Base A ingredients,
   shows the recipe craftable, and allows one craft. Check that the output is
   created **once**, the remote stack is deducted **exactly once**, and Base B
   and backpack material counts have not been charged. Save and reload the
   **disposable** world and check inventory conservation.
7. Also verify a normal local craft, a recipe without enough materials and
   an output-full refusal if available. Stop testing on crashes, missing
   ingredients, duplicated items, unexpected deductions, UI-only craftability
   or error messages; preserve relevant `kiln.log` and notes.
8. Exit Enshrouded, restore **both** original DLLs, and only then return to
   your normal saved world.

**Current deliberately closed gate:** a target base with **zero local Magic
Chests** is not accepted as a success case. The vanilla crafting menu may
select backpack-only stock mode there. The external reference implements a
client-only one-chest alias, but we leave that code inactive until the matching
server-side empty-base payer has been validated. This is not a fake enablement
test. First test with an *empty* Magic Chest in Base B.

**Evidence of success:** recipe row and Craft action reflect the genuine
remote materials; the original game payer conserves actual stock and output
through save/reload. Merely seeing the button enabled is a failure.

## Engineering provenance

- Independently verified actual original game client selector and two payer
  calls from the user's read-only `enshrouded-client-stock-map.json`.
- Client cache integration uses the game's original
  `cache_crafting_stock` and stock iterator; no direct inventory mutations
  or invented recipe quantities.
- Algorithmic guidance from Aleksander Wojtal,
  [Enshrouded Building Tools](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools),
  MIT licence (c) 2026. In particular: the six-argument original chest-list
  selector, the client stock iterator's 40-chest chunks, and the empty-local
  menu stock-order edge case.
- All runtime hooks are hash, source-code signature, original-caller and
  authoritative-world gated. If a gate fails, **no cross-base gameplay
  functionality is enabled**; the original client stock is unchanged.
- The game and mod have not been exercised in live runtime by CI; GitHub
  Actions compilation is not gameplay verification.

No files from Enshrouded itself, or any user save, are included in this ZIP.
