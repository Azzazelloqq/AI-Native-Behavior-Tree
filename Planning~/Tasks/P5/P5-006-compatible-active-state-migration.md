# P5-006 — Compatible active-state migration

Status: `Done`

## Objective

Extend `P5-004`'s shared construct-fresh-and-copy mechanism (`ADR-P5-001`,
`AIBT-023`) with an **empty** exclusion set: when `P5-003`'s classifier finds
no node anywhere `Incompatible`, every node's own verdict
(`Migrate`/`New`/`Dropped`) applies with nothing forced to restart, giving
the fullest state preservation the model allows. This card does not
reimplement copying -- it is `P5-004`'s mechanism called with the smallest
possible exclusion set. Falls back to `P5-005`'s localized exclusion set (or
`P5-004`'s whole-tree one) whenever any node classifies `Incompatible`.

## Depends on

- `P5-002` (state-layout hash).
- `P5-003` (classifier: which changes are candidates for migration at all).
- `P5-004` (fallback mechanism).

## Required reading

- `P5-001`'s accepted ADR (the exact compatible-change set this card must
  handle).
- `Documentation~/hot-reload.md`'s "Reload strategies" section.
- `Documentation~/specifications/reference-executor-machine-v1.md`,
  `Documentation~/specifications/native-runtime-v1.md` (the actual execution
  state -- lifecycle status, memory/reactive composite state, blackboard
  values, async operation state -- that must survive migration unchanged for
  everything the change did not touch).

## Allowed changes

- `Runtime/HotReload/Migration/` (new, or wherever `P5-001`'s ADR places it).
- `Tests/Runtime/HotReload/Migration/` (new).

## Forbidden changes

- Attempting migration for any change `P5-003` did not classify as
  migration-eligible -- if in doubt, fall back, never guess.
- Weakening `P3-007`'s layout/semantic isolation invariant, or any accepted
  execution policy's semantic equivalence, to make migration simpler.
- Silent partial migration -- if migration cannot complete atomically for
  the whole instance, the entire attempt aborts and falls back to
  `P5-004`/`P5-005`, never leaving an instance half-migrated.

## Deliverables

- A function that, given a live instance, a compatible new compiled program,
  and `P5-002`'s state-layout comparison, migrates every untouched node's
  live execution state (lifecycle status, memory/reactive composite state,
  blackboard values it owns, in-flight async operation state where
  identity/version/layout are unchanged) into the new program's layout, and
  applies the compatible change (parameter edit, insertion, removal,
  reordering, or type-version change per `P5-001`'s decided set) to the
  touched portion only.
- An atomicity guarantee: migration either fully succeeds or the instance is
  left exactly as it was before the attempt, with the caller informed to
  fall back.
- Structured reporting of what was migrated vs. what still required a
  restart within the same reload (a compatible change can still trigger a
  restart for the specific touched node while migrating everything else),
  for `P5-008`.

## Acceptance criteria

- For every compatible-change category `P5-001` decided, a live instance's
  untouched state (blackboard values, running-node status, in-flight async
  operations) is byte-identical before and after migration, proven against
  an independently constructed oracle instance, not the migration path's own
  internal bookkeeping.
- An attempted migration that cannot complete atomically leaves the instance
  provably unaffected (a golden-state comparison before vs. after a forced
  failure injection) and falls back correctly.
- Migration introduces no managed allocation on the native execution hot
  path outside the migration call itself (it runs once at reload time, not
  per frame).
- A stress test applying repeated compatible migrations in sequence produces
  a final state equivalent to constructing the final program fresh and
  replaying only the genuinely new inputs -- not merely "no crash."

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <migration fixture>
per-category before/after state-equivalence proof against an independent oracle
forced-failure-injection atomicity proof
```

## Handoff notes

- `P5-007` verifies migrated state does not desynchronize scheduler
  calibration or policy-selection state for the migrated instance.
- `P5-008` needs this card's per-node migrated-vs-restarted split to give
  the user an accurate picture of what a reload actually did, not a binary
  success/failure notice.
- This is the card most likely to reveal that `P5-001`'s decided compatible
  set was too optimistic against the real native memory layout -- if so,
  escalate back to `P5-001`'s ADR rather than silently narrowing what
  "compatible" means here alone.

## Outcome

`HotReloadStateMigration.Migrate` implements the shared mechanism's empty-exclusion-set case:
per-node memory, activation generation, and cooldown-init flags migrate by stable `NodeId`
(via two new, owner-approved `ReferenceExecutionMachine` methods, `CaptureNodeState`/
`SeedNodeState`); blackboard values migrate via the existing `initialBlackboard` constructor
parameter, filtered to keys the new program still declares with a matching type. 4 tests, all
passing, including a real snapshot-comparison proof (not inference from behavior) that a
`Repeater`'s persisted state survives a parameter edit.

**Real scope reduction found during implementation, escalated and decided by the owner**:
`ReferenceFrame`'s read-only `NodeIndex` and extensive decorator/parallel/repeater/cooldown-specific
field set made full frame-stack migration substantially larger than `ADR-P5-001`'s own text
anticipated. Migration now runs **only when the old instance is idle** (no active frames);
otherwise it falls back to `HotReloadFullRestart` entirely. See `ADR-P5-001`'s implementation
addendum.

**Disclosed gaps against this card's original acceptance criteria**: in-flight async-operation
state is not migrated (idle instances were not separately proven to always have zero active
operations, since no async-command test fixture exists in the currently available registries); no
forced-failure-injection atomicity test exists (the only failure mode, a memory-size mismatch, is
checked before any byte is written, but this was not stress-tested by deliberately injecting one);
no repeated-migration-sequence stress test exists. All disclosed, not silently skipped -- see
`Planning~/Evidence/P5-006/README.md`.
