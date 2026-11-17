# Gem Forge × Magic Chest — native Kiln integration

**Status: experimental v0.3.0; draft PR #48; NOT validated in-game, NOT released.**

This mod offers loose gems in **loaded Magic Chests in your current flame-altar base** to Enshrouded's original Shroud Forge **GEMS** grid. It uses the official Kiln **0.2.0-dev.29.15 / mod API 0.2.7** `GemForgeOpenedEvent` and `Gems.OfferChestGems` contracts. There is no custom forge UI and no proximity loan / return path.

The mod is **server-only** (`clients=none`): for a hosted single-player/friends world it runs on the hosting client. **The player viewing the forge must have Kiln 0.2.0-dev.29.15 or later installed on their own client** to see offered rows; that client does not need this mod package. Old/vanilla clients see the normal forge.

References: [upstream Kiln issue #61](https://github.com/frostdev-ops/kiln/issues/61), [SDK Gems forge](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/gems-forge.md), [native GEMS grid](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/ui-gemforge.md), [downstream draft PR #48](https://github.com/bisdat/kiln-mods/pull/48).

## Behaviour and permissions

1. **Opt-in:** the mod starts **OFF on every load/restart**. An administrator in the hosted game runs `gforge on` to enable it; `gforge off` withdraws currently tracked offers.
2. A server-side `GemForgeOpenedEvent` arrives when a player opens the native forge, periodically while open, and after an offered-gem action. Kiln identifies the actual forge within 12 m.
3. The mod requires the player to be in a base, the forge to be **in that same base**, and the player's **AccessInventories** game permission. It uses `Bases.Storage(baseId)`, then filters for real `*_Storage_*_Magic`-style storage templates and nonempty, loaded gem slots. **No chests in another base or unloaded area** are offered.
4. The mod invokes `Gems.OfferChestGems(player, chest, options)`. Kiln creates short-lived server-controlled offers and lists these gems after the player's own in the original GEMS grid. The mod never accepts client row IDs as authority; the server revalidates the slot and exact item before an action.
5. For **Insert / Upgrade**, **Archaic Essence is paid from the player's inventory first and then the current base's eligible storage** (`GemPayer.PlayerThenBaseStorage`), with the native forge price and transaction. This does **not** draw on other bases.
6. For **Recycle**, resulting essence returns to the player's inventory (the default), to avoid a full source chest preventing a return. The mod does not intervene in removal of socketed gems; vanilla player-held carriers continue to use the game's flow.
7. Only loose gems in Magic Chests are offered. Gem-bearing weapons in chests are not exposed; the **weapon/carrier being socketed must be player-held**.
8. The mod caps offers at **256 gems per player** and refreshes them on Kiln's open/refresh event, with a **10-second expiration**. It withdraws previous offers when their chests leave the eligible set. Kiln withdraws offers on screen close, player departure, mod unload or world stop.

If `gems.sources`, `gems.payers`, base services or player permissions are unavailable, the mod fails closed instead of silently transferring anything.

## Commands

Run via Kiln in-game chat / console as an administrator in the hosted world.

| Command | Purpose |
|---|---|
| `gforge status` | Show enabled state, capabilities, last offer count and refusal reason |
| `gforge scan` | Count the current base's loaded Magic Chests with loose gem slots; read-only |
| `gforge on` | Enable native offers for players with the required permissions |
| `gforge off` | Disable offers and withdraw active entries |
| `gforge help` | Summary |

After `gforge on`, open the Gem Forge (or reopen it). The normal GEMS grid should include offered gems. Use `gforge status` afterward to see the number of rows offered; the grid itself **must** be checked visually.

## In-game acceptance plan — backup your save first

Use a **throwaway backed-up world**, one weapon with an empty socket, one disposable loose gem in a Magic Chest, and a known amount of the forge's tier-specific Archaic Essence in the base Magic Chests. Turn off other inventory-moving mods while testing so they cannot refill source chests between measurements.

1. **Baseline:** mod loaded, `gforge status` says `enabled=False`. Open forge: only vanilla gems should appear. `gforge scan` must report the correct base's loaded gem slots.
2. **Native list:** `gforge on`, reopen forge; verify the disposable chest gem appears alongside backpack gems. Check `gforge status` for offered count. Check other bases do not contribute gems; no gem leaves its chest just because the menu opened.
3. **Socket test:** record the **precise source chest slot, gem item instance/level, weapon socket and essence quantities**. Select the chest gem in native UI and insert it into the player-held weapon. Verify gem removed once from source, correct socket occupied, and exactly the native cost paid from permitted stocks. No duplicate or leftover gem in backpack.
4. **Upgrade and recycle:** with a *second disposable chest gem*, test upgrade and recycle through the original UI. Check correct gem level/consumption and essence debit/return. Do not use a valuable gem for initial tests.
5. **Refusal safety:** try insufficient essence, full relevant inventory (where applicable), a locked/unauthorised base, a source chest whose gem was removed before selection, and leaving the base. Expected: action refused or the row withdrawn, **nothing lost or duplicated**.
6. **Lifecycle:** close/reopen forge, leave/join world, disable with `gforge off`, restart the game. No offers persist into another session. Verify a **vanilla client** still gets the original forge, and a Kiln client with `ui.gemforge.sources` disabled does not show offered rows.

If the native GEMS grid fails to render, or an action changes inventories incorrectly, **stop the test, restore the backup and report the exact Kiln version, source/destination item counts and kiln.log** on draft PR #48 and upstream #61. Do not merge, tag or publish before acceptance.

## Development

```bash
python3 -m unittest discover -s tests/gemforge-storage -p 'test_*.py' -v
bash scripts/build-compat.sh GemForgeStorage
```

CI builds with **checksum-verified published Kiln SDK assets** and publishes a GitHub Actions **TEST-only artifact**. These static checks do not prove the native forge transactions are safe in the game. Previous research and rejected loan/return experiments remain archived at `docs/research/GEMFORGE-DEV29-PROBE-HISTORY.md`.
