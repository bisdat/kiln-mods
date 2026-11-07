# Endless Chests (Kiln) — v0.4.0 (experimental)

An independent Kiln-native C# implementation of the Endless Chests behaviour, without the original ShroudForge DLL or undocumented game-memory offsets. The original ShroudForge mod is attributed to HipShot808.

**WARNING — DESTRUCTIVE BEHAVIOUR:** The world owner's requested defaults in **v0.4.0** change how renamed chests behave. A chest's **existing items are permanently deleted whenever the mod observes an in-game name change**, even if the new name is not a recognised item/group. After deleting the previous contents, it refills the new recognised selection. Naming a chest **`Empty`** (or `empty`, regardless of ASCII letter case) **deletes all its contents automatically, without any confirmation**, and will clear anything put into it again on subsequent checks. **Back up the Enshrouded world before installing this release.** Test on a disposable chest first.

## Status

- **Confirmed in-game:** basic Kiln refills (v0.1.0), manual preview/confirm deletion (v0.2.0), automatic in-game name recognition and refill for **Tools**, **Starter Blocks** and **Animals** (v0.3.0; reported 7 November 2026).
- **New in v0.4.0 — not yet gameplay tested:** automatic clear on rename and automatic clear on `Empty`; CI compiling and static checks do not demonstrate reliable live deletion. Keep [PR #3](https://github.com/bisdat/kiln-mods/pull/3) in draft until you test a throwaway chest.

## Automatic rename behaviour (new in v0.4.0)

1. Install the new ZIP in the host's Kiln profile; enter your saved world.
2. Stand beside a test chest and use Enshrouded's **built-in Rename** interface (no `echest bind` command necessary).
3. When a name change is observed, the mod **removes all items already in that chest**, through the game's own `Kiln.Items.TakeSlot` transactions.
4. **Only when deletion has completed** does it refill the chest if the new name is an original group, alias, or item label (for example `Starter Blocks`, `Animals` or `Tools`). Renames to unknown names still clear items but **do not refill**.
5. Rename a chest to `Empty` (any capitalization such as `empty` or `EMPTY`): the mod deletes all current contents and does not refill them. It rechecks periodically, clearing any new items while that name remains.

**If you rename Tools → Starter Blocks → Animals, each detected rename wipes the previous selection before refilling the next one.** It does not merge each selection's items as v0.3.0 did. Refills and clears run every 4 seconds by default (`recheckSeconds` in `chests.json`). A transaction already submitted when a rename occurs may finish before the subsequent clear; each later removal/give checks the current name.

### First observation, restart and manual bindings

- The first time v0.4.0 sees an ordinary named chest in a loaded world, it records the name **without clearing**. It cannot prove a rename happened before the mod started observing. Normal chests with existing contents therefore **do not get wiped merely because you installed, upgraded or restarted Kiln**.
- **Exception: a chest already named `Empty` is cleared on the first scan**, without any confirmation. It continues clearing on subsequent scans while the name stays `Empty`.
- On an observed game-UI name change, existing `echest bind` assignments for that chest are automatically dropped, letting the game name take over. Prior manual bindings otherwise remain intact.
- The mod only observes chests that are loaded as compatible storage entities. If a chest is unloaded during a rename, the mod cannot necessarily detect the missed transition. It never guesses an absent/invalid name to trigger deletion.
- The snapshot-based clear checks the chest's world ID, entity ID, position, name hash, and each item's slot/quantity/entity ID between transactions. A second rename, world exit or inventory conflict stops further deletion; pending renames are retried on later checks. **Partial deletion cannot be undone.**

### Configuration: opt out of destructive actions

The mod's active data file is `kiln/data/endless-chests-kiln/chests.json` (or the mod data folder Kiln reports). These settings are **true by default**:

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

**Edit only the relevant fields in your existing file; do not replace the entire file above**, because `chests` contains any saved manual bindings. `echest reload` applies edits in a running world.

- `clearOnRename=false` stops automatic deletion caused by ordinary name changes.
- `autoEmpty=false` stops repeated clearing on a chest already named `Empty`. If `clearOnRename=true`, changing a name *to* `Empty` still causes a one-time rename clear.
- `autoNames=false` disables both automatic name-based actions and refills; manual `echest bind` still works.

### Legacy console commands

All `echest` commands remain admin-only; on a locally hosted solo world you can create `kiln/control/console/*.cmd` after entering the running world. A command naming a nearby chest works **only** when the character is inside a world and within 4 metres of a compatible chest.

| Command | Effect |
|---|---|
| `echest nameprobe` | Read-only: report the nearest chest's `CustomPlayerString` hash, known-name match and any manual binding |
| `echest status` | Show settings, world, service and catalogue state |
| `echest groups [search]` | Find exact original group names |
| `echest bind Starter Blocks` | Manually bind the nearest **empty** chest; existing binding takes precedence until in-game rename or unbind |
| `echest unbind` | Remove a manual binding without deleting contents |
| `echest empty` / `echest bind Empty` | Legacy console path: preview nearby items (does not clear until confirmed) |
| `echest empty confirm` | Complete the legacy console deletion from its matching preview |
| `echest refill` | Request an immediate refill check |
| `echest reload` | Reload the INI and JSON configuration |

**For automatic deletion, use the game's Rename UI**. Naming a chest `Empty` there now requires **no console commands**. The optional legacy console `echest empty` path retains its original preview/confirm procedure and is separate from automatic game-UI actions.

To issue a diagnostic command in the Enshrouded installation directory:

```bash
mkdir -p kiln/control/console
printf 'echest nameprobe\n' > kiln/control/console/myprobe.tmp
mv kiln/control/console/myprobe.tmp kiln/control/console/myprobe.cmd
# The result file appears only after Kiln processes the command in a loaded world.
cat kiln/control/console/myprobe.result.json
```

## Original catalogue and known limitations

The shipped original [`endless-chests.defaults.ini`](defaults/endless-chests.defaults.ini) contains **195 groups**, **3,464 group entries**, **3 aliases** and **45 disabled-item entries**. The active `endless-chests.ini` is created once in the mod data directory, never overwritten on upgrade. `[Groups]`, `[Aliases]`, `[DisabledGroups]`, `[DisabledItems]` and supported `[Options]` work; owner restrictions exclude blocked entries globally. Unavailable items are skipped and logged.

The custom player string reference in Kiln's server ECS is exactly the **FNV-1a 64-bit hash of the original UTF-8 name** for two tested labels. The mod hashes its known names and compares them directly to the ECS value, without native memory access. Arbitrary names are **not decoded** (but a change to their non-zero hash is observed). Only names in the catalogue can select refill items; the special name `Empty` accepts any ASCII capitalization. Hashes are not cryptographically collision-resistant; matches among the known catalogue are checked for collisions.

Enhancing gear rarity/perks, named level variants and gem-level creation from the original ShroudForge implementation remain unsupported by ordinary Kiln Items transactions. Magic storage chests and dedicated server name replication have not yet been validated. Large groups may exceed chest capacity.

## Install and build

1. Back up your Enshrouded world/save.
2. Install a compatible Kiln release. This repo builds against the published Kiln `v0.2.0-dev.15` SDK commit.
3. Download the successful `endless-chests-kiln-package` artifact from [Package Endless Chests (Kiln)](https://github.com/bisdat/kiln-mods/actions/workflows/endless-chests.yml).
4. Extract the artifact and import its internal **`endless-chests-kiln-0.4.0.zip`** into Kiln's Library. Enable this version in the active profile, then restart Enshrouded.
5. Test `Tools → Starter Blocks → Empty` using disposable chest contents before using real supplies.

CI checks the complete INI and all known FNV hashes for collisions. Local Linux build:

```bash
KILN_UPSTREAM_SHA=eba9a5e1e0e0044a9146fdd53021a751e2957030 \
KILN_SDK_VERSION=0.2.0-dev.local.20261107.61500 \
bash scripts/build.sh EndlessChests
```

Output `bin/Release/endless-chests-kiln-0.4.0.zip`. A version-tagged release uses `endless-chests-v0.4.0`. Development continues in [PR #3](https://github.com/bisdat/kiln-mods/pull/3); parity tests are tracked in [issue #4](https://github.com/bisdat/kiln-mods/issues/4).
