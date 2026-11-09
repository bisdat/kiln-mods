# Kiln EML path-regression diagnostic

**Not an Enshrouded mod; do not install.** This isolated Rust standard-library diagnostic tests the failure seen in [Kiln upstream issue #4](https://github.com/frostdev-ops/kiln/issues/4) and [Flame Altar Expanded blocker #14](https://github.com/bisdat/kiln-mods/issues/14).

The observed failure was an EML `DiskFileSystem` under Wine/Proton. A root-level `mod.json` opened, but `src/mod.lua` failed with Windows `os error 3` even though the package and Library copy contained the Lua file. EML's root was a canonical Windows extended-length path of the form `\\?\Z:\...`; such paths do not normalise a forward slash embedded in a joined filename.

The diagnostic compares the old `root.join("src/mod.lua")` operation to a component-by-component path builder, prints both paths, and verifies the latter can open the nested file while retaining root-containment checks.

The workflow runs the **same pure std Rust test** on native Windows and Ubuntu. A passing Windows test supports the proposed fix but **does not replace a real Wine/Proton integration test**, and does not test Enshrouded resource patching. Keep the game mod quarantined until that separate integration test succeeds.

Run locally (Rust required): `rustc --edition=2021 --test diagnostics/kiln-eml-path/test.rs -o eml-path-test && ./eml-path-test --nocapture`.
