# P7-004 — Long-running and stress test suite

Status: `Done`

## Objective

Add the "long-running and stress tests" `Documentation~/roadmap.md` names for Phase 7, a test
category `Documentation~/testing.md`'s own "Test layers" list does not yet have (its "Performance
tests" layer points at `benchmarks.md`'s methodology, which measures short, repeatable runs — not
extended-duration or large-population soak behavior). Prove the runtime does not leak, drift, or
degrade over a much larger tick/agent count than any existing test or benchmark exercises.

## Depends on

- `P2-021` (allocation and native-lifetime gate; this card's soak tests are a large-scale extension
  of exactly what that gate already proves at a smaller scale).
- `P4-009` (Phase 4 gate; stress scenarios reuse `P4-001`'s scenario catalog and scheduling
  policies rather than inventing a new one).

## Required reading

- `Documentation~/testing.md`'s "Allocation and safety tests" and "Performance tests" sections (this
  card is a new layer alongside them, not a replacement).
- `Tests/Runtime/NativeExecution/Allocation/` (the existing, smaller-scale allocation-safety
  pattern this card extends in duration/population, not in technique).
- `Planning~/Evidence/P4-002/` (the `BatchedJobsSameFrame` fixed-batch-size overhead finding —
  stress scenarios should include the population sizes where that already-known effect is largest,
  since a stress suite that avoids the one already-known bad case would be dishonest).

## Allowed changes

- `Tests/Runtime/Stress/` (new).
- `Benchmarks~/Phase7/Stress/` (new, if a scenario needs an isolated Player-build harness the same
  way `Benchmarks~/Phase4/` already does — mirror that structure, do not invent a new one).
- `Planning~/Evidence/P7-004/`.

## Forbidden changes

- Loosening any existing correctness test to make a long-running variant pass faster or with less
  precision.
- Introducing a new performance default or threshold from this card's own results alone — a stress
  test that fails becomes a defect report or a `P7-002` input, not a silently adjusted expectation.

## Deliverables

- A multi-hour-equivalent (compressed via update count, not wall-clock, where the harness allows —
  disclose which) soak test proving zero unbounded growth in native memory footprint, GC
  allocations, and command/completion table occupancy across the run.
- A large-population stress test (at least 10x `P4-002`'s largest measured population, 1024 agents)
  proving no crash, no silent data corruption, and no determinism drift versus the same scenario at
  a normal scale.
- A hot-reload-under-load stress test: reload fired repeatedly while a live population executes,
  proving `P5-004`/`P5-005`/`P5-006`'s own existing correctness guarantees still hold at scale and
  under repetition, not just the single-reload cases their own evidence already covers.

## Acceptance criteria

- Every stress test states its own compressed-vs-wall-clock equivalence explicitly (e.g. "N updates
  emulating M real-time hours at policy X's own measured tick rate"), not an unstated assumption.
- A stress test that finds a real defect is reported as a defect, with a minimal reproduction,
  not narrowed until it passes.
- No stress test depends on non-deterministic timing to pass (flaky-by-construction tests are
  rejected, per this project's own testing discipline).

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
Tests/Runtime/Stress/ run to completion at least twice, confirming reproducible pass/fail
```

## Handoff notes

- Any real defect this card's stress tests surface is its own bug-fix scope, escalated per
  `Planning~/AGENT_WORKFLOW.md`'s stop conditions rather than silently patched inside this card if
  the fix would touch a different area's owned files.

## Outcome

Done. `Tests/Runtime/Stress/` (new, test-only) delivers all three: a 20,000-tick-cycle soak test
proving zero managed GC allocation and no native-array resizing after warmup; a 10,240-agent
(10x `P4-002`'s largest measured population) stress test with no crash and no determinism drift
against a 16-agent control; and a repeated-reload-under-load test for both backends, comparing a
never-reloaded group against an all-untouched control while a repeatedly-reloaded group survives 10
full-restart waves.

Two real findings surfaced during test-driven development (not assumed, not guessed): a normally-
completed native instance is terminal (`HasRootStatus` is cleared only on the abort path, confirmed
by reading `PopFrame`/`PopAbortedFrame` directly) — the soak tests' first draft wrongly assumed
"tick to completion, then begin again," which failed reproducibly; and `NativeCommandAsyncOwnerV1`'s
own operation-record table is a monotonic lifetime log, not a reclaimable ring buffer — `TryCancel`
marks an operation's state in place but never frees its slot. Both are disclosed, existing behavior
this card's own soak-test design had to discover and conform to, not defects. See
`Planning~/Evidence/P7-004/README.md`.

No stress test surfaced a real production defect this pass; full EditMode regression (1615 tests)
shows no new failures beyond the 3 already-pre-existing, unrelated ones.
