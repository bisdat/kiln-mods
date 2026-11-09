# Building Tools for Kiln

**Kiln adapter 1.0.0 · upstream Building Tools 0.31.2 · native client-side mod**

[Releases](https://github.com/bisdat/kiln-mods/releases) |
[Original Building Tools](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools) |
[Packaging workflow](../.github/workflows/buildingtools-kiln-native-adapter.yml)

Building Tools is MIT-licensed software by **Aleksander Wojtal**.
This package loads its official, **unchanged** DLL through Kiln's native
mod loader. It does not install or replace the game-root dinput8.dll,
which is Kiln's own loader. This is an interoperability wrapper, **not
a rewrite of Building Tools or a new crafting implementation**.

## Confirmed in the game

On 9 November 2026 the user confirmed that the Building Tools menus
and **Magic Chests from all bases** work when this adapter is loaded
under Kiln on Linux Steam/Proton. The tested client executable is
Enshrouded build 26430625, SHA256:

    5d9ef2f140b8ed49e30dbee655841b89d08baec20f799a1817e171a18a850c77

**This does not independently verify** single-ingredient debit
counts, rollback, save/reload conservation, future Enshrouded versions
or multiplayer. The original mod has game-signature gates and may disable
some functionality after an update. Always inspect the runtime log.

## Install and upgrade

1. Exit Enshrouded. Back up your saves before installing or updating mods.
2. Download **building-tools-kiln-1.0.0.zip** from the matching GitHub
   Release. The GitHub Actions artifact contains an outer ZIP; when using
   an Actions artifact, extract the **inner** install ZIP.
3. Extract the install ZIP into the Enshrouded **game root**, alongside
   enshrouded.exe. Its files land in these locations:

       kiln/native/building-tools/native.json
       kiln/native/building-tools/BuildingTools.dll
       kiln/native/building-tools/BuildingTools-LICENSE.txt
       kiln/native/building-tools/BuildingTools-README.txt
       kiln/native/building-tools/ADAPTER-README.txt
       kiln/native/building-tools/provenance.json
       BuildingTools-Languages/*.txt

   **Do not replace or delete the game-root dinput8.dll.**
   On upgrades, the same native adapter folder is overwritten safely.
   Your existing BuildingTools.ini is **not included** in the package
   and is therefore **preserved**.
4. When using a Kiln launcher profile, allow **native drop-ins**.
   Start the game; verify id **building-tools** with state **loaded**
   in kiln/state/native.json, then check game-root BuildingTools.log
   for the mod's own **ready** status.

## Enable Magic Chests across Flame Altars

**In-game:** press **Home → Options → Magic chests of all bases → On**.

**Configuration alternative:** exit the game, edit the existing
game-root BuildingTools.ini and restart:

    [Chests]
    GlobalMagicChests=1

This feature is **off by default** in the original mod. Pressing **F5**
turns all Building Tools functionality off and on, including remote
Magic Chest access. The intended material priority is **backpack,
current-base Magic Chests, then other-base Magic Chests**. Loot
deposit remains local. The game may not know about inventory in a
far-away region until you visit/load it.

## Operational and compatibility notes

- Do **not** install the archived experimental Cross-base Storage
  kiln-rules.dll and kiln-items.dll replacements. They intercept some
  of the same crafting engine functions and were not gameplay-verified.
- The older Cross-base Storage 0.1.9 managed plugin was diagnostic-only;
  it is not required by this adapter. To avoid duplicate and confusing
  diagnostics you may uninstall it, but the wrapper does not depend on it.
- The adapter also makes **Building Tools' broader Construction Hammer
  features** available; only cross-base Magic Chests have been tested
  in this particular Kiln + Proton setup.
- On a newer game build, Kiln may show an untested-build note and
  Building Tools may report an unsupported signature. Do **not**
  suppress safety checks or reuse outdated engine offsets.
- To remove the adapter: exit Enshrouded and remove only
  kiln/native/building-tools/. Leave Kiln's original dinput8.dll alone.
  You may retain your BuildingTools.ini and languages for a later install.
- Failure investigation: kiln/state/native.json tells you whether Kiln
  discovered the DLL; BuildingTools.log tells you whether the **original
  game-specific hooks** became active. A loaded DLL alone is not proof
  that distant materials can be consumed.
- For new crafting functionality, verify one ordinary recipe with
  ingredients exclusively in a remote Magic Chest, output once,
  ingredient debit once, and saved quantities after reload.

## Reproducible production packaging

Version and source pin: [tools/buildingtools-release.json](../tools/buildingtools-release.json).

The packaging script:

- downloads nothing itself; the build workflow fetches only the
  **official upstream v0.31.2 release ZIP and published checksum**;
- requires both that checksum and the separately **repository-pinned
  SHA256** of the exact upstream archive;
- checks the PE x86-64 DLL, MIT licence, allowed archive members
  and dangerous paths before copying any bytes;
- packages the upstream DLL byte-for-byte under
  kiln/native/building-tools/BuildingTools.dll, with a native.json
  manifest, upstream licence, README, translations and provenance;
- writes a deterministic ZIP and matching SHA256; it does not
  touch any user-installed game file or world.

Run safety tests without game binaries using:

    python3 -m unittest discover -s tests -p 'test_buildingtools_adapter.py' -v

The workflow verifies the original official DLL equals the repackaged
DLL, checks deterministic ZIP output and publishes an installable Actions
artifact. A matching **building-tools-kiln-v1.0.0** tag can publish a
namespaced GitHub Release with the ZIP and SHA256 after review.
Version changes require explicit upstream pin updates and new tests;
the wrapper **never silently upgrades Building Tools**.

**Licence and credits:** original software copyright © 2026
Aleksander Wojtal, MIT licence. The original licence and README are
copied without alteration into every package. This project is not
the upstream Building Tools maintainer.

## Archived earlier work

The retired custom Cross-base Storage diagnostics, native crafting
experiments, research files and CI have been preserved at the immutable
snapshot commit **78e129d201f2e5e54b86c861a4bdfc0ac4adac4d**
in [archive/cross-base-storage-native-research-2026-11-09](https://github.com/bisdat/kiln-mods/tree/archive/cross-base-storage-native-research-2026-11-09).
That branch is retained as reference material only.
