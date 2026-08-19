# P4-006 — Auto vs. fixed-policy comparison across the scenario matrix

Status: `Draft`

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
