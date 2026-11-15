# Gem Forge Magic Storage (Kiln) — v0.1.0 test candidate

Bring **Archaic Essence and weapon gems** from *loaded Magic Chests* to a Gem Forge automatically. The original item entities move through the game's inventory transaction; gems retain their level and other state. The mod never spawns or deletes inventory. This is a **Kiln SDK mod**, not a ShroudLink plugin, and does **not** need ShroudLink's core or addon.

## What to test

1. **Back up your world and character save**. Install the GitHub Actions package `gemforge-magic-storage-0.1.0.zip` into Kiln, with Kiln 0.2.0-dev.28+.
2. Enable the mod server-side (single-player hosting works). Keep **Building Tools for Kiln** enabled if you use its global Magic Chest option; it is independent and does not supply an API to this mod.
3. Put a gem and Archaic Essence in ordinary *Magic Chests*, not the player's backpack. Stand within 4 m of a Gem Forge. Use `/gforge scan` to confirm that Kiln can see the chests (read-only).
4. The bridge loans up to **24 weapon gems** (keeping 4 generic backpack slots free) and up to **2,000 units of each Archaic Essence tier**, limited by actual inventory capacity. It never transfers weapons, ordinary resources or unrecognised gem-like items.
5. Check the Gem Forge interface, insert/upgrade a gem and verify the required essence decreases **once**. Walk more than 10 m away or use `/gforge return`. Verify remaining items go back to their original Magic Chests and their item levels have not changed except through the forge.
6. Save, reload and check gem instances and quantities. Repeat with **Endless Chests** running, since an auto-refilled source chest can become full before a return.

Commands: `/gforge status`, `/gforge scan`, `/gforge return`, `/gforge help`. The return command suppresses automatic re-borrowing until you leave the forge's 4 m reach.

## Scope and deliberate limitations

- **Not a direct Gem Forge hook.** The game's forge consumes from the player's inventory. Like the MIT-licensed [ShroudLink Gemforge](https://github.com/bjck/shroudlink/blob/main/mod/mods/gemforge/src/lib.rs), this implementation loans materials temporarily; unlike that prototype, it uses Kiln's authoritative `Items.Move` which preserves the item entity. It also includes weapon gems, not just the T7 essence.
- **Loaded Magic Chests only.** The mod scans the server world's available Magic Chest inventories, at any loaded base. Building Tools' `GlobalMagicChests=1` affects crafting via separate native hooks; Kiln `Items.FindInventories` cannot read an *unloaded* distant chest. `/gforge scan` explicitly reports the available sources.
- **Transfer capacity and consumption.** A full backpack or chest can prevent a move. No replacement items are minted. The game can merge stackable essence, so return uses the original carried amount as a baseline: spending more than the loan, collecting extra essence during a visit, or handing essence to another player can affect exactly what is returned. The mod cannot detect the engine's precise per-stack consumption order.
- **Disconnect, process exit, world change, crash or hot-unload.** Any already-borrowed items are kept in the character's backpack. The in-memory original-chest ledger is not persisted; the mod does **not** manufacture compensation or attempt to return items from a stale session. Run `/gforge return` before stopping or disabling it.
- This is **unverified in-game**. Source build and policy tests do not prove that the game exposes its Gem Forge to `Nearby.Entities(..., "ShroudForge")`, or that the UI refreshes after transfer. If `/gforge scan` sees the forge and chests but the menu does not update, collect `kiln/logs/kiln.log` and report it on the PR.
- Gem instances are matched by their `ItemEntity` when returned. If a gem is socketed into a weapon or recycled, it is not taken back.

**Credits and licence:** the proximity-loan concept and `Material_T*_Fognest_Ressource` identification were informed by bjck's MIT-licensed ShroudLink gemforge. This code is independently implemented against Kiln APIs and retains attribution. Kiln and the game retain their respective licences.
