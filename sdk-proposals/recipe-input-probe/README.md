# Experimental Kiln recipe-input probe

**Status: SDK source experiment, not an installable mod, not a cross-base crafting solution.** The existing Cross-base Storage **0.1.4** ZIP is unchanged. Do not copy the resulting DLLs into an installed Kiln instance or ask users to do so before a full native build, ABI review and controlled game test.

## Why this exists

At Base B, `cbstore plan 0x23c98255 1` could read 716 String in distant Magic Chests for a Workbench recipe that needs 3 String, but the native recipe's second input appeared as `itemId=0, count=0`. The pinned Kiln SDK's `Gameplay.Recipe` copies only an input's item ID at offset `+0x10` and direct count at `+0x14` from a 48-byte native `RecipeInfo` input record.

[ShroudForge 1.7.9](https://github.com/bonsaibauer/shroudforge/tree/1.7.9/mods/sf-crafting-essentials) separately exposes `RecipeRegistryResource.recipes[].input[].itemStack.count`, **`inputItemCategory.count`**, and `waterCharges` when changing recipe cost data. This supports adding a typed Kiln category/input API, **but does not prove native offsets for the missing fields**.

## Implemented research-only probe

This pair of anchor-guarded source scripts extends **a disposable checkout of the exact known Kiln commit** `eba9a5e1e0e0044a9146fdd53021a751e2957030`:

- `native.py`: adds `KilnGameplayRawRecipe` to the C ABI (size 400 bytes), and appends an optional, tick-thread-only `recipe_raw_inputs` function to the **end** of the existing `kiln.gameplay` v1 function table. It uses the existing game recipe lookup and SEH guard, copies up to eight **48-byte input records**, and returns **no native engine-owned pointers**. Existing API members and the regular recipe reader remain unchanged.
- `managed.py`: adds matching `Gameplay.RawRecipe(uint id)` returning detached byte copies and an API-size check. If the native runtime lacks the optional function, the managed method fails closed with `NotSupportedException`.
- [CI](../../.github/workflows/kiln-recipe-probe.yml) verifies exact source anchors, `git diff --check`, C ABI struct size, and the managed service compile. **A matching Windows x64 native `kiln-gameplay.dll` build now passes** in the separate Windows CI job, alongside the managed build and C ABI size check; [successful run](https://github.com/bisdat/kiln-mods/actions/runs/42527820064). **No in-game integration, native category field semantics or atomic crafting behavior has been tested.**

Because the bytes are **opaque**, this API cannot decide whether an input is category-based, what category it names, how many items are required after scaling/rounding, or whether water is consumed. Do not use the raw bytes to add, remove, pay for, or waive items. Do not publish raw memory snapshots; a native input record may contain internal addresses or implementation details.

## Reproduce the build checks locally

From this repository's root, on a developer machine with Git, Python, .NET 10 and a C++ compiler:

```bash
git clone https://github.com/frostdev-ops/kiln.git .kiln-probe
git -C .kiln-probe fetch --depth=1 origin eba9a5e1e0e0044a9146fdd53021a751e2957030
git -C .kiln-probe checkout --detach eba9a5e1e0e0044a9146fdd53021a751e2957030
python3 sdk-proposals/recipe-input-probe/native.py .kiln-probe
python3 sdk-proposals/recipe-input-probe/managed.py .kiln-probe
git -C .kiln-probe diff --check
dotnet build .kiln-probe/managed/services/Kiln.Gameplay/Kiln.Gameplay.csproj -c Release
```

Both scripts **refuse any other Kiln revision**. Apply them **once** per clean checkout. The exact commit is required because native game layouts and public table tails can change.

## What makes it a proper public Kiln feature

1. Independently establish the native recipe input layout (including category GUID or ID, category count, scaling/rounding rules, and water charges) on a **known game build**; avoid guessing offsets from adjacent fields.
2. Export **typed** ingredient descriptors, including explicit `unknown` when fields cannot be validated. Preserve `Gameplay.Recipe`'s original signature/behavior and backward compatibility.
3. Test the known Workbench recipe's full ingredients against a controlled, untouched vanilla world, including other recipes with category and water requirements.
4. Build native and managed services for supported targets and verify client/server ABI/runtime compatibility. Do not install this source experiment in place of a release without that validation.
5. Separately extend native client material availability and server atomic source selection/consumption for the **actual** Cross-base Storage feature. Recipe introspection alone does not unlock crafting.

See [the overall bridge design](../../CrossBaseStorage/ENGINE-BRIDGE.md). Upstream `frostdev-ops/kiln` is **read-only** for the connected GitHub account, so this currently lives as a source proposal in our draft PR rather than an upstream pull request.
