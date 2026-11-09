# Kiln EML startup-path diagnostic patch

**Test-only, not a Flame Altar Expanded mod or a release. Do not drop this archive onto Kiln.**

This patch targets the exact upstream Kiln source revision used in the failed launch:
[`frostdev-ops/kiln@d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63`](https://github.com/frostdev-ops/kiln/tree/d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63).

The failure: after approximately 184 seconds in EML's startup processing, an EML mod registry successfully read `mod.json` but its Lua module loader failed on `src/mod.lua` with Windows `Path not found (os error 3)`. The game data was restored; no Lua module was executed.

### Additional evidence from 9 November 2026

The user's preserved files show an **idle no-EML-mod** run at 14:22:05–14:22:06 UTC, followed by an **incomplete mod-enabled EML log** whose last file mtime is 14:28:58 UTC. That later log contains mod discovery, the successful root `mod.json` read and `Running lua with options` but **never reaches** `Running mods...`, the first `src/mod.lua` read, or any file-reader instrumentation. It has no corresponding final EML state, so this is not a successful mod run. This evidence points investigation to the *pre-Lua* preparation in `mod-loader-lua::run`, not necessarily the Lua entrypoint file reader. Later idle starts overwrite `kiln/state/eml.json` and truncate `kiln/logs/eml.log` at plugin initialization; therefore per-attempt snapshots are required.

A subsequent, **real** Proton 11 read-only test using the pinned EML file reader succeeded with both the Linux `/home` and `/var/home` Library paths. Therefore no general canonicalization defect has been demonstrated. We need error-stage logging **inside the game startup code**, not another speculative path rewrite.

### Confirmed 15:06 checkpoint boundary

The 15:06:23 UTC in-game trace reached `eml_preload_checkpoint stage=track_game_files_start` at elapsed 2 ms, with **no matching `track_game_files_done`**. The previous mod snapshot and previous-cache read had already completed successfully. Its `eml.json`/ `boot.json` are older (14:56 UTC, idle), not the active-run outcome. This isolates the interrupted operation to `FileStateCache::track_game_files`; it is **not proof** that an individual file hung or that the failed `src/mod.lua` read recurred.

The pinned EML `track_game_files` enumerates the game's top-level files by extension (`dat`, `kfc`, `kfc_resources`, `exe`, `bak`, `integrity`, plus `eml.json`) and `FileStateCache::track` reads **every byte** through a CRC-64 digest in 64-KiB blocks. `apply_game_file_progress.py` adds tracing-only `eml_game_file_fingerprint_start/done` with file sizes and elapsed time, plus `eml_large_file_hash_progress` every 128 MiB for large files. Existing file selection, hashing, cache contents and error handling are unchanged.

A separate `list-game-file-sizes.py` is included with the diagnostic artifact. Run `python3 list-game-file-sizes.py` on the host (game **closed**, Flame Altar Expanded **disabled**) to list exactly the candidate sizes **without opening or hashing their contents**. This is a safe preliminary check before authorizing another DLL test.

## What is changed

The `apply_instrumentation.py` script applies strict single-match substitutions to these two GPL files in a checkout of the pinned Kiln commit:

- `gpl/kiln-eml/eml/mod-loader-base/src/registry/fs.rs`: logs when Lua file reading begins and succeeds, along with requested path, canonicalized root, candidate path, OS error kind/code, and elapsed milliseconds. If canonicalization fails it obtains **read-only** metadata for the candidate and its parent, to distinguish a missing file from a missing directory. Failures are tagged by stage: `canonicalize`, `sanitize_path`, `is_file`, `open`, `outside_root`, `symlink`.
- `gpl/kiln-eml/eml/mod-loader-lua/src/runner/state.rs`: logs the mod ID, fully qualified module, root and `src/mod.lua` require operation immediately before file access, plus the success/failure and any UTF-8 read errors.

- `gpl/kiln-eml/eml/mod-loader-lua/src/lib.rs`: a `eml_run_identity` record with process ID and UTC epoch milliseconds; `eml_preload_checkpoint` events bracketing initial mod fingerprinting, cached-state reads, game-file hashing, second mod snapshot, `AppState::new`, game input fingerprinting, Lua runner construction/setup, and the transition into executing modules. This identifies the **last stage reached if the process stops before requiring a Lua file**. These events measure the time from EML's run entry; they perform no extra game-file reads.

- `gpl/kiln-eml/eml/mod-loader-lua/src/cache/track.rs` and `cache/mod.rs`: identify and time the exact game file being CRC-64 fingerprinted and report bounded, 128-MiB progress while hashing large files. Read and CRC semantics remain identical.

**No path handling or authorization rule is relaxed.** The same `canonicalize_utf8`, root-containment, symlink, `is_file`, `File::open`, and original `io::Error` return semantics remain. No caching, game data, transaction, or recipe behaviour changes. Diagnostics log local Library paths; review/redact them before publicly sharing logs.

## Tests

`stage_diagnostics.rs` runs four temporary-fixture regressions: valid Lua read, file removed between registry creation and module read, missing nested directory, and outside-root rejection. CI applies the patch to the pinned upstream checkout, runs these tests on **Windows 2022 and Ubuntu 24.04**, and checks the modified `mod-loader-lua` and `kiln-eml` crates. It also creates a source-only `git diff` artifact suitable for upstream review.

A separate **Windows x64, statically linked CRT, release** `kiln-eml.dll` may be produced after Windows tests pass. Its artifact is labelled `TEST-ONLY`; nothing is installed automatically.

## Controlled in-game verification, only after deciding to test

1. Keep a **byte-for-byte backup** of the installed `kiln/plugins/kiln-eml.dll` outside the game tree. Confirm the pinned revision and available backup before replacing anything. Stop the game and launcher before any DLL changes.
2. **Before starting the test, prepare the read-only capture script** included in this artifact. After the one controlled mod-enabled launch, and **before launching Enshrouded a second time or disabling/re-enabling profiles**, run from the extracted artifact directory:
   ```bash
   bash capture-run-logs.sh
   ```
   It writes a new, uniquely named `~/Downloads/kiln-eml-startup-<UTC>-<pid>` directory, preserving source modification timestamps. It does not modify game or mod files.
3. If opting in to test, replace **only** `kiln/plugins/kiln-eml.dll` with the instrumented DLL built from that exact revision. Never modify Kiln's `dinput8.dll`, EML's game backups, save files, or game data.
4. On one controlled launch with the problematic mod enabled, capture `kiln/logs/eml.log`, `kiln/state/eml.json`, `kiln/state/boot.json`. If startup fails again, do not repeatedly launch. Restore the original DLL and remove the mod from the active profile.
5. Confirm game startup and vanilla restoration after reverting the original DLL. A successful module read only unlocks the next research phase; it does **not** prove the Flame Altar recipe mod is safe.

The DLL is a *diagnostic mechanism*, not a fix, and the mod remains blocked under [issue #14](https://github.com/bisdat/kiln-mods/issues/14) and [upstream Kiln issue #4](https://github.com/frostdev-ops/kiln/issues/4). Keep draft [PR #13](https://github.com/bisdat/kiln-mods/pull/13) unmerged.

## License

This diagnostic patch alters Kiln's EML GPL-3.0-or-later workspace. The patching script, tests and compiled EML DLL are GPL-3.0-or-later. Upstream source and license: https://github.com/frostdev-ops/kiln/tree/d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63/gpl/kiln-eml.
