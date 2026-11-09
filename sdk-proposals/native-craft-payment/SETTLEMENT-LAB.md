# Cross-base Storage — isolated original-transaction settlement lab

**Not a playable Cross-base Storage mod. Never install into your only or primary save.**

This test-only native `kiln-rules.dll` is built from the exact published Kiln
`v0.2.0-dev.20` source with `CBPAY_ENABLE_SETTLEMENT_WITNESS`. It observes
**vanilla recipe payer return values, newly staged take journal records and
the original commit/rollback calls**. It does not modify recipe prices,
source grants, Magic Chest access, stock, output or the client crafting UI.
The payment resolver at BOTH recipe hooks is still `nullptr`.

## Mandatory test conditions

1. Use a **disposable copy** of a saved world, with backups of that world AND
   the original `kiln-rules.dll`. Do not use an active multiplayer save.
2. Confirm the installed Kiln service build corresponds to the **same**
   published SDK tag and source identified in the archive metadata. Do not
   overwrite a newer auto-updated Kiln service with this older lab binary.
3. Close the game completely. Locate the installed native
   `kiln-rules.dll` under its actual `kiln` service directory and save
   the original outside the game folder. Never overwrite another filename
   just because it looks similar.
4. Install only this archive's `kiln-rules.dll` in place of the exact matching
   installed file, restart, and enter the disposable world. Keep the ordinary
   Cross-base Storage v0.1.9 mod unchanged.
5. Disable FreeCrafting and conflicting native recipe-payment mods for this
   test. Perform one low-value **normal local craft** using locally available
   materials, then attempt a missing-material or output-full craft if the
   vanilla UI permits submitting it. Note: client refusal before submission
   yields no server payer sample and is not a failure of this observer.
6. Leave the disposable world and fully stop the game. Preserve `kiln.log`.
   The marker `Cross-base test witness` reports receipt counts at world exit.
   Restore the original native DLL before returning to your normal saves.

## What constitutes evidence

- `commitOk`: original payer returned success and original commit function
  returned normally for the same live transaction.
- `rollbackFail`: original payer returned a nonzero result and original
  rollback function returned normally for the same live transaction.
- `rollbackOk`: original payer returned success, but the original rollback
  function later returned normally (e.g. output failure).
- `commitFail`: a failed payer appeared to commit; treat as an immediate
  **release blocker**.
- `partialFailedRollback`: a failed payer staged at least one **validated
  original take journal operation** before its original rollback function
  returned. It **still does not prove** storage was restored.
- A missing sample, unverifiable journal or absent native symbols must remain
  **unknown**, not accepted as proof.

**Release criteria are stricter than this lab:** independently verify exact
player + source stock before/after craft and failed craft, including save/reload;
resolve real Magic/Flame/player permissions and category/water costs; prove
client source/craftability integration. Never enable any remote payer from
these counters alone.

The lab archive is a GitHub Actions artifact only. It is not published as a
normal Kiln or Cross-base Storage release, is not auto-installed, and MUST
NOT be merged as a gameplay feature.
