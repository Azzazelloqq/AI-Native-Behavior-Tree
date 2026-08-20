# P4-003 PipelinedJobs executor evidence

## Result

- `Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs` (new, internal): a phase
  controller with the exact same division of responsibility as `NativeSameFramePhaseControllerV1`
  (P2-019) -- it owns only scheduling/phase authority; Snapshot, Shared reduction, and output
  owners remain the caller's job. It wraps the same `NativeBatchedLifecycleOwnerV1` unchanged (not
  modified, not reimplemented), so the ownership guard that one instance can never be scheduled
  twice concurrently is inherited, not rebuilt. The one semantic difference from the same-frame
  controller: `TryCompleteExecuteRound` is structurally refused -- a real failure with a
  diagnostic, never a silent same-frame fallback -- unless at least one `TryAdvanceStage` call has
  happened since the matching `TryScheduleExecuteRound`. `TryAdvanceStage` is a plain counter the
  caller drives once per real frame (or once per explicit pipeline stage it defines) -- never from
  wall-clock time, matching "AIBT does not assign operating-system threads." Published metrics
  (`NativePipelineMetricsV1`) add `StagesElapsed` (the exact number of stage boundaries crossed
  between an update's first scheduled round and its last completed round) alongside the same
  `UpdateId`/`SnapshotRevision`/`LaneCount`/`ExecuteRounds`/`ExecutedAtomicSteps` fields
  `NativeSameFrameMetricsV1` already reports -- this is the "pipeline delay is explicit and
  queryable by the caller" requirement, satisfied directly rather than inferred after the fact.
- `Tests/Runtime/NativeExecution/Scheduling/NativePipelinedPhaseControllerTests.cs` (new): 9 tests
  -- same-stage completion is refused (the core latency guarantee), multi-round stage accumulation
  reports the correct total `StagesElapsed`, full phase-order/update-id-monotonicity enforcement,
  the ownership guard rejects a duplicate `SchedulingOwnerId` at this controller's own `TryCreate`
  entry point (not just inherited by assumption), batch-partition invariance (1/2/3/4) across a
  real pipeline stage boundary, and zero managed allocation during steady-state driving.
- `Tests/Integration/NativeRuntime/NativeBehaviorCaseAdapter.cs` and
  `NativeExecutionEquivalenceTests.cs` (modified, scope expanded by explicit user decision -- see
  Decision below): `NativeGoldenExecutionPolicyV1` gains `PipelinedJobs`. The golden-case adapter
  (`NativeBehaviorCaseExecutor`, one tree instance per executor) routes `PipelinedJobs` to the same
  `_machine.TryAdvance` path as `Immediate` -- see the Decision section for why this is the correct
  equivalence claim for a single-instance adapter, not a shortcut. Separately,
  `NativeExecutionEquivalenceTests.RunPipelined` drives a real `NativePipelinedPhaseControllerV1`
  end to end (real `TryAdvanceStage` calls, a real refused same-stage completion attempt, real
  multi-update cycling through Snapshot/Reduce/Publish) and proves the resulting atomic-step trace
  is byte-identical to `RunImmediate`'s, on both the plain-sequence and retained-parallel
  scenarios, and that `StagesElapsed` is always positive.
- Full EditMode suite: 1323 tests (1314 + 9 new), 1320 passed; 3 pre-existing failures unrelated
  to this card (same as every prior P3/P4 evidence file:
  `AIBT.Tests.CodeGen.Generation.GeneratedArtifactContractTests` x2,
  `LocalSaveSystem.Tests.SaveStoreTests.SaveStore_AutoSave_WritesToDisk`). Confirmed via
  `git status` inside the `AIBT` submodule that this session touched only
  `Runtime/Scheduling/Native/`, `Tests/Runtime/NativeExecution/Scheduling/`,
  `Tests/Integration/NativeRuntime/`, and `Planning~/`.
- No new `[BurstCompile]` code was introduced. The only Burst job either controller schedules is
  `NativeBatchedLifecycleOwnerV1.AdvanceJob`, unchanged from `P2-019`, whose own Player/Burst
  compile evidence already covers it. This card adds no new Player-compile surface to verify.

## Decision: two boundary/interpretation questions escalated before implementation

1. **Allowed-changes scope.** This card's acceptance criteria and Required verification both
   require the golden-case/native behavior-case equivalence matrix P2-020 built
   (`NativeGoldenExecutionPolicyV1`, `NativeBehaviorCaseExecutor`,
   `NativeExecutionEquivalenceTests`), which lives in `Tests/Integration/NativeRuntime/` --  not
   listed in this card's original "Allowed changes" (only `Runtime/Scheduling/Native/` and
   `Tests/Runtime/NativeExecution/Scheduling/`). Escalated via `AskUserQuestion` before writing any
   code; the user chose to expand Allowed changes rather than duplicate the harness inside the
   originally-allowed area. The card and `work-items.json`'s `P4-003.owns` were updated to record
   this before implementation started.
2. **What "equivalence" means for a single-instance adapter.** `NativeBehaviorCaseExecutor` drives
   exactly one tree instance per test. Inside one instance, atomic steps are strictly sequential --
   step N+1 cannot even be determined until step N's dispatch completes -- so there is nothing for
   a single instance to pipeline against; pipelining is a property of when a *population's* batch
   work is submitted versus observed, not of reordering one instance's own steps. Escalated via
   `AskUserQuestion`: the user confirmed the correct reading is that single-instance behavior under
   `PipelinedJobs` is identical to `Immediate` (that identity *is* the "same observable results"
   half of the acceptance criterion for this adapter), with genuine cross-stage latency proven
   separately -- which `RunPipelined` and `NativePipelinedPhaseControllerTests.cs` do, on real
   multi-round/multi-instance scenarios where there is something to actually defer.

Neither decision weakened any acceptance criterion; both resolved a genuine premise mismatch
between the card's text and what the referenced infrastructure actually is.

## Scope and limitations

- `PipelinedJobs`'s *population-level* throughput benefit (not force-completing a Job the same
  frame it was scheduled, letting other main-thread work proceed while it completes in the
  background) is not measured here -- this card proves semantic correctness and explicit,
  queryable latency, not a throughput number. `P4-001`'s benchmark catalog carries a documented
  placeholder for this; filling it in is that card's or a follow-up's job, not this one's.
- `TryAdvanceStage` is a pure counter with no relationship to real elapsed time or actual Unity
  frame boundaries; a production caller is responsible for calling it exactly once per the frame
  or stage boundary it wants to expose, and nothing here prevents a caller from advancing it
  incorrectly (e.g., twice in one frame) -- that misuse is out of this card's scope in the same way
  `NativeSameFramePhaseControllerV1` doesn't validate that its own caller drives it once per real
  frame either.
- No `Auto`/autotuning selection logic exists yet (forbidden by this card); nothing here selects
  `PipelinedJobs` automatically.

See `verification-results.json` for exact commands and results.
