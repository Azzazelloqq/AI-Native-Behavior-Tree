# P4-002 — Fixed-policy scheduling overhead and cost curves

Status: `Draft`

## Objective

Execute `Documentation~/benchmarks.md`'s "Scheduler research" step 1 — measure scheduling overhead and fixed-policy curves for Immediate, Budgeted, and BatchedJobsSameFrame across `P4-001`'s full scenario/parameter matrix — and publish the raw results. This card produces evidence, not a policy or a default.

## Depends on

- `P4-001`.

## Required reading

- `Documentation~/benchmarks.md`.
- `P4-001`'s harness and scenario catalog.
- `Planning~/Evidence/P2-GATE/` (existing Phase 2 microbenchmark discipline this extends).

## Allowed changes

- `Benchmarks~/Phase4/CostCurves/` (new).
- `Planning~/Evidence/P4-002/`.

## Forbidden changes

- Any change to `P4-001`'s harness semantics, `Runtime/Scheduling/`, or any accepted Phase 2 code.
- Any performance default, regression threshold, or scheduling recommendation. This card is measurement only; `P4-004` calibrates from it.

## Deliverables

- Raw scheduling-overhead and cost-curve measurements for all three fixed policies across the full scenario/parameter matrix, on this workstation.
- A published, reviewable curve per policy per scenario dimension (agent count, tree shape, batch parameters), with raw samples retained separately from any chart.

## Acceptance criteria

- Every scenario in `P4-001`'s catalog that applies to a fixed policy is measured across at least three points on its relevant parameter axis (e.g. three agent-count points).
- Results record environment (Unity version, OS, CPU, logical/worker counts, build configuration) per `benchmarks.md`'s platform-process discipline; no result is generalized beyond what was measured.
- No default, threshold, or recommended policy choice is derived here — this card's own `Forbidden changes` explicitly bars it.

## Required verification

```text
Verify-Static.ps1
full P4-001 harness run across all three fixed policies, raw samples and environment recorded
```

## Handoff notes

- `P4-004` (work-estimation and batching calibration) is the direct consumer of these curves.
- `P4-006` (Auto vs. fixed comparison) needs these as the "best fixed policy" baseline.
