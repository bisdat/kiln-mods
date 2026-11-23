# Kiln mods

Enshrouded mods using the [Kiln SDK](https://github.com/frostdev-ops/kiln), maintained here as independent projects.

## Survival QoL — first mod

[Survival QoL documentation](SurvivalQoL/README.md) | [Build workflow](.github/workflows/build.yml)

Implements infinite stamina, infinite mana, fast health regeneration, fast mana regeneration, no fall damage and disabled equipment durability loss using Kiln's supported services.

**Survival QoL v0.2.3 candidate:** Health and Mana Regen Per Second now default to **0 additional points/s (normal game)**, while optional bonus rates remain selectable from 0–100 points/s. Existing active-profile values are preserved; set previously saved 25/35 values to 0 manually to adopt the new baseline. **v0.2.2** introduced the independent 1×–10× stamina recovery slider (1× = native recovery), and **v0.2.1** introduced the accepted oxygen/frost duration multipliers. See the [mod guide](SurvivalQoL/README.md).

Build with `bash scripts/build.sh` (requires git, .NET 10 and internet access), or use **Actions → Package Survival QoL** to download the `survival-qol-package` artifact from a successful run. Pushing a matching version tag (e.g. `v0.2.2`) also attaches the ready-to-install ZIP to a GitHub Release. Configuration is hot-reloadable.

See the mod README for limitations, installation, SDK pin and testing instructions.

## Endless Chests (Kiln) — dedicated weapon groups v0.6.0

[User guide](EndlessChests/README.md) | [Canonical JSON item catalogue](EndlessChests/defaults/catalogue.json) | [Settings-only INI](EndlessChests/defaults/endless-chests.defaults.ini) | [Releases](https://github.com/bisdat/kiln-mods/releases) | [CI](.github/workflows/endless-chests.yml)

An independent Kiln-native Magic Chest refill mod. **v0.6.0** adds **ten dedicated weapon/shield groups** (one-handed melee ×3, two-handed melee ×2, daggers, bows, wands, staffs, shields), covering **489 weapon IDs plus 58 equipable shield IDs**. The canonical catalogue now has **205 groups, 4,011 logical positions, 3,481 verified game item IDs and zero unresolved positions**. All 195 original groups and the accepted v0.5.2 Mycelium, Indigo, Basalt, Fishing Bait and identity fixes are preserved. [Weapon classification and exact group names](docs/research/ENDLESS-CHESTS-WEAPON-GROUPS.md). A 72-slot Magic Chest is recommended for complete weapon groups; group selections are truncated to available inventory slots on smaller chests. In-game acceptance is tracked under [issue #45](https://github.com/bisdat/kiln-mods/issues/45).

**Upgrade:** `endless-chests.ini` is now for runtime settings, group exclusions by name, and individual exclusions by **decimal game item ID**; old `[Groups]` sections are ignored. No legacy item-name aliasing or suffix compatibility is retained. Existing `chests.json` bindings and canonical group names continue working. Automatic rename-to-recognised-group clears previous inventory; renaming to an unknown name leaves contents untouched; `Empty` still clears destructively. Back up the world before testing.

## Building Tools for Kiln — cross-base Magic Chests

**Kiln adapter v1.0.0 · upstream Building Tools v0.31.2**

[Building Tools for Kiln source and documentation](BuildingToolsKiln/README.md) |
[Packaging workflow](.github/workflows/buildingtools-kiln-native-adapter.yml) |
[Releases](https://github.com/bisdat/kiln-mods/releases)

An **MIT-licensed upstream Building Tools DLL**, packaged unchanged
as a **Kiln-native mod**. Does **not** replace Kiln's dinput8.dll.
The user confirmed that the in-game menu and **Magic Chests from all
Flame Altar bases** work under Kiln on Linux Steam/Proton.

Enable with **Home → Options → Magic chests of all bases → On**.
Alternatively set GlobalMagicChests=1 under [Chests] in the
game-root BuildingTools.ini while the game is closed, then restart.
The upstream binary is preserved byte-for-byte with its original MIT
licence and translations; the production build pins upstream release
0.31.2 to its verified SHA256. Existing user INI configuration
survives upgrades.

Previous custom native Cross-base Storage implementation and all
related research were **archived**, not merged:
[archived research](docs/ARCHIVED-CROSS-BASE-RESEARCH.md).
Do not install its experimental kiln-rules/kiln-items DLLs.
Remote ingredient debit/save-reload conservation still deserves
separate acceptance testing; see the user guide.

## Enhanced Fishing Rod — stronger Excellent Fishing Rod

[Enhanced Fishing Rod instructions](EnhancedFishingRod/README.md) |
[Packaging workflow](.github/workflows/enhanced-fishing-rod.yml)

Standalone data-only Kiln mod v0.1.0: changes `Tool_T6_FishingRod_epic` Fishing Strength from **2.5 to 5.0** and Fishing Endurance from **55 to 110**, without changing its crafting recipe, rarity, other rods or hidden loot multipliers. Requires Kiln dev.22 or newer. This is a **test candidate** awaiting in-game confirmation; package with `python3 EnhancedFishingRod/package.py` or use the GitHub Actions ZIP.

## NoClip Toggle — in-game hotkey

[NoClip Toggle guide](NoClipToggle/README.md) |
[Packaging workflow](.github/workflows/noclip-toggle.yml)

Standalone Kiln mod v0.1.1 (backtick default following the accepted v0.1.0 toggle) for Kiln 0.2.0-dev.27 or newer. **backtick key** toggles Kiln's native NoClip with HUD feedback; remap the shortcut under Kiln → Keys. Unlike implementing separate collision handling, this mod delegates flight and safe-position recovery to Kiln. Hosted single-player uses the authoritative world tick; optional multiplayer requests are checked against Kiln's NoClip admin permission. Kiln's native NoClip and the initial toggle passed in-game on Bazzite/Proton. If an earlier keybinding was saved, reset it under Kiln → Keys to adopt the backtick default. The backtick shortcut was also confirmed in-game on Bazzite/Proton.

## Base Station Magic Storage — native production automation (test candidate)

[User guide](BaseStationMagicStorage/README.md) | [Candidate build](.github/workflows/base-station-magic-storage.yml) | [Issue #50](https://github.com/bisdat/kiln-mods/issues/50)

An independent mod using Kiln API 0.2.6 to auto-fill FactoryStations from **their own base's Magic Chests** and collect finished outputs into deposit-enabled chests. Off by default; an admin can opt in a **single station** for testing, then deliberately enable the entire base with **bstation all on**. It only collects outputs into dedicated Magic Chests named **BSMS Output** (or BSMS Output 2), preventing accidental delivery into an Endless Chests **Empty** chest. These outputs become shared crafting stock for other stations and the blacksmith. It does not borrow items through the player's backpack, change a recipe or a station's stop state, or pretend the Gem Forge or hand-crafting bench is a FactoryStation. In-game acceptance is pending; no public release.

## Gem Forge Magic Storage — automatic native integration v1.0.0

[Installation and feature guide](GemForgeStorage/README.md) | [Build workflow](.github/workflows/gemforge-storage.yml) | [Releases](https://github.com/bisdat/kiln-mods/releases)

**Automatically** makes eligible Magic Chest gems from the player's current flame-altar base appear in Enshrouded's original Gem Forge **GEMS** grid, using Kiln's supported server-authoritative item transactions. Player-held weapons can receive chest gems, while Archaic Essence payments use player inventory first and then the current base's Magic Chest storage. Recycling rewards and removed gems go to the player's backpack. **No console commands, enable switches, inventory loans, memory patches or custom UI.**

Requires **Kiln 0.2.0-dev.29.15+** on the hosting server and on the client viewing offered gems. The mod itself is installed server-side only. Tests on the precursor v0.3.0 integration passed for native selection, socket/unsocket, exact chest-funded upgrading, insufficient-funds refusal, recycling and withdrawal; the production v1.0.0 removes the test commands. The abandoned temporary gem-lending design remains retired, and historical findings remain in [Gem Forge research](docs/research/GEMFORGE-DIRECT-INTEGRATION.md).

## Rubble Remover — craftable rubble and mycelium clearing tool (test candidate)

[Install/test guide](RubbleRemover/README.md) | [Candidate build workflow](.github/workflows/rubble-remover.yml)

A standalone Kiln-native v0.1.4 diagnostic candidate, crafted at the Workbench for **20 Rubble + 20 Mycelium + 1 Wood Log**. Three sphere brushes (baked into startup tables with a Kiln dev.30.6 loader compatibility shim) remove rubble and thick mineable mycelium, never ordinary terrain or building voxels. Uses Kiln's supported terrain-only native action policy; no EML-R, DLL patching or unrestricted `Build.Dig`. Removal-only, no rubble placement in v0.1.0. Requires **Kiln dev.30.6+ on host and clients**, and is **awaiting in-game acceptance**. v0.1.3 crafts and activates, but its three brushes do not appear; v0.1.4 instruments brush registration and client capability diagnostics without changing terrain edits. Unlike the supplied GPL reference, the implementation is independently written under MIT.

## Licence

Original code, scripts and documentation in this repository are available under
the [MIT License](LICENSE), copyright © 2026 bisdat.

Third-party material is **not relicensed** by this notice. In particular,
Building Tools remains © Aleksander Wojtal under its own MIT licence, and the
Endless Chests catalogue incorporates material attributed to HipShot808.
See [Third-party notices](THIRD_PARTY_NOTICES.md) for attribution and scope.
