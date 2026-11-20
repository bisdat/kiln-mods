# Automated Building Tools upstream updates

Building Tools for Kiln is an **unchanged binary wrapper** for upstream
[Enshrouded Building Tools](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools).
The original author owns the C++ sources and DLL. Our packaging neither recompiles
nor patches that DLL, and never replaces Kiln's game-root `dinput8.dll`.

## What runs automatically

The [upstream watcher](../.github/workflows/buildingtools-upstream-watch.yml)
runs daily, on manual dispatch, and after relevant changes to `main`.

1. Look up the latest **stable official GitHub release** and ensure its ZIP and
   published checksum asset have appeared. When the upload is incomplete the
   watcher reports the condition and retries at the next run, rather than
   publishing an incomplete package.
2. Download only that version's official archive and checksum; verify the
   actual ZIP against the author's SHA256 (legacy per-archive and SHA256SUMS
   formats). If a pinned version's bytes change, **fail closed**.
3. If a newer version is available, increment the Kiln adapter patch version
   and update `BuildingToolsKiln/release.json`. Preserve the previous
   gameplay-verified version, notes and tested game-client checksum verbatim.
   An already-pinned version with no published release (e.g. 0.34.0 / 1.0.2
   at the start of this workflow) is also eligible for packaging.
4. Run the entire regression suite, package the original DLL byte-for-byte,
   verify licence, translations, native manifest, safe archive paths,
   official and pinned checksums, and reproducibility. Generate the install
   ZIP and its SHA256. **No game is launched by GitHub Actions.**
5. Only after passing those checks, fast-forward the new upstream pin to
   `main` (if changed), create a namespaced immutable version tag and
   publish an **automated prerelease** with the install ZIP and SHA256.
   A concurrent move of `main` aborts the publish; the run must be retried
   against the new HEAD. An existing release is never overwritten.
6. The GitHub Actions bot's own commits and tag pushes do not launch new
   downstream workflows. The watcher therefore performs package, tag and
   prerelease publication **within one workflow run**.

`BuildingToolsKiln/package.py` validates the archive and builds the
wrapper. `propose_update.py` maintains version/checksum pinning.
The separate [package workflow](../.github/workflows/buildingtools-kiln-native-adapter.yml)
continues compiling and testing PRs, but never independently publishes releases.

## Release status and gameplay testing

**No version-specific owner approval is required for an automated prerelease.**
Every new upstream binary is published with a visible
**"CI VERIFIED — GAMEPLAY UNTESTED"** warning. This is a build result,
not approval of changes to game inventory or crafting logic.

The `gameplay_verified_upstream_version`, `tested_client_sha256`, and
`gameplay_acceptance_note` fields record actual gameplay evidence.
They are **not** advanced by automation. After a new prerelease is tested
in a disposable Enshrouded world, record the test against that exact
upstream version separately. Do not promote a prerelease to stable
without evidence of real-game acceptance.

Recommended in-game checks: Kiln native loader reports the adapter loaded;
Building Tools menus work; Magic Chests at another Flame Altar are visible;
a craft consumes remote materials exactly once; save/reload preserves the
balances; logs contain no unsupported game signatures. These cannot be
established from CI alone. Back up saves before testing a prerelease.

## Failure and recovery

- **No uploaded assets yet:** retry on the next scheduled run, no release.
- **Missing/mismatched official checksum, invalid ZIP/DLL/licence, bad test
  or nonreproducible output:** fail without modifying `main` or publishing.
- **Competing `main` update:** fail before tagging. A later run retries.
- **GitHub bot lacks permission to push `main`/tags:** fail rather than
  publishing a package without its pinned source commit. Fix repository
  workflow permissions; do not bypass integrity checks.
- **Publication fails after a successful pin commit/tag:** next run sees the
  unchanged pin but missing release and retries the same version, without
  overwriting tags or previously published assets.
- **Already published:** leave the immutable release and assets unchanged.

No gameplay mod is installed on the user's computer by these workflows.

## Manual action

To request an immediate check rather than the daily schedule, open
[Check and publish Building Tools for Kiln](https://github.com/bisdat/kiln-mods/actions/workflows/buildingtools-upstream-watch.yml)
and choose **Run workflow**.

The watcher chooses the latest official stable upstream release. It does not
follow untagged developer builds, arbitrary Nexus uploads or unknown binaries.
Editing the original C++ code would be a separate fork of the upstream project.
