# Archive: Cross-base Storage experiments

The pre-production native Cross-base Storage implementation has been
**retired** in favour of the **working Kiln native Building Tools wrapper**.

Snapshot commit:
**78e129d201f2e5e54b86c861a4bdfc0ac4adac4d**

Full source and its history:
[archive/cross-base-storage-native-research-2026-11-09](https://github.com/bisdat/kiln-mods/tree/archive/cross-base-storage-native-research-2026-11-09)

The archive includes managed CrossBaseStorage 0.1.x diagnostics and
inventory quotes, native craft-payment and client-stock proposals,
native transaction tests, CraftPaymentTrace, original game binary
metadata, temporary upstream Kiln patchers, CI workflows and all
intermediate commits. The package built from that branch was
**diagnostic-only**; its original native hooks were never accepted
as tested in-game crafting functionality.

Do **not** install the experimental replacement kiln-rules.dll or
kiln-items.dll together with Building Tools. The hooks overlap.
No old experimental source is built or packaged in the production
adapter's branch or release.

Current user documentation:
[Building Tools for Kiln](BUILDINGTOOLS-KILN-ADAPTER.md).
