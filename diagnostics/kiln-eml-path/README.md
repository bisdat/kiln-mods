# Kiln EML path-regression diagnostic

**Not an Enshrouded mod; do not install.** This isolated Rust standard-library diagnostic tests the failure seen in [Kiln upstream issue #4](https://github.com/frostdev-ops/kiln/issues/4) and [Flame Altar Expanded blocker #14](https://github.com/bisdat/kiln-mods/issues/14).

The observed failure was an EML `DiskFileSystem` under Wine/Proton. A root-level `mod.json` opened, but `src/mod.lua` failed with Windows `os error 3` even though the package and Library copy contained the Lua file. EML's root was a canonical Windows extended-length path of the form `\\?\Z:\...`; such paths do not normalise a forward slash embedded in a joined filename.

The diagnostic compares the old `root.join("src/mod.lua")` operation to a component-by-component path builder, prints both paths, and verifies the latter can open the nested file while retaining root-containment checks.

The workflow runs the **same pure std Rust test** on native Windows and Ubuntu. A passing Windows test supports the proposed fix but **does not replace a real Wine/Proton integration test**, and does not test Enshrouded resource patching. Keep the game mod quarantined until that separate integration test succeeds.

Run locally (Rust required): `rustc --edition=2021 --test diagnostics/kiln-eml-path/test.rs -o eml-path-test && ./eml-path-test --nocapture`.

## Targeted read-only test in the user's actual Proton environment

The first matrix passed on Windows, Linux, Wine `Z:`, and the **real** Kiln EML `mod-loader-base` crate compiled from the exact upstream revision `d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63`, including a synthetic Fedora-style `/home -> /var/home` symlink. No universal bug in path joining or canonicalisation was reproduced. Do not change upstream code on those results alone.

A separate `eml_readonly_probe.rs` creates a standalone **Windows executable** linked against that pinned GPL-licensed EML reader. It **does not write files**: the probe checks Windows `metadata` and `canonicalize` on a caller-supplied existing Library folder and performs only two EML `FileSystem::read_file` reads (manifest and Lua entrypoint). It never loads or evaluates the Lua code.

GitHub Actions attaches `kiln-eml-readonly-proton-diagnostic-v2.zip` containing `eml-readonly-probe.exe`, `run-with-proton.sh`, and this README. It is **not a Kiln mod** and must **not** be dropped onto the Kiln launcher.

### User-side procedure (no game launch)

1. Ensure the faulty Flame Altar Expanded 0.1.1 is still **Library-only and absent from the active Kiln profile**. The Library copy must contain `mod.json` and `src/mod.lua`; the game must not be running.
2. Download the diagnostic artifact from the successful [Diagnose upstream Kiln EML nested paths](https://github.com/bisdat/kiln-mods/actions/workflows/diagnose-kiln-eml-path.yml) run and extract its three files into one folder.
3. Run `bash run-with-proton.sh` **from that folder**. If Steam has more than one Proton installed, the script lists candidates and requires an explicit selection:
   ```bash
   PROTON_RUNNER="$HOME/.local/share/Steam/steamapps/common/Proton Experimental/proton" bash run-with-proton.sh
   ```
   Set `KILN_EML_LIBRARY_DIR` if the Library version directory changes. The script uses a **new temporary Proton compatibility prefix**, does **not** activate Kiln, and does **not** launch Enshrouded. **The first diagnostic wrapper had a cleanup race**: the Proton launcher could exit while Wine was still writing the registry, and the EXIT trap deleted its prefix prematurely. The corrected wrapper uses Proton's `waitforexitandrun` verb, verifies two actual `EML src/mod.lua:` and two `RESULT:` records, and **retains the disposable diagnostic prefix instead of deleting it automatically**. It prints the path to its log and temporary prefix. Once the Wine/Proton process has fully stopped, that one-off temporary directory can be removed manually; do not delete any Steam game compatdata directory.
4. Provide stdout/stderr from the probe, particularly `EML mod.json`, `EML src/mod.lua`, and `canonicalize`/OS error values. The script checks both logical `Z:\home\...` and resolved physical `Z:\var\home\...` paths for the same installed files.

**Interpretation:** If both paths succeed, the installed EML reader and file mapping work in an isolated Proton process, and a file change/race during the long in-game startup (or some other game-process-specific factor) needs instrumentation. A failure only for one root identifies path mapping/canonicalisation behavior. None of these tests validates Flame Altar Expanded's recipe logic; mod distribution remains suspended.

### Licensing

The diagnostic source (`eml_readonly_probe.rs`) and launcher script are MIT licensed. The compiled Windows executable links `mod-loader-base` from upstream Kiln/EML (GPL-3.0-or-later). The corresponding GPL source is publicly available at [upstream commit `d35ee27`](https://github.com/frostdev-ops/kiln/tree/d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63/gpl/kiln-eml); keep the upstream license terms when redistributing the executable.

**Important:** Do not use the original (v1) downloaded diagnostic ZIP; its wrapper had a race that prematurely deleted the scratch Proton prefix. Download the v2 artifact and extract it into a new directory, keeping the obsolete probe separate.
