# P7-033 architecture audit

Date: 2026-09-05

## Current production surface

- `ProductionTreeHost` owns exactly one `SchedulingAgent`, drives it from its own `Update`, and
  exposes only nullable per-host `StepBudget`.
- No public scheduling/profile types or population coordinator exist.
- `NativeAutoSelectionV1`, `NativeWorkEstimatorV1`, same-frame/pipelined controllers and batched
  lifecycle owner are internal Runtime types.
- P6-019's current deterministic rule prefers `Immediate` over `BatchedJobsSameFrame` for same-frame
  execution and matched the best fixed policy in all 24 re-measured Editor cases.
- The work estimator's 60.275 ns/step coefficient is derived from 42 Windows/Android Player points.
  It estimates native lifecycle work; it is not evidence for a universal whole-game AI frame budget.

## Integration gap found

`SchedulingPolicyDriver.TryRunBatchedJobsSameFrame` schedules lifecycle-machine advancement but
completes leaf dispatch from a caller-supplied `NodeStatus[]`. It is appropriate for its structural
benchmark scenarios and cannot execute a real project's custom callbacks.

The generated Burst-node pipeline separately proves real immediate and scheduled catalog execution:
generated catalogs expose `ExecuteImmediate(ref BurstExecutionBatch)` and
`Schedule(ref BurstExecutionBatch, JobHandle)`. Runtime owns dispatch workspaces and transaction
storage; Authoring owns catalog prebinding. No production type currently joins these pieces to a
compiled tree population or feeds generated callback results into lifecycle machines.

Therefore a global coordinator cannot be a thin public wrapper over the existing benchmark driver.
A production generated-dispatch integration is a prerequisite for honest
`BatchedJobsSameFrame`/`PipelinedJobs` support and for the Swarm Arena performance claim.

## Budget distinction

The existing `NativeAutoConfigurationV1.UpdateBudgetSteps` is a per-update step budget. Its presence
selects `Budgeted` before pipelined execution. A global time allowance must be a separate admission
input or every configured production scheduler would route to `Budgeted` and never exercise Jobs.

The global time allowance can only be checked between atomic segments. The coordinator can stop
admitting more work and report overruns, but cannot promise a hard wall-clock cap around arbitrary
callbacks or already-scheduled Jobs.

## Recommendation

Accept [ADR AIBT-037](../../../Documentation~/decisions/ADR-P7-033-global-scheduler-and-profiles.md)
as the behavioral direction, then split the generated-dispatch prerequisite into its own bounded
implementation card before writing the population coordinator. Keep the coordinator and profile API
in P7-033; make P7-034/P7-035 depend on both production pieces.
