# Gem Forge Magic Storage (Kiln) — v0.1.2 test candidate

Bring **Archaic Essence and weapon gems** from *loaded Magic Chests* to a Gem Forge automatically. The original item entities move through the game's inventory transaction; gems retain their level and other state. The mod never spawns or deletes inventory. This is a **Kiln SDK mod**, not a ShroudLink plugin, and does **not** need ShroudLink's core or addon.

## Diagnosing a missing command response

In an **online hosted game** the Kiln chat hook should respond to `/gforge scan`. A silent command is **not** proof that the Gem Forge detector failed: first check whether the mod actually loaded and its command was registered. While in the world, open Kiln's Mods page, or inspect `kiln/state/mods.json` for `gemforge-magic-storage` and `kiln/logs/kiln.log` for `Gem Forge Storage v0.1.2 loaded` or a fault.

The command can also be run via Kiln's control folder to distinguish registration failure from chat delivery failure (including an online world whose chat hook is unavailable):

```bash
GAME="$HOME/.local/share/Steam/steamapps/common/Enshrouded"
CONSOLE="$GAME/kiln/control/console"
printf 'gforge scan\n' > "$CONSOLE/gforge-test.tmp"
mv "$CONSOLE/gforge-test.tmp" "$CONSOLE/gforge-test.cmd"
sleep 2
cat "$CONSOLE/gforge-test.result.json"
```

`found=false`: command not registered; check mod install and startup log. `found=true`, `ok=false`: read `error`; check service availability or forge proximity. `ok=true`: `output` reports loaded Magic Chests, gem counts and instance IDs. A silent in-game reply with `ok=true` means chat transport/hook is the issue. If you are **joining somebody else's world** rather than hosting, this server-side mod must be installed on **that world's host/server**.

Install the **inner** `gemforge-magic-storage-0.1.2.zip` from the GitHub Actions artifact, not the outer Actions download ZIP.

## Forge detection in v0.1.2

The v0.1.1 runtime test proved the mod was loaded, inventory moves were available and all the Kiln services were available, but `gforge scan` failed with `No Gem Forge within 4 m.` This revision checks **three independent native Kiln sources**: the `ShroudForge` ECS component, the live GemForge template name, and the `UsedItem` ID `3250908839` (`_Prop_T7_Player_GemForge`) on a placed world prop. The proximity limit is 8 metres to allow for the forge's model origin being behind the interaction point.

Use `gforge probe` through the console folder **while standing at the forge**. It is read-only, works even if the mod can't find a forge, and reports how many entities match each source and the nearest candidates with distances. Run `gforge scan` afterward to check loaded Magic Chests and gems. If all three counts are zero, report the output rather than making further changes to inventory logic.

## What to test

1. **Back up your world and character save**. Install the GitHub Actions package `gemforge-magic-storage-0.1.2.zip` into Kiln, with Kiln 0.2.0-dev.28+.
2. Enable the mod server-side (single-player hosting works). Keep **Building Tools for Kiln** enabled if you use its global Magic Chest option; it is independent and does not supply an API to this mod.
3. Put a gem and Archaic Essence in ordinary *Magic Chests*, not the player's backpack. Stand within 8 m of a Gem Forge. Use `/gforge scan` to confirm that Kiln can see the chests (read-only).
4. The bridge loans up to **24 weapon gems** (keeping 4 generic backpack slots free) and up to **2,000 units of each Archaic Essence tier**, limited by actual inventory capacity. It never transfers weapons, ordinary resources or unrecognised gem-like items.
5. Check the Gem Forge interface, insert/upgrade a gem and verify the required essence decreases **once**. Walk more than 16 m away or use `/gforge return`. Verify remaining items go back to their original Magic Chests and their item levels have not changed except through the forge.
6. Save, reload and check gem instances and quantities. Repeat with **Endless Chests** running, since an auto-refilled source chest can become full before a return.

Commands: `/gforge status`, `/gforge scan`, `/gforge return`, `/gforge help`. The return command suppresses automatic re-borrowing until you leave the forge's 8 m reach.

## Scope and deliberate limitations

- **Not a direct Gem Forge hook.** The game's forge consumes from the player's inventory. Like the MIT-licensed [ShroudLink Gemforge](https://github.com/bjck/shroudlink/blob/main/mod/mods/gemforge/src/lib.rs), this implementation loans materials temporarily; unlike that prototype, it uses Kiln's authoritative `Items.Move` which preserves the item entity. It also includes weapon gems, not just the T7 essence.
- **Loaded Magic Chests only.** The mod scans the server world's available Magic Chest inventories, at any loaded base. Building Tools' `GlobalMagicChests=1` affects crafting via separate native hooks; Kiln `Items.FindInventories` cannot read an *unloaded* distant chest. `/gforge scan` explicitly reports the available sources.
- **Transfer capacity and consumption.** A full backpack or chest can prevent a move. No replacement items are minted. The game can merge stackable essence, so return uses the original carried amount as a baseline: spending more than the loan, collecting extra essence during a visit, or handing essence to another player can affect exactly what is returned. The mod cannot detect the engine's precise per-stack consumption order.
- **Disconnect, process exit, world change, crash or hot-unload.** Any already-borrowed items are kept in the character's backpack. The in-memory original-chest ledger is not persisted; the mod does **not** manufacture compensation or attempt to return items from a stale session. Run `/gforge return` before stopping or disabling it.
- This is **unverified in-game**. Source build and policy tests do not prove that the game exposes its Gem Forge to `Nearby.Entities(..., "ShroudForge")`, or that the UI refreshes after transfer. If `/gforge scan` sees the forge and chests but the menu does not update, collect `kiln/logs/kiln.log` and report it on the PR.
- Gem instances are matched by their `ItemEntity` when returned. If a gem is socketed into a weapon or recycled, it is not taken back.

**Credits and licence:** the proximity-loan concept and `Material_T*_Fognest_Ressource` identification were informed by bjck's MIT-licensed ShroudLink gemforge. This code is independently implemented against Kiln APIs and retains attribution. Kiln and the game retain their respective licences.
