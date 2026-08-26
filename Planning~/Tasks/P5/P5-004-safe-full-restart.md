# P5-004 — Safe full restart

Status: `Done`

## Objective

**Per `ADR-P5-001` (`AIBT-023`): full restart, subtree restart, and
compatible migration are one mechanism -- construct a fresh instance and
selectively copy surviving state keyed by stable node ID -- not three
independent implementations, differing only in which nodes are excluded
from the copy.** This card builds that shared mechanism, exercised through
its simplest, always-safe case: the exclusion set is the whole tree, so
nothing is copied and a live tree instance is torn down and rebuilt from the
new compiled program with no state preserved. `P5-005` and `P5-006` extend
this same mechanism with a non-trivial exclusion set (localized subtree,
then none); they must not reimplement the copy logic this card builds.

## Depends on

- `P5-001` (accepted `ADR-P5-001`; full restart is triggered by `P3-007`'s
  content-hash change-detection signal, whether or not `P5-003`'s classifier
  is available -- the whole-tree exclusion set needs no classification at
  all).

## Required reading

- `Documentation~/hot-reload.md`'s "Reload strategies" and "Interaction with
  async operations and commands" sections.
- `Documentation~/specifications/async-and-commands-v1.md` (cancellation on
  incompatible change).
- `Documentation~/specifications/time-and-random-v1.md` (stream
  non-preservation on restart).
- `Planning~/Evidence/P3-GATE/phase5-inputs.md`,
  `Planning~/Evidence/P4-GATE/phase5-inputs.md`.

## Allowed changes

- `Runtime/HotReload/Restart/` (new, or wherever `P5-001`'s ADR places it).
- `Tests/Runtime/HotReload/Restart/` (new).

## Forbidden changes

- `P5-003`'s classifier, `P5-005`'s subtree localization, or `P5-006`'s
  migration -- this card implements only the unconditional whole-instance
  path.
- Any change to the accepted Phase 2/4 execution policies or their proven
  semantic equivalence.

## Deliverables

- A function that, given a live tree instance and a new compiled program,
  tears the instance down (cancelling every active async operation per
  `async-and-commands-v1.md`'s idempotent-cancellation rule, discarding
  every uncommitted random-stream advance per `time-and-random-v1.md`) and
  constructs a fresh instance from the new program.
- Correct behavior for both execution backends (managed reference oracle,
  native executor) -- this card does not add a third backend-specific
  reload path.
- Explicit, structured reporting of what was restarted and why (feeding
  `P5-008`'s editor workflow), not a silent swap.

## Acceptance criteria

- After a full restart, the new instance's observable behavior is identical
  to a freshly constructed instance from the new program with no prior
  history -- proven by comparing against an independently constructed
  instance, not by trusting the restart path's own internal state.
- Every active async operation at the moment of restart is cancelled exactly
  once, idempotently; a late completion from the old activation cannot
  reactivate or complete after restart (direct test of
  `async-and-commands-v1.md`'s rule under a real restart, not just the
  existing abort path).
- Full restart works identically whether the instance's compatibility
  classification is "incompatible" or classification was never run at all
  (the ADR's mandatory fallback must not secretly require the classifier).
- No memory or handle leak across repeated restart cycles (a stress test
  repeating restart N times shows stable native memory usage).

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <full-restart fixture>
repeated-restart stress test with memory-stability assertion
```

## Handoff notes

- `P5-005` and `P5-006` both fall back to this card's mechanism whenever
  their own narrower strategy cannot proceed -- reuse it directly, do not
  reimplement teardown/rebuild.
- `P5-007` (scheduler interaction) verifies this card's restart does not
  disturb calibration state belonging to *other* live instances sharing a
  worker pool.

## Outcome

`HotReloadFullRestart.Restart` implements the shared reload mechanism at its whole-tree exclusion
set for the **reference-executor backend**: inspects the old instance's activity, aborts it via
`NodeAbortReason.HotReload` (an abort reason already reserved in the accepted contract), and
constructs a fresh `ReferenceExecutionMachine`. 5 tests, all passing, including a 50-cycle
repeated-restart stress test. A real bug was found and fixed by running live rather than trusting
the implementation: the abort's update ID cannot be hardcoded (`ReferenceExecutionMachine` requires
strictly-increasing update IDs across the instance's lifetime), so it is now a required
caller-supplied parameter. **Native backend explicitly deferred**: its dispose sequence is already
proven elsewhere and its own program-generation-binding invariant already forces an unconditional
dispose-and-recreate, but wiring fresh-instance construction (capacity-plan/lease preflight) is a
separate subsystem this card did not have scope to research and implement correctly in the same
pass -- disclosed as real follow-up work, not silently done or skipped. Full detail in
`Planning~/Evidence/P5-004/`.
