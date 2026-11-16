# Gem Forge Integration Research — v0.2.1 (read-only)

**Experimental investigation, not a Gem Forge integration or production mod.** The original v0.1.x proximity-loan/return feature was rejected after an in-game failure: gems could remain in the backpack when their original chest became full. **All item-moving code has been removed from this research build.** It never calls `Items.Move`, `Items.Give`, `Items.Take`, `Events.Cancel`, `Hooks.Detour` or any other state-changing API.

## Dev.29 client UI inspection (new in v0.2.1)

This build compiles against Kiln **0.2.0-dev.29** and runs on **both** the client and server sides, because the old server-only mod cannot see the client's screen or rendered Gem Forge slots. This is a diagnostic decision for a hosted solo game; joining a remote server with this installed would also require the corresponding mod on the server. Do not publish a production package from this probe.

The new **`ui-watch`** mode subscribes to `Ui.Values.SubscribeSlots` and `Ui.Screens.Changed`. These read the last client-rendered slots and identify the screen type (`HudScreen.ShroudForge=45`). **No slot is injected, and no gem is moved.** Its client entity IDs are *never equated* to the server's chest/item entities. The callback is throttled, with a 30-record bounded history and no game-state API calls.

At your placed Gem Forge, in the hosted world:

```bash
GAME="$HOME/.local/share/Steam/steamapps/common/Enshrouded"
CONSOLE="$GAME/kiln/control/console"
printf 'gforge status\ngforge ui\ngforge ui-watch on\n' > "$CONSOLE/gforge-ui-start.tmp"
mv "$CONSOLE/gforge-ui-start.tmp" "$CONSOLE/gforge-ui-start.cmd"
sleep 2
cat "$CONSOLE/gforge-ui-start.result.json"
```

Open the Gem Forge, select or browse some gems **without applying an upgrade or recycling anything**, then close the UI. Collect the client observations:

```bash
printf 'gforge ui\ngforge ui-recent\ngforge ui-watch off\n' > "$CONSOLE/gforge-ui-findings.tmp"
mv "$CONSOLE/gforge-ui-findings.tmp" "$CONSOLE/gforge-ui-findings.cmd"
sleep 2
cat "$CONSOLE/gforge-ui-findings.result.json"
```

If the UI screen is detected but no gem slots appear, inspect `kiln/logs/kiln.log` for `Gem Forge Research UI:` lines. This tells us whether the forge shares Kiln's tracked inventory-slot renderer; it does **not** demonstrate the native forge's underlying item provider or enable Magic Chest items in that provider.

## In-game result and correction — 16 November 2026

The v0.2.1 client UI probe **worked** on Kiln dev.29: `HudScreen.ShroudForge` identified and 3,357 forge UI frames captured. The sampled UI draws included two gem IDs (`0xe52a5ac0` = **Gem of Storm's Eye**, `0x03c1b61e` = **Gem of Focused Light**) with unresolved client holders, plus three player-owned weapons (**Electric Initialiser**, **Dragoncore Staff**, **Wolf's Hydra**).

**The user confirmed that both displayed gems are already socketed in their weapons and are shown as available for removal only.** Their `holder=0` rendered slots therefore do **not** demonstrate a new-gem inventory source, and must not be mistaken for items in Magic Chests. `Wolf's Hydra` is the test weapon with an **empty gem socket**.

**Next observational test (no new build):** with `gforge ui-watch on` enabled, select **Wolf's Hydra → empty socket → choose a gem for insertion**, but **do not actually insert one**. Close the forge and collect `gforge ui`, `gforge ui-recent`, `gforge ui-watch off` as shown above. If the short history misses the insertion list, the full bounded logs prefixed `Gem Forge Research UI:` in Kiln's log may retain it. This distinguishes socketed-gem removal views from the genuine new-gem insertion picker. No new state-changing code is proposed.

## Verified facts

- The user's hosted Enshrouded world reported **zero entities** with the `ShroudForge` ECS component.
- A placed Gem Forge was found at entity **5881**, 1.7m away, using the native `UsedItem` item ID `3250908839` (`_Prop_T7_Player_GemForge`). Its runtime template was `Shroud_Gem_Forge_07_AncientLands_Premium_PLAYER_CRAFTED`.
- Kiln `Items.FindInventories` listed **133 loaded Magic Chests** and 57 remaining gem entries in the previous test. That API does not expose distant *unloaded* storage.
- Kiln's `PlayerClientActionEvent` includes a game-reflected **28-byte `weaponGemAction`** and the raw payload; Kiln can observe it before the game's handler. This event is not a documented hook for the forge's inventory-selection UI or its actual debit/commit transaction.

## Read-only test procedure

**Before upgrading:** Ensure no v0.1.x loan session is still returning, and manually place any leftover lent gems from the old version into the desired Magic Chest. The research build intentionally **cannot repair or reassign** items already left behind by v0.1.x. Back up the character/world before any future write experiments. Disable/remove v0.1.x before installing this new build.

