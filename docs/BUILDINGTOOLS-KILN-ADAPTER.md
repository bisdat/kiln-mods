# Building Tools → Kiln native adapter (experimental)

## Why the original ZIP was not recognised

[Enshrouded Building Tools](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools)
v0.31.2 is an **independent Windows native proxy DLL**, originally named
`dinput8.dll`. It is not a `kiln.mod.json` managed mod, an EML data mod or
a Shroudtopia plugin. Kiln's `dinput8.dll` is already the game's loader.
Overwriting it with Building Tools would remove/replace Kiln.

Released Kiln v0.2.0-dev.20 **does** support unmodified ordinary DLLs through
`kiln/native/<id>/native.json` (see upstream
[native mods documentation](https://github.com/frostdev-ops/kiln/blob/d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63/docs/kiln/native-mods.md)).
It calls `LoadLibrary` for `dlls[]`. Building Tools uses its
`DllMainCRTStartup` to start a delayed game-signature scanner and does not
require its filename to be `dinput8.dll` once it is loaded.

**Therefore a non-invasive native drop-in wrapper is viable at the packaging
level, although interaction with Kiln's existing hooks still needs game
testing.** No changes to Kiln's main loader or the original Building Tools
binary are necessary.

## Adapter provided on PR #7

[`tools/package-buildingtools-for-kiln.py`](../tools/package-buildingtools-for-kiln.py)
takes the unmodified [official MIT-licensed release](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools/releases/tag/v0.31.2)
and its official `.zip.sha256`. It rejects unexpected archive entries and
mismatched checksums, and produces:

```text
kiln/
  native/
    building-tools/
      native.json
      BuildingTools.dll           # exact upstream dinput8.dll bytes, RENAMED
      BuildingTools-LICENSE.txt   # unchanged MIT licence
      BuildingTools-README.txt
      ADAPTER-README.txt
      provenance.json
BuildingTools-Languages/
  *.txt                           # unchanged upstream translations
```

`native.json` identifies the mod as **building-tools**, client side,
phase **boot**, loaded as a plain native DLL (no Kiln-aware init export). Its
provenance tracks upstream ZIP SHA256 and original DLL SHA256.

The stock game and Kiln files are **not overwritten**, and the adapter is
distinct from our draft Cross-base Storage implementation.

The native-DLL package is built and structurally checked by
[`buildingtools-kiln-native-adapter.yml`](../.github/workflows/buildingtools-kiln-native-adapter.yml).
The GitHub Actions artifact contains a ZIP; extract the **inner**
`BuildingTools-Kiln-native-0.31.2-EXPERIMENTAL.zip` into the existing
Enshrouded game root after taking backups.

**Do not put the upstream ZIP in `kiln/mods/`**. It has neither the
`kiln.mod.json` required there nor the `native.json` this adapter adds.
In launcher profile mode, ensure the profile includes **native drop-ins**;
otherwise the `kiln/native` folder is intentionally skipped.

## Isolated game compatibility check

1. Exit the game. Make a separate disposable copy of your Enshrouded world.
   Keep the original installed Kiln `dinput8.dll`. Do not install the
   unfinished experimental `kiln-rules.dll` /
   `kiln-items.dll` from PR #7 alongside Building Tools.
2. Extract only the adapted ZIP to the game root. Start the game.
3. Check `kiln/state/native.json` for the ID `building-tools` with
   `state: loaded`. Check the **game-root** `BuildingTools.log` for the
   mod's own `ready` message. Kiln recognition alone does **not** prove
   Building Tools' signature scan or game hooks succeeded.
4. In the game-root `BuildingTools.ini`, set the optional configuration:
   ```ini
   [Chests]
   GlobalMagicChests=1
   ```
   Quit and restart after saving the INI. Leave unrelated Building Tools
   features unchanged; this test is cross-base crafting only.
5. Test a vanilla recipe whose inputs exist **only** in a Magic Chest at
   another Flame Altar, with an empty local Magic Chest and an empty backpack.
   Confirm the normal crafting menu, output once, remote deduction once,
   and quantities again after save/reload of the disposable world.
6. Also test an insufficient-material refusal, local recipe, and whether
   loot deposits still remain local. Capture `kiln/logs/kiln.log`,
   `kiln/state/native.json`, and `BuildingTools.log` on failures.
7. Uninstall the adapter (delete just `kiln/native/building-tools`) and
   restore your original world when done. Do **not** delete/overwrite
   Kiln's game-root `dinput8.dll`.

### Limitations / current test status

- Packaging **is not gameplay validation**. Native Windows DLL loading,
  delayed hooks, and conflicts with Kiln's native crafting hooks have
  **not** been tested under the user's Proton setup. Test on a disposable
  save only.
- The official Building Tools binary includes its whole Construction
  Hammer feature set, not just cross-base storage. This temporary adapter
  should not become the final Cross-base Storage mod.
- Upstream's own documentation notes that distant unloaded chests may
  not count until the game has loaded their area.
- If the game changes, the upstream mod's signature checker may refuse
  to activate. That is expected safety behaviour; never force stale RVAs.

## Adaptation into Cross-base Storage

MIT licence: copyright (c) 2026 Aleksander Wojtal. If derived source is
copied into our own mod, retain the upstream copyright and MIT notice.
The native source in PR #7 is already being refactored using these
independent, source-grounded original-game interfaces:

- Original six-argument chest-list selector (includes player/position)
- Client `cache_crafting_stock` original iterator and per-base chest IDs
- Vanilla backpack/chest-stock-mode selection, including empty-base case
- Server payer transaction and original payment/rollback path

Our final deliverable must be a **separate, Kiln-native Cross-base Storage**
implementation with the shared-chests option and **no** general Building
Tools keybindings, renderer or Construction Hammer hooks. It must not install
another `dinput8.dll` or replace Kiln's core service DLLs.

PR #7 remains **draft** until we have runtime proof of craftability,
exactly-once remote debit, output and save/reload conservation.
