# Automatic Doors — experimental 0.1.0

**Test candidate only; do not publish as a working mod yet.** Kiln currently has no dedicated door-operation API. This prototype uses Kiln.World.Update on the game's ServerToggleState component. It remains unverified whether doing so updates door animations, colliders, water blocking and NPC pathfinding. If anything looks open but blocks passage, stop testing.

## Intended behaviour

Doors open automatically at 2.5 m for players or friendly recruited NPCs and crafters. They close when all eligible actors have moved beyond 4 m and 1.5 seconds have elapsed. Vertical tolerance prevents doors on other floors opening. Only doors opened by this mod are subsequently closed; manually opened doors are left alone. Hostile creatures do not trigger doors.

The mod identifies door entities by ServerToggleState, DirectionalInteraction and InteractionToggle, rather than guessing template names or changing all switches. Configuration is hot-reloadable in the active Kiln profile's automatic-doors.json: enabled, openForPlayers, openForNpcs, openRadius, closeRadius, verticalTolerance, closeDelayMs and pollIntervalMs. Diagnostic command: /adoors status.

Current implementation checks recognised doors anywhere in the loaded world. A base-only option will require a reliable per-door Flame Altar area check; do not infer this from an unrelated nearby player's state.

## Testing

On a **backup save**, verify: (1) a closed door opens when the player approaches; (2) the animated door becomes physically passable; (3) it stays open during passage and closes only when everyone has moved clear; (4) a friendly NPC can path through; (5) a manually opened distant door remains open; (6) swing from both sides, tall gates, multistorey layouts, save/reload and removal; (7) no new errors in kiln.log. Also verify whether logic-driven doors are affected before relying on this mod.

Use GitHub Actions > Package Automatic Doors (experimental) to build the branch and download the ZIP artifact for installation in Kiln. Build locally with bash scripts/build-compat.sh AutomaticDoors.

**Technical limitation:** the tracked state write is not equivalent to the game's door interaction or the future EMF door_set API. Do not declare success without animation, collider and path-grid acceptance.

MIT. No third-party Auto Doors binaries or code are redistributed.
