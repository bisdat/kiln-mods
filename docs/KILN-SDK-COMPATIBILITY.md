# Kiln SDK compatibility CI — policy

## Purpose

The `Follow latest Kiln development release` workflow is **optional
compatibility smoke testing**, not the build or release path for normal mods.
It checks that all four active mods still compile/package against a new official
Kiln SDK and captures exact source, upstream tag, commit and SHA256 provenance.
It **cannot** establish in-game compatibility or replace acceptance testing.

Per-mod workflows (`build.yml`, `endless-chests.yml`, `noclip-toggle.yml`,
`base-station-magic-storage.yml`) build and test ordinary mod changes. They
remain the required CI gates for their own mod releases.

## Why this watcher is deliberately lean

Before this change the watcher polled twice each hour (48 scheduled triggers
per day), rebuilt four mods after source SHA changes, and created a compatibility
prerelease for each changed release/source pair. These duplicated per-mod CI,
generated unnecessary build artifacts and made an unrelated upstream launcher
patch look like a mod failure.

On 2026-11-21, [run #156](https://github.com/bisdat/kiln-mods/actions/runs/44840812099)
failed because official Kiln `v0.2.0-dev.29.23` lacked the
`Kiln.Api`/`Kiln.Sdk` NuGet packages. [That release](https://github.com/frostdev-ops/kiln/releases/tag/v0.2.0-dev.29.23)
modified only the launcher's save editor: the developers explicitly state that
its game-side code was unchanged from dev.29.22. It was **not an SDK release**,
so neither a four-mod rebuild nor a failure was warranted.

## Trigger and selection policy

| Trigger | Behaviour |
| --- | --- |
| Daily at 05:17 UTC | Light SDK resolution and cache lookup. Build four mods **only** when a previously unverified SDK version appears |
| PR changing shared SDK-compatibility tooling/tests | Revalidate and build all four against newest official SDK; no GitHub prerelease |
| Push to `main` changing shared compatibility tooling/tests | Check new SDK and cache; ordinary mod updates do not start this watcher |
| Manual `workflow_dispatch` | Reuse the SDK cache by default; use `force=true` for a complete four-mod rebuild |

The resolver selects the newest **published, version-matching SDK-bearing**
Kiln development release, not necessarily the newest launcher/runtime release.
It ignores releases that have **no** SDK packages. If the newest SDK-bearing
release has only one of the required SDK packages, lacks `SHA256SUMS`, or
uses a mutable/non-SHA release target, the workflow **fails closed** rather
than quietly accepting an older SDK. An explicit requested SDK tag must also
be complete; a requested launcher-only release does not fall back.

The cache key is the SDK version, not the mods' changing Git SHA. Compatibility
candidates always record the exact mod source SHA used in their provenance.
The per-mod CI handles later mod-source revisions until another SDK release
appears or an operator forces the check.

On a genuinely new SDK version, all four mod packages still run with checksum
verification, and a source-and-SDK-specific GitHub compatibility **prerelease**
is produced only if all packages pass. These prereleases are test candidates,
**not** stable mod releases, installation instructions, or gameplay approval.

## When to remove the schedule altogether

If these prerelease candidates and early SDK-breakage warnings are not being
used, the daily poll can be removed entirely. Keep the manual dispatch and
tooling-PR checks; no existing mod package depends on scheduled compatibility
prebuilds.
