# Debug Tools — Build Anywhere (Kiln)

**v0.1.0 test candidate, not yet validated in a real Enshrouded client.** A standalone Kiln-native mod to enable and disable building restrictions for one administrator's character. It does not change Survival QoL or the default `/creative` preset.

## Use

Install the package `debug-tools-0.1.0.zip` through Kiln's Library, enable it in the active profile, and restart the game. The **host/server and game client must both run compatible Kiln**; this mod runs on the host/server and uses Kiln's rules sync for the client.

In the game's Kiln command chat (as an administrator):

- `/debugbuild on` — start a session of building outside bases, including the Shroud and dungeon no-build zones when enabled in config.
- `/debugbuild status` — report *effective* permissions after server policy and other rule sets.
- `/debugbuild off` — revoke only this mod's rules. Already-placed objects are **not** deleted.

The command requires administrator permission, is scoped to its invoking character (not all players), and starts **off** every session. On disconnect, unload or world end, Kiln ends the rule set. The Kiln Configs screen exposes these hot-reloadable settings:

| Setting | Default when session enabled | Behaviour |
|---|---|---|
| `buildInShroud` | On | Clear the Shroud cursor refusal |
| `buildInDungeons` | On | Clear game no-build zone refusals (dungeons, POIs) |
| `ignoreEnemiesNearby` | On | Clear nearby-enemy refusal |
| `keepBuildsOutsideBases` | On | Ask Kiln.Build to retain placements outside existing bases |
| `freeBuilding` | Off | Build materials and dismantling refunds both set to zero |

`BuildOutsideBases` is always true **while /debugbuild is on**. Building in another player's base without permission is never enabled by this mod.

## Important: Flame Altars are an acceptance test, not a guaranteed bypass

Kiln's documented `PlayerRules` remove the build-cursor restrictions for outside-base locations, Shroud and no-build zones. Its low-level research explicitly identifies **other cursor restrictions, including Flame Altar limits and water-source limits, that these rules do not clear**. Thus this release does **not** promise every Flame Altar can be placed inside an existing dungeon or Shroud region, nor does it bypass the altar count cap. Some game/Flame-level settings may still refuse placement.

In Enshrouded 1.0 the game itself supports enabling Shroud Flame Altars via a world setting that changes the required Flame level. That setting is separate from this mod's placement rules.

**Back up the world before testing.** A build in a dungeon/POI can overlap or alter game-authored terrain and structures. Kiln.Build's persistence does not guarantee that every special object, dungeon reset, or Flame Altar ownership survives reload. Avoid testing on your only save.

## Acceptance checklist

1. With debug building OFF, confirm original placement rules.
2. ON: place and remove ordinary blocks outside any Flame Altar; restart and verify retained blocks.
3. ON: try an ordinary block in the Shroud, then a dungeon/POI.
4. ON: try a new **Flame Altar inside the Shroud**, noting global Flame level, world setting, active altar count, and exact refusal text.
5. ON: try a new **Flame Altar in a dungeon**, noting the refusal text if it fails; do not interpret a failed altar as a failure of ordinary block placement.
6. Turn off `buildInShroud` and `buildInDungeons` one at a time and confirm that their corresponding restrictions return.
7. Check `/debugbuild off` and confirm existing placements are not wiped.
8. Check `kiln/logs/kiln.log` for unavailable rules/hooks or placement errors.

## Build

From the repository root (requires git, .NET 10 SDK and internet):

```bash
python3 -m unittest discover -s DebugTools/tests -p 'test_*.py' -v
bash scripts/build.sh DebugTools
```

The [Package Debug Tools workflow](../.github/workflows/debug-tools.yml) builds against the repository's pinned Kiln SDK and uploads `debug-tools-0.1.0.zip`. **No stable release before in-game acceptance.**

## Roadmap — separate features

This PR is the *building* part of a wider debug toolkit. NoClip, recipe discovery/unlocking, item spawning, and teleportation are intentionally not represented as working controls here. Kiln already has `/creative` and an F7 creative menu for some of those tasks; we should integrate supported services rather than duplicate or emulate them.
