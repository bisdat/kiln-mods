# Endless Chests (Kiln) — v0.4.1 (experimental)

An independent C# implementation through the Kiln SDK, using the original Endless Chests group defaults (attributed to HipShot808). It does **not** load or redistribute the Windows ShroudForge DLL or use undocumented native memory offsets.

**v0.4.1 changes:** automatic game-UI name matching now accepts different capitalisation, and renaming to an **unrecognised** name **preserves every existing item** and stops automatic refills. A recognised name still clears the old selection before refilling. The special name `Empty` still **clears automatically without confirmation**, including on startup and when new items are deposited.

The owner confirmed v0.4.0's automatic rename clearing works in-game; v0.4.1 requires a gameplay acceptance test.

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

The bundled [`endless-chests.defaults.ini`](defaults/endless-chests.defaults.ini) contains **195 original groups**, **3,464 item entries**, **3 aliases** and **45 disabled-item entries**. It is copied to the active mod's `endless-chests.ini` on first run only, never overwritten on upgrade. Edit `[Groups]`, `[Aliases]`, `[DisabledGroups]`, `[DisabledItems]` or `[Options]` in that active copy, then run `echest reload`. Disabled items and members of disabled groups are excluded globally. Missing game items are skipped and logged.

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
3. From a successful [Endless Chests packaging run](https://github.com/bisdat/kiln-mods/actions/workflows/endless-chests.yml), download the `endless-chests-kiln-package` artifact.
4. Extract it and import the enclosed **`endless-chests-kiln-0.4.1.zip`** into Kiln's Library. Enable v0.4.1 in the active profile and restart Enshrouded.
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

The GitHub Actions workflow validates all 195 original groups, the 3,464 item entries, all 32 case variants of Empty, the observed chest-name FNV hashes, mixed-case matching for all original group labels and the collision-safe catalogue lookup. It builds against the released/pinned Kiln SDK source.

Local Linux build:

```bash
KILN_UPSTREAM_SHA=eba9a5e1e0e0044a9146fdd53021a751e2957030 \
KILN_SDK_VERSION=0.2.0-dev.local.20261107.61500 \
bash scripts/build.sh EndlessChests
```

Output: `bin/Release/endless-chests-kiln-0.4.1.zip`. The version tag `endless-chests-v0.4.1` can publish a release. Development remains in draft [PR #3](https://github.com/bisdat/kiln-mods/pull/3); remaining parity checks are tracked in [issue #4](https://github.com/bisdat/kiln-mods/issues/4).
