# P5-004 — Safe full restart

Status: `Draft`

## Objective

Implement the mandatory, always-available reload strategy: tear down a live
tree instance entirely and rebuild it from a new compiled program, with no
state preserved. This is the fallback every later strategy (`P5-005`,
`P5-006`) falls back to when it cannot proceed, so it must exist and be
proven correct first.

## Depends on

- `P5-001` (accepted ADR; full restart is triggered by `P3-007`'s
  content-hash change-detection signal, whether or not `P5-003`'s classifier
  is available).

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
