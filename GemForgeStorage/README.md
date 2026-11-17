# Gem Forge Magic Storage

**Production version: 1.0.0** — automatic integration with Enshrouded's native Gem Forge.

Gem Forge Magic Storage makes eligible **loose gems in loaded Magic Chests at the player's current flame-altar base** available directly in the game's **GEMS** selection grid. It uses Kiln's server-authoritative native forge operations. **No console commands, manual enable switch, extra UI, temporary backpack loans, inventory copying or memory patches.**

### Requirements

- **Kiln v0.2.0-dev.29.15 or later** (Kiln mod API **0.2.7** or later) on the hosting world/server.
- The same Kiln version or newer on the player's own game client to display the added native GEMS rows. Vanilla/older Kiln clients continue to see the normal forge.
- The mod is **server-side only** (`clients=none`). For a locally hosted world, install it on the host; other players with a compatible Kiln client do not need to install this mod.
- A loaded Magic Chest with loose gems, in the same flame-altar base as the player and the Shroud Forge. The player needs the game's **AccessInventories** permission.

### Installation

Download the **gemforge-magic-storage-1.0.0.zip** release package from [GitHub Releases](https://github.com/bisdat/kiln-mods/releases). It contains the `gemforge-magic-storage/` folder. Extract the package into your Enshrouded installation's `kiln/mods/` folder, or install the packaged mod using Kiln's mod manager. Remove any older `gemforge-magic-storage` installation first; do not run two copies.

With the mod enabled in your Kiln profile, **open the Gem Forge normally**. Loose Magic Chest gems appear in the native GEMS list alongside your character's own gems. No configuration or commands are needed.

### Behaviour

- Gem discovery is automatic whenever the Shroud Forge opens. Kiln refreshes offers while the screen is open and after offered-gem actions.
- Only **loaded, same-base Magic Chests** are eligible, using `Bases.Storage`; ordinary chests, other bases, unloaded chests and other players' inventory are excluded.
- Source gems remain in their Magic Chest until an actual action commits. The game's own socket, upgrade and recycle actions validate current item instances and execute the transaction. Refused transactions do not move items.
- For **socketing and upgrading**, the forge consumes its normal Archaic Essence cost **from the player's inventory first, then the current base's crafting stock** (Magic Chests), through Kiln's `GemPayer.PlayerThenBaseStorage`.
- **Removing** a gem from a weapon returns it to the player's backpack, as the game ordinarily does. **Recycling** a loose chest gem sends the Archaic Essence reward to the player's backpack.
- The weapon receiving a socketed gem must be in the player's own inventory.
- Offers expire after 10 seconds unless refreshed; Kiln automatically withdraws them when the forge closes, the player leaves, the mod unloads or the world ends. The mod also withdraws sources when eligibility changes.
- A player can receive at most 256 live offered gem entries at once. The mod scans up to 256 crafting-stock containers at the current base, skipping a whole chest if adding it would exceed the player limit.
- If the required Kiln capabilities, base membership or inventory rights are unavailable, chest gems are not offered. No unsafe fallback or item-transfer workaround is attempted.

### Verified in-game

Tested on Enshrouded with Kiln v0.2.0-dev.29.15 / API 0.2.7 using the v0.3.0 integration code before removing its commands:

- 28 chest gems offered and displayed in the native GEMS list.
- Successful socket of a chest gem into a player-held weapon; unsocket returned the gem to the backpack.
- Gem of Overpowering upgraded **level 43 → 44** and stamina damage **100 → 102**; **172 Archaic Essence VII** deducted from Magic Chests while the player's backpack had none.
- Insufficient essence correctly refused an upgrade, leaving the gem and essence untouched.
- Recycled a chest gem and received **113 Archaic Essence** in the backpack.
- Disabling the old test-only switch withdrew offers from the native grid.

**Version 1.0.0 removes the test-only switch and diagnostic commands; otherwise the native source/payment logic remains the same.** Additional world/isolation edge cases were not individually tested. See [PR #48](https://github.com/bisdat/kiln-mods/pull/48) for the complete acceptance history.

### Developers

Build with the checksum-verified official Kiln SDK:

```bash
python3 -m unittest discover -s tests/gemforge-storage -p 'test_*.py' -v
bash scripts/build-compat.sh GemForgeStorage
```

The GitHub Actions workflow packages the mod and publishes the versioned release after integration to `main`. The historical rejection of the unsafe proximity-loan approach and earlier API research remain documented in `docs/research/GEMFORGE-DIRECT-INTEGRATION.md` and `docs/research/GEMFORGE-DEV29-PROBE-HISTORY.md`.

Source and original content: MIT License.
