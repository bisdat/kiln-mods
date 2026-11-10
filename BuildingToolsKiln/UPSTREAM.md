# Upstream updates: Nexus → GitHub → Kiln

**Our source is in this directory; the original game's C++ source is not.**
The [upstream developer](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools)
maintains the original Building Tools source. Their
[Nexus publishing notes](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools/blob/main/docs/NEXUS.md)
confirm that new Nexus versions are built and uploaded from the official
GitHub release process.

We do **not** maintain a fork or modify their native DLL. Therefore there
are no upstream C++ commits to merge into this wrapper. An update means
repackaging a newer **official GitHub release** using its published ZIP
and SHA256, retaining the licence and keeping Kiln's loader intact.

## Project files

- [package.py](package.py): Kiln adapter source (copies upstream DLL
  byte-for-byte, generates a native.json manifest and retains translations).
- [release.json](release.json): upstream version, asset SHA256, adapter
  version, last known working game build and **gameplay acceptance gate**.
- [propose_update.py](propose_update.py): propose the next upstream binary.
- [tests/](tests/): packaging and version proposal regression tests.
- [CI](../.github/workflows/buildingtools-kiln-native-adapter.yml):
  verifies official checksum, packages and signs the release with SHA256.
- [Upstream watcher](../.github/workflows/buildingtools-upstream-watch.yml):
  checks the upstream release automatically each day or on demand.

## Controlled update process

1. The upstream author publishes an official GitHub release (normally
   followed by a Nexus upload).
2. The upstream watcher checks for a **newer non-prerelease version**,
   downloads the official ZIP and its published SHA256, and proposes
   a new version in release.json via a **draft pull request**.
   It increments the Kiln adapter patch version without copying any source.
3. CI builds a candidate Kiln ZIP from the actual upstream binary, verifies
   its bytes and licence, tests archive safety, and checks reproducibility.
4. Test the candidate in a disposable Enshrouded world under Kiln.
   Confirm the menu, Magic Chests at another base, recipe craftability and
   actual remote ingredient consumption.
5. Once the new upstream version has passed in-game, update the field
   `gameplay_verified_upstream_version` in the draft PR's
   `release.json` to exactly match `upstream_version`.
   If the game's own executable was updated, verify and update
   `tested_client_sha256` as well.
6. Mark the PR ready, review and merge. The packaging workflow publishes
   the corresponding namespaced `building-tools-kiln-vX.Y.Z` release,
   with the install ZIP and checksum. No existing `BuildingTools.ini`
   settings are packaged or overwritten.

**Safety gate:** The production release job refuses publication when
`upstream_version` differs from `gameplay_verified_upstream_version`.
The scheduled update checker can never silently merge, install, tag or
publish a new upstream DLL.

If GitHub repository settings prevent Actions from opening draft PRs,
the workflow attempts to create an issue with the proposed branch link
and the same acceptance instructions.

## Manual trigger

Go to [Actions → Check Building Tools upstream updates](https://github.com/bisdat/kiln-mods/actions/workflows/buildingtools-upstream-watch.yml),
then select **Run workflow**. When the upstream release is unchanged, the
action reports that no update is needed.

For a manual pin update from a reviewed release, use:

```bash
python3 BuildingToolsKiln/propose_update.py \
  --upstream-version 0.31.3 \
  --sha256 <sha256-of-official-upstream-release-zip>
```

Then open a PR, run the packaging workflow and complete the in-game acceptance
before changing the gameplay verification gate.

If you want to **edit the original C++ mod**, rather than simply keep
the unmodified wrapper updated, you would need a separate GitHub fork of
the developer's repository. That is a different project with ongoing source
merge obligations; this wrapper deliberately avoids that burden.
