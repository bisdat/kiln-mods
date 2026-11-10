# Flame Altar Level 4 Range Test — experimental EML 0.1.0

> **BLOCKED — DO NOT INSTALL (10 November 2026).** A user reports that Kiln fails to load with the test ZIP installed. This experiment used an incorrect separate mod identity/version (0.1.0 rather than a 0.1.2 iteration of Flame Altar Expanded). The cause of the loading failure is not yet established. Package publishing in CI has been disabled. Disable/remove `flame-altar-level4-range-test`, preserve diagnostic logs, and do not retry until investigated. This document below describes an **inactive historical test plan**, not instructions to execute now.

**Purpose:** isolate Enshrouded's altar **build-zone size** mechanism from the unresolved fifth-tier recipe/upgrade UI logic in [issue #12](https://github.com/bisdat/kiln-mods/issues/12). This is a temporary diagnostic mod, **not an update to Flame Altar Expanded 0.1.1** and not a production release.

## Exact change

- At **existing altar level 4**, change `keen::BalancingTable.buildzoneSizesPerAltarLevel[5]` (Lua 1-based array indexing) from **160 × 160 × 160 m** to **300 × 300 × 300 m**.
- Leave levels 1–3 unchanged, do not alter the level-zero entry, and leave level 5 at the original **160 × 160 × 160 m**.
- No new recipes, no Shroud Nexus cost, no changes to `altarLevelMax`, no runtime hooks and no extra levels.
- Abort without modifying any resource if the game has an unexpected size array, dimensions, maximum level or EML capability. Uses exactly one BalancingTable resource.

The test answers **only**: does the existing level-4 zone (menu, actual building permission and persistence) honour the pregame balancing-table change? It does **not** test or implement the separate level-5 upgrade.

## Safety — read before using

**Use only an expendable/copy-of-world save with an existing level-4 Flame Altar.** Expanded build zones may change the regions the game saves or protects. Do not save a valuable world with this diagnostic enabled and do not subsequently open a test world containing outside-vanilla construction with the mod disabled.

**The original Flame Altar Expanded 0.1.1 must be DISABLED.** They both patch the same BalancingTable, and EML mod execution order is not guaranteed. Do not install two different instances or manually change KFC files/cache.

This mod needs official Kiln `v0.2.0-dev.21` or later including `kiln-eml.dll`. A successful in-game test on a local hosted world is not proof of multiplayer/dedicated-server safety.

## Installation through Kiln

1. Exit Enshrouded. In Kiln, disable **Flame Altar Expanded 0.1.1** in the active profile.
2. Download the **`flame-altar-level4-range-test-0.1.0` GitHub Actions artifact** from a passing build of this test PR. The downloaded ZIP is the **directly installable EML mod**: its root contains `mod.json` and `src/mod.lua`. Do not extract or double-zip it.
3. Drag that ZIP onto the Kiln launcher, confirm **Flame Altar Level 4 Range Test** is enabled as an EML mod, and start Enshrouded.
4. Inspect the EML log `kiln/logs/eml.log` and state `kiln/state/eml.json`. On an uncached patch run expect:
   `flame-altar-level4-range-test: level 4 changed 160x160x160 -> 300x300x300 m; level 5 unchanged; recipes untouched`, `patched=1`, `phase=done`, `error=null`. Subsequent unchanged boots may be cached.
5. In the **disposable world** with an already-level-4 Flame Altar, check **Altar Level 4**, and whether the UI now reads **300 m × 300 m × 300 m**. The **Upgrade Altar** button may remain greyed out and that is expected: the mod adds no next tier.
6. If the UI changed, test whether a test block can be placed at **~100 m horizontally from the altar centre**, outside the original 80 m half-width but inside the proposed 150 m half-width, without crossing another altar zone or a no-build restriction. Do not build in a valuable save. On the test copy only, optionally save/restart with the mod still active to verify persistence.
7. Capture a screenshot plus `kiln/state/eml.json`, `kiln/logs/eml.log` and `kiln/logs/kiln.log`; report separately: (a) EML patch, (b) level-4 UI dimensions, (c) actual extended building permission and (d) restart persistence if tested.
8. Exit the game and disable/remove this test mod in Kiln. With **no EML mods** enabled, launch once to the main menu and exit so Kiln can restore the vanilla KFC. Verify `kiln/state/eml.json` reports `restored=true` (if the patch was previously present). Restore/discard the disposable world rather than loading altered territory in vanilla.

**Rollback:** Keep original game backups/KFC untouched; use the Kiln UI to disable the mod. Do not manually delete `.kfc`, `.kfc.bak`, `.dat` or EML caches.

## Development

Source: `src/mod.lua`, manifest: `mod.json`. Offline mocked tests: `lua5.4 tests/test_patch.lua`. The GitHub Actions workflow runs Lua syntax, isolated userdata-backed mock contracts, manifest verification and archive layout checks. These tests do not emulate real Enshrouded world building.

Copyright: MIT. Kiln's bundled GPL-licensed EML code is not redistributed by this mod.
