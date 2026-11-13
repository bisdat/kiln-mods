# Endless Chests (Kiln) — v0.4.4 (experimental)

An independent C# implementation through the Kiln SDK, using the original Endless Chests group defaults (attributed to HipShot808). It does **not** load or redistribute the Windows ShroudForge DLL or use undocumented native memory offsets.

**v0.4.4 changes:** Fixes [issue #30](https://github.com/bisdat/kiln-mods/issues/30): the original catalogue includes `Mirage Variant 1165213726` in `Songs`. The suffix is the song sheet's actual decimal game item ID, not part of the display name. The mod now recognises `<display name> Variant <decimal ID>` by looking up that exact ID and checking the game's display name matches the prefix; if it is absent or mismatched the item is skipped instead of being confused with another item (for example, the separate **Mirage** weapon). The audit also identified **22 incomplete `Variant` labels without IDs**: 21 in `Vanity Outfits 3` and `Hunter Boots Variant` in `Vanity Outfits 1`. Each was matched to a specific **regular vanity item**, distinct from a Twitch-drop or combat-armour identity, using the pinned Kiln SDK catalogue. The shipped INI now contains numeric IDs for all 22, bringing the total to **88** vanity/song/decoration numbered variants. A further two formerly unresolvable collectible entries are repaired under `Alchemist Wall Hanging`, giving **90** verified numbered variants altogether. The 195 groups, their 3,464 entries, and their order remain intact. Existing user-owned INIs are **not overwritten**: the resolver has an explicit compatibility mapping for the 22 old bare `Variant` labels, including `Flame Pact Gloves Variant` → ID `3829626055` (not the Twitch-drop ID `1425634981`).

**v0.4.4 additional correction:** `Alchemist Wall Hanging = Alchemist Hanging I | Alchemist Hanging II` used names no longer present in the game catalogue. Its two distinct collectible halves share the current name `Ripped Alchemist's Wall Hanging`; the shipped group now uses their verified IDs `2365197861` (`Collection_Alchemist_WallHang_Piece_01`) and `1372134498` (`Collection_Alchemist_WallHang_Piece_02`). The resolver also accepts both historical `Alchemist Hanging I/II` labels in existing user INIs, requiring the expected game debug name. There is no need to replace your active INI. The in-game `Alchemist Showcase` is a separate crafted item, not part of this two-piece group.

**v0.4.3 changes:** [Issue #16](https://github.com/bisdat/kiln-mods/issues/16) pins **Rough Wood Block** to verified game item ID `2012474258` (`Block_T1_Wood_Rough_CRAFTING`) rather than one of four quest reward icons with the same English display name. The fix applies to `Starter Blocks`, `Springlands Blocks`, `Wood Blocks` and direct item selections, including older user-owned INIs. Other duplicate display labels produce a once-per-selection warning with the chosen ID, without changing their current refill behaviour.

**v0.4.2 changes:** The original catalogue is now **alphabetically sorted by key within each INI section** for easier browsing. Group membership and the order of items *inside each group* are unchanged. GitHub Releases also supply a **separate downloadable `endless-chests.defaults.ini`** alongside the installable mod ZIP. The name matching, refill and automatic Empty behaviour from v0.4.1 are unchanged.

The owner confirmed v0.4.1 works in-game. v0.4.2 changed only catalogue presentation and release assets; v0.4.3 changes item resolution as described above.

### Variant IDs and duplicate display names

Some original INI entries contain `Variant <decimal ID>` after the display name, because multiple game items share that name. Endless Chests v0.4.4 understands those entries directly using Kiln `Items.Get(ItemId)` and verifies the corresponding `DisplayName` (case-insensitive). For example `Mirage Variant 1165213726` is the collectible song sheet with internal ID `8kLoreText_scroll_bard_song_12`, **not** the separate Mirage staff (`1992044990`). Incorrect or unavailable variant IDs are skipped; they never fall back to a different same-named item. **Twenty-two entries previously had no numeric suffix** (for example, `Flame Pact Gloves Variant`). The bundled defaults now include verified decimal IDs for the normal vanity items, while the runtime also recognizes exactly those 22 historical labels in pre-existing user INIs. All other bare `Variant` names fail closed. **Do not simply remove `Variant`** from your active INI: some unsuffixed labels appear in `[DisabledItems]` to exclude the account-restricted versions. There is no need to replace the active INI after installing v0.4.4.

### Duplicate display names

Some game item IDs share an English display name. Kiln's default `Items.Lookup(displayName)` can choose an unrelated icon. For **Rough Wood Block**, the mod resolves by numeric ID and verifies the item's internal debug name; if the identity changes in a later game version, it skips the item instead of refilling an icon. The 195 original groups and their entry count are preserved, but the bundled INI now includes the 22 corrected numeric variant suffixes. Existing `endless-chests.ini` user customisations are never overwritten.

For **other** duplicates, the log reports the number of matching IDs and the currently selected fallback ID. No automatic substitution is attempted. To fix another ambiguous entry yourself, replace only that *item name* in a `[Groups]` list with its decimal item ID, then run `echest reload`. This is distinct from renaming a chest: recognised in-game renames and `Empty` still intentionally delete inventory as described below.

## Name-change behaviour

| Action using Enshrouded's in-game Rename interface | Behaviour |
|---|---|
| Rename `Tools` → `Starter Blocks` | **Clear the previous items**, then refill Starter Blocks |
| Rename `Starter Blocks` → `Animals` | **Clear the previous items**, then refill Animals |
| Rename to `tools`, `TOOLS`, `tOoLs`, or other capitalisation of a known group | Recognise the original group; **clear old contents** (if an actual rename was observed), then refill |
| Rename to `My Storage` or any **unrecognised** name | **Do not delete anything** and **stop automatic refilling**; items already inside stay |
| Rename to `Empty`, `empty`, `EMPTY` or mixed-case variants | **Permanently clear automatically** at the next check and continue clearing anything put inside, with no confirmation |
| Restart with an ordinary named chest | Preserve contents on initial observation, then resume normal refill |
| Restart with a chest named `Empty` | **Automatically clear it**, even if no rename occurred during this session |

Automatic processing runs on the next scan (every 4 seconds by default). Renames to an unrecognised name also remove any obsolete manual binding, preventing the old group from refilling that chest. For manual `echest bind` use, existing assignments take precedence until renamed in-game or unbound.

**Back up your world.** Recognised renames and `Empty` intentionally delete inventory permanently. Deletions are serialized per chest via Kiln's `Items.TakeSlot` transactions, with rechecks of world, position, chest name fingerprint, exact slot and item identity. If another rename occurs while an asynchronous removal is in flight, remaining transactions are stopped; an already-issued removal cannot be undone. This release is experimental.

## Capitalisation support

Enshrouded exposes only an **FNV-1a64 fingerprint** of each custom chest name through its 8-byte `CustomPlayerString` component. A fingerprint by itself cannot simply be lowercased. The mod therefore compares the hash against variations of every original group, alias and item label:

- All **195 original group names** and all **3 aliases** accept **any ASCII uppercase/lowercase mixture** (for example `sTaRtEr bLoCkS` or `animalS`), including multiword names. Any recognised rename triggers the normal clear-and-refill.
- For original individual item labels with **30 or fewer ASCII letters**, the same arbitrary mixed-case matching is supported. For longer labels, original spelling, all-lowercase, ALL-UPPERCASE and title-case variants are covered, but arbitrary mixed-case letters are not yet guaranteed.
- Unicode characters, including non-ASCII letter case variations, may need an exact or common-case spelling. Spaces, punctuation and apostrophes are unchanged by case-insensitive matching. FNV name collisions are rejected when detected.
- All **32 ASCII capitalisations of `Empty`** are recognised independently and remain destructive.

No private string-store reads or Windows-client DLLs are required. The original name hashes were established from two successful live probes of the same game chest: `Starter Blocks = 0x35F105E7ECC53E32` and `Tools = 0xC26501E3ADE7DA3C`.

## Original catalogue and settings

The bundled [`endless-chests.defaults.ini`](defaults/endless-chests.defaults.ini) contains **195 original groups**, **3,464 item entries**, **3 aliases** and **45 disabled-item entries**. Entries are now **sorted alphabetically by their left-hand-side names within each section** (`[Options]`, `[DisabledItems]`, `[Groups]`, `[Aliases]`; `[DisabledGroups]` has no active entries). Items on each group's right-hand side retain their original order; only 22 incomplete `Variant` suffixes have had numeric game IDs added. The INI is copied to the active mod's `endless-chests.ini` on first run only, **never overwritten on upgrade**. Edit `[Groups]`, `[Aliases]`, `[DisabledGroups]`, `[DisabledItems]` or `[Options]` in that active copy, then run `echest reload`. Disabled items and members of disabled groups are excluded globally. Missing game items are skipped and logged.

The **tagged GitHub Release** publishes both `endless-chests-kiln-0.4.4.zip` (installable Kiln mod) and **`endless-chests.defaults.ini` (standalone sorted catalogue)** under Assets. You can [view the sorted catalogue in the repository](defaults/endless-chests.defaults.ini) without downloading. The standalone file is for reference or manual editing; **do not import the INI as a Kiln mod**. If you want to replace an existing active INI, back up your custom edits and copy its contents deliberately; installation does not overwrite it.

The active `chests.json` file (typically in `kiln/data/endless-chests-kiln/`) stores existing manual chest bindings and settings:

```json
{
  "enabled": true,
  "autoNames": true,
  "clearOnRename": true,
  "autoEmpty": true,
  "recheckSeconds": 4,
  "positionToleranceMeters": 0.75,
  "chests": []
}
```

**Do not overwrite your existing `chests.json` with this example:** doing so would erase your manual binding list. Instead, edit individual options and use `echest reload` to apply them.

- `autoNames=false`: disable the automatic game-name system (manual commands still work)
- `clearOnRename=false`: recognised renames preserve existing items while changing the refill selection
- `autoEmpty=false`: stop *repeated* Empty clears; with `clearOnRename=true`, a new rename to Empty still performs one clear

Gear level/rarity/perk upgrades and gem-level special items from ShroudForge are not implemented through normal Kiln Items transactions. Magic chest and dedicated server behaviours require further tests.

## Installation

1. **Back up the world**, especially chests with valuables.
2. Use a compatible Kiln runtime (this repo pins published Kiln `v0.2.0-dev.15`).
3. From a successful [Endless Chests packaging run](https://github.com/bisdat/kiln-mods/actions/workflows/endless-chests.yml), download the `endless-chests-kiln-package` artifact, or use the [tagged GitHub Releases](https://github.com/bisdat/kiln-mods/releases) page for the individual ZIP and sorted INI assets.
4. Extract the Actions artifact (if downloaded) and import the enclosed **`endless-chests-kiln-0.4.4.zip`** into Kiln's Library. Enable v0.4.4 in the active profile and restart Enshrouded. The accompanying `endless-chests.defaults.ini` is a separate reference/download, **not** an installable Kiln package.
5. Try **`TOOLS → starter blocks → My Storage → Empty`** on a throwaway chest. The unrecognised name must **not** delete the blocks; Empty **must** clear them without confirmation.

## Kiln console commands (optional)

In a locally hosted solo world, Kiln processes `kiln/control/console/*.cmd` after a character enters the game. The nearest-chest commands require your character within **4 metres** of a compatible chest. A missing command result may mean the game hasn't processed the file yet.

| Command | Effect |
|---|---|
| `echest nameprobe` | Read-only: report nearest chest's ECS name fingerprint, recognised original name and manual binding |
| `echest status` | Show settings, service readiness and group/name-map counts |
| `echest groups [search]` | Search original group labels |
| `echest bind Tools` | Manually bind an **empty** nearby chest to Tools, until unbound or renamed in-game |
| `echest unbind` | Remove manual binding without deleting contents |
| `echest empty` / `echest bind Empty` | **Legacy manual operation:** preview a clear without performing it |
| `echest empty confirm` | Confirm the manual preview within 60 seconds |
| `echest refill` | Request the next scan |
| `echest reload` | Reload owner JSON and INI |

**Automatic game-UI `Empty` does not require commands.** The separate manual `echest empty` command retains preview/confirmation.

Run a read-only probe from a terminal **in the Enshrouded game folder**:

```bash
mkdir -p kiln/control/console
printf 'echest nameprobe\n' > kiln/control/console/probe.tmp
mv kiln/control/console/probe.tmp kiln/control/console/probe.cmd
# After the game processes it:
cat kiln/control/console/probe.result.json
```

## CI and development

The GitHub Actions workflow validates all 195 original groups, the 3,464 item entries, all 90 numbered variants against the pinned Kiln SDK registry, all 22 legacy unnumbered vanity mappings and the 2 renamed Alchemist hanging mappings, alphabetical key order within every INI section, all 32 case variants of Empty, the observed chest-name FNV hashes, mixed-case matching for all original group labels and the collision-safe catalogue lookup. It builds against the released/pinned Kiln SDK source and stages the sorted standalone INI with the mod ZIP for download and tagged releases.

Local Linux build:

```bash
KILN_UPSTREAM_SHA=eba9a5e1e0e0044a9146fdd53021a751e2957030 \
KILN_SDK_VERSION=0.2.0-dev.local.20261107.61500 \
bash scripts/build.sh EndlessChests
```

Output: `bin/Release/endless-chests-kiln-0.4.4.zip`. After merging this change into `main`, publishing tag **`endless-chests-v0.4.4`** triggers both release assets automatically. The original v0.4.1 release remains unchanged; the remaining parity checks are tracked in [issue #4](https://github.com/bisdat/kiln-mods/issues/4).