Install the inner `gemforge-magic-storage-0.2.1.zip` from the PR's successful GitHub Actions artifact using Kiln. The mod retains `gforge` commands but has completely different, read-only behaviour.

With the game running, stand at the placed Gem Forge and write commands to Kiln's control console:

```bash
GAME="$HOME/.local/share/Steam/steamapps/common/Enshrouded"
CONSOLE="$GAME/kiln/control/console"
printf 'gforge status\ngforge forge\ngforge scan\ngforge watch on\n' > "$CONSOLE/gforge-investigate.tmp"
mv "$CONSOLE/gforge-investigate.tmp" "$CONSOLE/gforge-investigate.cmd"
sleep 2
cat "$CONSOLE/gforge-investigate.result.json"
```

Now **only perform a safe, non-consuming UI action initially**: open the Gem Forge, select a weapon or gem, inspect choices or cancel the selection. The UI may not send `weaponGemAction` until you actually insert/upgrade/recycle something; do not consume items merely to force a result. Then retrieve observations:

```bash
printf 'gforge recent\ngforge watch off\n' > "$CONSOLE/gforge-findings.tmp"
mv "$CONSOLE/gforge-findings.tmp" "$CONSOLE/gforge-findings.cmd"
sleep 2
cat "$CONSOLE/gforge-findings.result.json"
grep -F 'Gem Forge Research: weaponGemAction' "$GAME/kiln/logs/kiln.log" | tail -n 20
```

`gforge recent` reports up to six of the latest records. Each record reports 28 raw bytes and seven **uninterpreted little-endian dwords**; do not assume the meanings of the fields until they are correlated with real operations. All logs are bounded (40 records in memory). Run `gforge clear` to clear the diagnostic history.

`gforge watch on` dynamically attaches a passive `PlayerClientActionEvent` listener for that player. While subscribed, Kiln's documented action delivery **holds an observed client command by one simulation step** (~17ms) so that callbacks can inspect it. This version **never cancels or changes** any action or item. Use `gforge watch off` to detach the listener and eliminate the one-step delay. Only `weaponGemAction` within 12m of a detected placed Gem Forge is recorded.

## In-game result — 15 November 2026

User's Bazzite/Proton **hosted** game with v0.2.1:

- `gforge status`: read-only; playerstate=True; `weaponGemAction=28 bytes, canCancel=True`; all World/Spawn/Items services available.
- `gforge forge`: placeable forge entity **5930**, distance **3.0m**, template **`Shroud_Gem_Forge_07_AncientLands_Premium_PLAYER_CRAFTED`**, no `ShroudForge` ECS component.
- `gforge scan`: **133 loaded Magic Chests**, **85 weapon gem slots**, **13,750 units of Archaic Essence**; no items transferred.
- After opening the forge and browsing the selection UI with `gforge watch on`, `gforge recent` reported **no `weaponGemAction`**. This only establishes that *those UI operations* caused no captured server gem action, not that the real forge action cannot be observed.

The current Kiln source has a critical reverse-engineering lead: `docs/re/items.md` section 16 identifies the server-side `equipment_actions` program and its `0x144bb0` weapon-gem helper (server binary 26430253), which emits `GemCraftingEvent`. **The RVA is build-specific, not tested as a hook on the user's live executable.**

The game client exposes `HudScreen.ShroudForge=45` through `Ui.Screens.Current`. Upstream also documents `Ui.Values.Slots/SubscribeSlots`, a read-only snapshot of what the game rendered, but explicitly places those hooks in **Kiln dev.29**; dev.29 has now been published (2026-11-16) and is the basis for this version. Even those slot snapshots only inspect items that the game already drew: they cannot add entries to the forge selection UI.

See [the native integration research record](../docs/research/GEMFORGE-DIRECT-INTEGRATION.md).

## Research tasks before a functional mod

1. **Map the 28-byte action** with controlled, read-only comparison of UI interactions, including which operations generate it. Its field layout and processing function are not yet established.
2. **Locate the client forge UI item-provider function** (or a supported Kiln UI/service extension). The desired UI must enumerate items *in Magic Chests* without moving them to the player.
3. **Locate the native server operation** for insert / unsocket / upgrade / recycle; determine authoritative gem consumption, durability/state rules, item instance tracking, rollback, and its interaction with Archaic Essence.
4. **Define a safe server transaction** that debits a source chest exactly once only when the vanilla forge operation succeeds. Do not emulate an upgrade by cloning gems or forging a replacement item; test against game saves with backups.
5. **Cross-base requirements:** `Kiln.Items` operates on loaded inventories; Building Tools' global-magic-chest functionality uses a distinct system. Unloaded-base retrieval must be investigated separately; do not claim it works without proof.

The work remains in **draft PR #48**, with no merge or release until an actual native implementation is proven in game.

**References:** [Kiln's player states and client actions](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/modding/player-states.md), [Kiln hooks](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/hooks.md), [Kiln Items](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/items.md), and [ShroudLink's historical Gem Forge mod](https://github.com/bjck/shroudlink/blob/main/mod/mods/gemforge/src/lib.rs). The ShroudLink mod is not used in the new implementation.
