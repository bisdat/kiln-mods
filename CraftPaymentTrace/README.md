# Craft Payment Trace — experimental read-only observer

**Not Cross-base Storage and NOT a craft/payment mod.** Separate mod ID
`craft-payment-trace`, version `0.1.1`. Nothing in this package modifies
Enshrouded resources, source inventories, recipe choices, or native hooks.

The mod uses the *published* Kiln `Chat;Rules` service and installs one
administrator-only command, `cbtrace`:

- `cbtrace status` — list available (or absent) optional native trace APIs
- `cbtrace pair` — a **single original payer call** with its raw inputs and newly
  staged journal operations copied together; preferred over separately sampled
  `cost` and `journal` views
- `cbtrace payer` — original `InventoryPay` and `RecipePayAlt` invocation counters
- `cbtrace cost` — up to eight raw native payer cost-list entries
- `cbtrace journal` — up to eight underlying inventory entities whose take
  operations were **staged**, not necessarily committed
- `cbtrace all` — the three snapshots above, not an atomic correlated record

The `pair` snapshot is published under one native mutex. Its `SampleId`
identifies the observed original payer invocation (process-local). Both
halves refer to **the same hook invocation**, not independently chosen
last-seen calls. That is stronger evidence of input-to-staged-output
correlation, but **not proof of final commit or correct ingredient parsing**.

These methods are **optional**, found through managed reflection. On an
**ordinary, unmodified** Kiln runtime, the command correctly says
`UNAVAILABLE`; no attempts to read native memory or install detours occur.

## Important limits

The matched experimental `kiln.rules` native and managed services are
built separately from the checked-out **same published Kiln version** in
[`sdk-proposals/native-craft-trace`](../sdk-proposals/native-craft-trace/README.md).
The ordinary observer ZIP contains **neither modified service**. Do not
overwrite Kiln's host/service DLLs merely to make this command output
something; the trace build is not tested in your running game.

Even with the pair installed in a disposable, backed-up test world:
- Raw `item/count` memory offsets are hypotheses. Zero ID or zero count
  is **not** a free ingredient; category/alternative inputs remain unresolved.
- The journal records staged native take operations and a previous payer
  return code, **not final commit**, and can omit non-take operations.
- The original standalone `cost` and `journal` snapshots are sampled separately
  and may come from
  **different craft invocations**. `cbtrace all` is not an atomic snapshot.
- No remote chest access permission or Magic Chest classification is
  inferred, and no cross-base deduction occurs.

## Build

In this repository:

```bash
dotnet run --project tests/craft-payment-trace/Trace.Tests.csproj -c Release
python3 scripts/validate-craft-payment-trace.py
bash scripts/build.sh CraftPaymentTrace
```

The build selects Kiln's newest **published** development release,
and produces `bin/Release/craft-payment-trace-0.1.1.zip` with an adjacent
`kiln-sdk-provenance.json`. The
[isolated observer workflow](../.github/workflows/craft-payment-trace.yml)
uploads both files as a **separate experimental artifact**, never as part
of the ordinary Cross-base Storage ZIP or three-mod compatibility release.

The dedicated native trace build is experimental, version-specific and
not a supported Kiln API. Keep the main game runtime unmodified until its
safety and expected behavior have been verified in a disposable test setup.
