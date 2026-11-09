# Kiln EML startup-path diagnostic patch

**Test-only, not a Flame Altar Expanded mod or a release. Do not drop this archive onto Kiln.**

This patch targets the exact upstream Kiln source revision used in the failed launch:
[`frostdev-ops/kiln@d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63`](https://github.com/frostdev-ops/kiln/tree/d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63).

The failure: after approximately 184 seconds in EML's startup processing, an EML mod registry successfully read `mod.json` but its Lua module loader failed on `src/mod.lua` with Windows `Path not found (os error 3)`. The game data was restored; no Lua module was executed.

A subsequent, **real** Proton 11 read-only test using the pinned EML file reader succeeded with both the Linux `/home` and `/var/home` Library paths. Therefore no general canonicalization defect has been demonstrated. We need error-stage logging **inside the game startup code**, not another speculative path rewrite.

## What is changed

The `apply_instrumentation.py` script applies strict single-match substitutions to these two GPL files in a checkout of the pinned Kiln commit:

- `gpl/kiln-eml/eml/mod-loader-base/src/registry/fs.rs`: logs when Lua file reading begins and succeeds, along with requested path, canonicalized root, candidate path, OS error kind/code, and elapsed milliseconds. If canonicalization fails it obtains **read-only** metadata for the candidate and its parent, to distinguish a missing file from a missing directory. Failures are tagged by stage: `canonicalize`, `sanitize_path`, `is_file`, `open`, `outside_root`, `symlink`.
- `gpl/kiln-eml/eml/mod-loader-lua/src/runner/state.rs`: logs the mod ID, fully qualified module, root and `src/mod.lua` require operation immediately before file access, plus the success/failure and any UTF-8 read errors.

**No path handling or authorization rule is relaxed.** The same `canonicalize_utf8`, root-containment, symlink, `is_file`, `File::open`, and original `io::Error` return semantics remain. No caching, game data, transaction, or recipe behaviour changes. Diagnostics log local Library paths; review/redact them before publicly sharing logs.

## Tests

`stage_diagnostics.rs` runs four temporary-fixture regressions: valid Lua read, file removed between registry creation and module read, missing nested directory, and outside-root rejection. CI applies the patch to the pinned upstream checkout, runs these tests on **Windows 2022 and Ubuntu 24.04**, and checks the modified `mod-loader-lua` and `kiln-eml` crates. It also creates a source-only `git diff` artifact suitable for upstream review.

A separate **Windows x64, statically linked CRT, release** `kiln-eml.dll` may be produced after Windows tests pass. Its artifact is labelled `TEST-ONLY`; nothing is installed automatically.

## Controlled in-game verification, only after deciding to test

1. Keep a **byte-for-byte backup** of the installed `kiln/plugins/kiln-eml.dll` outside the game tree. Confirm the pinned revision and available backup before replacing anything. Stop the game and launcher before any DLL changes.
2. If opting in to test, replace **only** `kiln/plugins/kiln-eml.dll` with the instrumented DLL built from that exact revision. Never modify Kiln's `dinput8.dll`, EML's game backups, save files, or game data.
3. On one controlled launch with the problematic mod enabled, capture `kiln/logs/eml.log`, `kiln/state/eml.json`, `kiln/state/boot.json`. If startup fails again, do not repeatedly launch. Restore the original DLL and remove the mod from the active profile.
4. Confirm game startup and vanilla restoration after reverting the original DLL. A successful module read only unlocks the next research phase; it does **not** prove the Flame Altar recipe mod is safe.

The DLL is a *diagnostic mechanism*, not a fix, and the mod remains blocked under [issue #14](https://github.com/bisdat/kiln-mods/issues/14) and [upstream Kiln issue #4](https://github.com/frostdev-ops/kiln/issues/4). Keep draft [PR #13](https://github.com/bisdat/kiln-mods/pull/13) unmerged.

## License

This diagnostic patch alters Kiln's EML GPL-3.0-or-later workspace. The patching script, tests and compiled EML DLL are GPL-3.0-or-later. Upstream source and license: https://github.com/frostdev-ops/kiln/tree/d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63/gpl/kiln-eml.
