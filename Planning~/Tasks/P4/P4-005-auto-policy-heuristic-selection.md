# P4-005 — Auto policy: deterministic explainable heuristic selection

Status: `Done`

## Objective

Implement the `Auto` execution policy — deterministic, explainable selection among the supported fixed policies (`Immediate`, `BatchedJobsSameFrame`, `Budgeted`, `PipelinedJobs`) based on `P4-004`'s work-estimation model and the caller's configured latency/budget constraints — plus the full explainability and override surface `Documentation~/execution-and-scheduling.md` specifies. This is `benchmarks.md`'s "Scheduler research" step 3.

## Depends on

- `P4-003` (`PipelinedJobs` must exist to be selectable).
- `P4-004` (the estimation/batching model `Auto` selects from).

## Required reading

- `Documentation~/execution-and-scheduling.md` (all sections, especially "Explainability and overrides" and "Semantic guarantees").
- `Planning~/OPEN_QUESTIONS.md` (`OQ-006` — this card implements the deterministic-heuristic half only; runtime autotuning is `P4-007`'s separate, conditional scope).

## Allowed changes

- `Runtime/Scheduling/Native/Auto/` (new).
- `Tests/Runtime/NativeExecution/Scheduling/Auto/` (new).

## Forbidden changes

- Runtime/online adaptation of the selection heuristic itself (`OQ-006`'s autotuning question is `P4-007`'s separate, conditional card — this card is the fixed, deterministic heuristic baseline `P4-007` compares against).
- Opting into extra semantic latency (e.g. silently picking `PipelinedJobs` when the caller needs same-frame results) without the caller's explicit permission, per the spec's own rule.
- Any change to an individual policy's own semantics established by its own card.

## Deliverables

- Deterministic policy selection: given the work estimate and configured allowed latency/budget, choose among the supported policies using an explainable rule (not a black-box model).
- The full explainability surface: chosen policy and reason, workload estimate and confidence, batch size and count, scheduling and completion cost, worker-utilization proxy, node steps/commands/wakeups/deferred agents, and comparison with the configured budget.
- The full override surface: force a specific policy, minimum job workload, target batch work, batch bounds, update budget, latency mode, and tree-specific update cadence.
- Structured-diagnostic rejection (not silent substitution) when a caller forces a policy unavailable on the active backend.

## Acceptance criteria

- `Auto` never selects a policy with more latency than the caller's configured allowed latency permits, and never does so silently — every selection is queryable via the explainability surface.
- Forcing an unsupported policy on the active backend (e.g. `PipelinedJobs` on a backend that only exposes `SingleThreadImmediate`/`SingleThreadBudgeted`) returns a structured diagnostic; it is not silently replaced.
- For at least the scenarios in `P4-001`'s catalog, `Auto`'s selection is deterministic and reproducible given identical inputs (same estimate, same configuration -> same choice, every time).
- Every field the explainability surface promises is populated and independently verifiable against the same run's raw scheduling data.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <Auto policy fixture>
policy-forcing negative tests (unsupported backend/policy combinations)
determinism-on-rerun test
```

## Handoff notes

- `P4-006` runs this policy across the full benchmark matrix to compare it against the best fixed policy per scenario.
- `P4-007` is the separate, conditional card for runtime autotuning (`OQ-006`); this card's heuristic is the fixed baseline that comparison is measured against.

## Outcome

Implemented `NativeAutoSelectionV1.TrySelect`: a deterministic decision tree among `Immediate`,
`Budgeted`, `BatchedJobsSameFrame`, and `PipelinedJobs`, driven by `P4-004`'s work estimate, a
caller-supplied policy-capability set (no Web backend exists anywhere in this package to detect
real platform capability from), and the override surface (forced policy, minimum job workload,
target batch work, batch/memory bounds, worker count, update budget, latency mode, update
cadence). A forced policy always wins if supported and latency-consistent; automatic selection
picks `Immediate` below a minimum-workload threshold, `Budgeted` when a budget is configured,
`PipelinedJobs` when large and pipelining is explicitly permitted, `BatchedJobsSameFrame` when
large and same-frame latency is required, with a final fallback to whatever single policy remains
available. Before implementation, the explainability surface was escalated and narrowed to fields
with a genuine, verifiable data source (`Documentation~/execution-and-scheduling.md`'s full list
includes commands/wakeups/deferred-agents and a real per-batch scheduling cost, none of which any
existing type in `Runtime/Scheduling/Native/` tracks) — a documented gap, not a faked field, per
`Planning~/Evidence/P4-005/README.md`'s Decision section. 24 tests cover every branch, both
forced-policy rejection paths (unsupported backend and a latency-mode contradiction), and
determinism against all 6 real `P4-001` catalog scenarios.
