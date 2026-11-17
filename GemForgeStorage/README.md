# Gem Forge × Magic Chest — SDK 0.2.5 research

**Status: paused production integration / draft PR #48.** This folder still contains the last experimental **read-only v0.2.1 mod for Kiln dev.29**. It has **not** been ported, compiled or tested against **Kiln 0.2.0-dev.29.11 / mod API 0.2.5**. Do not mistake its `gforge` diagnostic commands for a working chest-backed Gem Forge.

The user rejected all proximity loan/return behaviour: it caused items to remain in the backpack after a chest refilled and cannot be reintroduced. **Current v0.2.1 code does not move items, alter forge actions, rewrite inventories or invent client gems.**

## What new Kiln provides

- **Client, `ui.gemforge.inspect`:** `Ui.Values.GemForgeCandidates()` / `SubscribeGemForgeCandidates` gives a **read-only**, immutable snapshot of the original native GEMS list (including off-screen entries, selected row and carrier/source distinctions). The vanilla list is only player-backed; these APIs **cannot add Magic Chest entries**.
- **Server, `gems.craft`:** `Gems.Socket/Unsocket/Upgrade/Recycle` perform engine-validated operations, including on an authorised chest with `GemCraftOptions.Holder`. **One holder is used to find both the carrier and gem.** `GemCraftOptions.Cost` does not specify a payer's inventory; costs and returns still use the player.
- Therefore a **player-held weapon plus a chest gem** is not yet directly supported, nor are native chest gem list entries or chest-paid Archaic Essence.

Official references: [UI](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/ui-gemforge.md), [forge actions](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/gems-forge.md), [Gems guide](https://github.com/frostdev-ops/kiln/blob/main/docs/kiln/services/gems.md).

## Next work and blockers

1. **Upstream:** [Kiln issue #61](https://github.com/frostdev-ops/kiln/issues/61) requests authorised cross-holder gem operation, native GEMS list/selection extension and separate chest payment source. [Kiln #42](https://github.com/frostdev-ops/kiln/issues/42) is closed after delivery of the read-only list API.
2. **When appropriate:** port the v0.2.1 diagnostic to SDK 0.2.5, replacing `Ui.Values.SubscribeSlots` as the source of native GEMS candidates with `GemForgeCandidates`. This is a **read-only** research milestone, not a shipping mod.
3. **Do not implement source/payment using transfers:** no loan queue, optimistic move/rollback of a gem, multi-step essence debit, synthetic client rows or hard-coded game RVAs.
4. **Acceptance:** demonstrate the native GEMS grid and an actual safe authoritative Magic Chest insertion/payment/upgrade/recycle/unsocket sequence before merging, tagging or publishing.

Full current [technical assessment and acceptance plan](../docs/research/GEMFORGE-DIRECT-INTEGRATION.md); historic [dev.29 UI diagnostic evidence](../docs/research/GEMFORGE-DEV29-PROBE-HISTORY.md).

## Legacy v0.2.1 diagnostic commands

These remain in the existing source and are **not new 0.2.5 APIs**:

`gforge status`, `gforge forge`, `gforge scan`, `gforge watch on/off`, `gforge recent`, `gforge ui`, `gforge ui-watch on/off`, `gforge ui-recent`.

They may be used to reproduce archived read-only observations only if the corresponding old mod is already installed. Do **not** repeat the old generic slot probing as a substitute for the new official candidate snapshot. No package/install instructions are being issued while the new version is not built.
