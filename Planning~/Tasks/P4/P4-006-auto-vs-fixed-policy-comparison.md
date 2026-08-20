# P4-006 — Auto vs. fixed-policy comparison across the scenario matrix

Status: `Done`

## Objective

Execute `Documentation~/benchmarks.md`'s "Scheduler research" step 4 — compare `Auto` (`P4-005`) against the best fixed policy per scenario across `P4-001`'s full matrix — and publish the raw comparison. This is the evidence `P4-007`'s `OQ-006` decision is made from; it does not itself decide anything.

## Depends on

- `P4-001`.
- `P4-005`.

## Required reading

- `Documentation~/benchmarks.md`.
- `P4-002`'s fixed-policy cost curves (the "best fixed policy" baseline).
- `P4-005`'s explainability surface (needed to record *why* `Auto` chose what it chose per scenario).

## Allowed changes

- `Benchmarks~/Phase4/AutoComparison/` (new).
- `Planning~/Evidence/P4-006/`.

## Forbidden changes

- Any change to `Auto`'s selection logic or any fixed policy's implementation — this card measures, it does not tune.
- Any regression threshold, default, or shipping recommendation. `P4-007` interprets this card's results; this card only records them.

## Deliverables

- Per-scenario comparison: `Auto`'s chosen policy, its recorded reason/confidence, and its measured cost, against the best fixed policy's measured cost from `P4-002`, across the full parameter matrix.
- An explicit list of scenarios where `Auto` matches, beats, or underperforms the best fixed policy, with the gap size recorded.

## Acceptance criteria

- Every scenario in `P4-001`'s catalog that applies to at least two comparable policies is included.
- Every recorded `Auto` decision includes its full explainability output (per `P4-005`), not just the resulting number — so a later reader can audit *why*, not only *what*.
- Results record environment per `benchmarks.md`'s platform-process discipline; no result is generalized beyond what was measured on this workstation.

## Required verification

```text
Verify-Static.ps1
full P4-001 harness run comparing Auto against the best fixed policy per scenario, raw samples and explainability output recorded
```

## Handoff notes

- `P4-007` (`OQ-006`) is the direct consumer: it only pursues runtime autotuning if this card's results show fixed heuristics leave meaningful performance on the table, per `benchmarks.md`'s own conditional step 5.

## Outcome

Measured `Auto` against the three same-frame-capable fixed policies across all 6 implemented
`P4-001` scenarios at 4 agent-count points (24 cases), scoped to `LatencyMode=SameFrame` since
`P4-001`'s harness was never wired to measure `PipelinedJobs` (an infrastructure gap escalated and
resolved by explicit user decision before implementation). **Result: `Auto` underperforms the best
fixed policy in 23 of 24 cases**, by +188% to +1,774% in ns/agent — reported honestly, not tuned
away (forbidden by this card). Root cause traced concretely: `Auto`'s decision tree unconditionally
prefers `BatchedJobsSameFrame` for same-frame-required large workloads, without accounting for
`P4-002`'s own finding that fixed-batch-size `BatchedJobsSameFrame` does not amortize at these
scales on this workstation. This is real evidence for `P4-007`'s `OQ-006` judgment, though not by
itself proof that runtime autotuning (rather than recalibrating `P4-005`'s own fixed thresholds)
is the right fix — see `Planning~/Evidence/P4-006/README.md`'s full analysis and the 24-row
comparison table in `Benchmarks~/Phase4/AutoComparison/README.md`.
