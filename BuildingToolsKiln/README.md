# Building Tools for Kiln

**Native client-side adapter for official Enshrouded Building Tools.**

The newest wrapper is published as a **CI-verified, gameplay-untested prerelease**;
see [GitHub Releases](https://github.com/bisdat/kiln-mods/releases) for its
current adapter and upstream versions. The last separately gameplay-verified
upstream release is **0.33.2** (adapter **1.0.1**). Automated version pins
are recorded on the `automation/building-tools-release-pins` branch while
`main` remains protected.

[Releases](https://github.com/bisdat/kiln-mods/releases) |
[Original Building Tools](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools) |
[Packaging workflow](../.github/workflows/buildingtools-kiln-native-adapter.yml)

Building Tools is MIT-licensed software by **Aleksander Wojtal**.
This package loads its official, **unchanged** DLL through Kiln's native
mod loader. It does not install or replace the game-root dinput8.dll,
which is Kiln's own loader. This is an interoperability wrapper, **not
a rewrite of Building Tools or a new crafting implementation**.

## Where is the source?

**This directory is the complete source for our Kiln adapter.**
[`package.py`](package.py) is our packaging code,
[`release.json`](release.json) pins the exact upstream version and checksum,
and [`tests/`](tests/) checks the package.
The original game's C++ implementation is **not copied or forked** into
this repository: its source is the upstream developer's
[`src/` directory](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools/tree/main/src).

Nexus releases are generated from the upstream
[GitHub release process](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools/blob/main/docs/NEXUS.md).
We don't merge the developer's source into this repo: updating means
repackaging a new official upstream release and retaining the original
DLL unchanged. See [`UPSTREAM.md`](UPSTREAM.md) for the controlled update workflow.

## Confirmed in the game

**17 November 2026 — PASS:** the user reported adapter v1.0.1 with Building Tools
upstream v0.33.2 tested and working in-game. Specific ingredient-deduction counts
and save/reload conservation checks were not separately reported.

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
2. Download the **version-matched** `building-tools-kiln-X.Y.Z.zip` from the matching GitHub
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

Version and source pin: [release.json](release.json).

The packaging script:

- downloads nothing itself; CI fetches only the **latest official stable upstream release ZIP and published checksum**;
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

    python3 -m unittest discover -s BuildingToolsKiln/tests -p 'test_package.py' -v

The workflow verifies the original official DLL equals the repackaged
DLL, checks deterministic ZIP output and publishes an installable Actions
artifact. **The daily upstream watcher now performs an end-to-end automated build:**
it fetches the latest official stable version, validates its checksum,
increments the wrapper's patch version, runs the tests and deterministic build,
then commits the pinned version and publishes a GitHub **prerelease** with ZIP
and SHA256. No individual owner approval is required for a prerelease.
Previously published releases are immutable. The packaging workflow for pull
requests remains independent and never publishes. GitHub Actions does not
run Enshrouded, so the automatically published version is explicitly marked
**gameplay untested** until a real in-game acceptance test is performed.
See [UPSTREAM.md](UPSTREAM.md) for the complete automation and recovery policy.

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
