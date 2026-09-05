# P7-033 evidence

Status: **In Progress**. Steps 0-4 of `implementation-plan.md` are done. Step 5's cross-instance
generated-dispatch grouping mechanism is built and proven; wiring it into the coordinator's own
deterministic policy selection (step 5's remainder) and step 6 (explainability) remain.

## Steps 2-4 (2026-09-05)

`Runtime/Scheduling/Production/`:

- `SchedulingPolicy.cs` -- public mirror of the internal `NativeAutoPolicyV1` set.
- `SchedulingProfile.cs` -- immutable runtime profile value. `TryCreate` validates every field
  without silent clamping (empty ID, zero cadence, a latency cap below cadence, an out-of-(0,1]
  budget share all fail closed with a structured `SchedulingProfileValidationError`). Built-in
  `Normal`/`Interactive`/`Background` presets differ only by `Priority` (pure ordering) and, for
  `Background` alone, `PipeliningPermitted` (justified by the preset's own name, not a fabricated
  timing constant) -- no invented cadence/latency/budget numbers, per the ADR's own "no illustrative
  number... without evidence" constraint.
- `SchedulingProfileAsset.cs` -- `ScriptableObject` wrapper; `TryFreeze` produces the same validated
  runtime value, bumping `SchedulingProfile.Revision` only after a real Inspector edit (`OnValidate`).
- `ProductionTreeScheduler.cs` -- the coordinator. `TryRegister(host)`/`TryRegister(host, profile)`
  (owner's own explicit ask: a profile-less overload defaults to `SchedulingProfile.Normal`, i.e.
  Auto). `ProductionTreeHost.Update()` was refactored to a one-line owner check delegating to a new
  internal `DriveOneUpdate()` -- identical body to the prior `Update()`, so standalone behavior is
  byte-for-byte unchanged (all 20 pre-existing `ProductionTreeHostTests` pass unmodified). Due
  ordering follows the ADR's own five-tier rule exactly (deadline, eligible-since age, one-shot
  urgency, profile priority, stable `InstanceId`) via a single `Comparison<Registration>` over a
  per-frame snapshot list -- deliberately a snapshot, not a live view, so a host's own dispatch
  callback can safely register/unregister any host (including itself or another not-yet-driven one)
  without corrupting the loop; a small `_byHost.ContainsKey` guard re-checks each entry is still
  actually registered immediately before driving it, closing the one gap a snapshot design would
  otherwise leave (a same-frame unregister of a not-yet-driven host, live-reproduced by a dedicated
  test before being fixed). Budget admission supports `Unbounded`/`Fixed`/`Provider` modes, per-profile
  `MaximumBudgetShare` caps (tracked independently per profile so a capped group's own limit never
  blocks a different group, and never redistributes an unused share back to itself), and never guesses
  an unmeasured host's cost -- it is always admitted on its first-ever drive, with `LastFrameOverran`
  reporting that this happened.

## Verification

- 54 new tests (`Tests/Runtime/Scheduling/Production/SchedulingProfileTests.cs`,
  `ProductionTreeSchedulerTests.cs`), all passed live against the real `6000.5.8f1` Editor via Unity
  MCP, including genuine re-entrant-mutation and starvation-protection proofs (not simulated).
- Full host EditMode regression: 1778/1778 current, the same 3 pre-existing unrelated failures as
  every prior Phase 7 card (2 CodeGen `PackageInfo` assertions, 1 `LocalSaveSystem` autosave test).
- A real environment quirk found and designed around, not assumed: `Thread.Sleep(2)` measured ~15-20ms
  in this environment (Windows timer-resolution granularity), not 2ms -- the one test needing a
  precise budget/cost proportion (`Budget_ShareCap_...`) derives its numbers from a real measured cost
  read back via reflection rather than an assumed sleep duration; the other Sleep-based tests only
  needed an order-of-magnitude relationship and were already robust to this.
- `Verify-Static.ps1` and `git diff --check` passed. Generated API docs regenerated for the new
  additive public surface via `AIBT/MCP/Regenerate Documentation`.

## Step 5, part 1: cross-instance group dispatch (2026-09-05)

`GeneratedTreeDispatchAdapterV2` (P7-037) previously dispatched exactly one node for one instance
per call -- no cross-instance grouping existed. Built the grouping mechanism required for real
`BatchedJobsSameFrame`/`PipelinedJobs` support:

- `Runtime/Integration/GeneratedDispatch/GeneratedDispatchGroupParticipantV2.cs` -- one agent's own
  request/config/memory/binding data, prepared without copying its persistent storage.
- `GeneratedTreeDispatchAdapterV2.TryPrepareGroupParticipant`/`TryCommitGroupParticipant` -- the
  adapter's own per-agent prepare/commit halves of the existing single-instance `DispatchSlot.Execute`
  preamble/postamble, reused rather than duplicated.
- `GeneratedDispatchGroupExecutorV2.TryExecuteGroup` -- builds one shared
  `NativeBurstDispatchBatchOwnerV2` batch spanning every participating agent's own request, executes
  it as exactly one immediate or scheduled call, and commits each participant's own result back
  through its own adapter. A rejected/faulted batch commits no participant's memory or blackboard
  state.
- `ProductionTreeHost.TryAdvanceToNextDispatch`/`CompletePendingDispatch` -- inverted the host's own
  drive loop so a coordinator-driven group wave can pause a host mid-segment at exactly its next
  `DispatchRequired` step and resume it later with the group's own result, instead of the host
  driving itself start-to-finish per `RunSegment`.

Two defects found and fixed empirically (via live Unity MCP bisection, not guessed):
1. `NativeBurstDispatchBindingInputV2`'s `completions`/`completionPayloadBytes` were constructed with
   `default` instead of a genuinely-allocated zero-length array; `default(NativeArray<T>.ReadOnly)`
   reports `IsCreated=false`, failing the validator's own enabled-branch consistency gate that the
   single-instance `DispatchSlot.TryCreate` path already satisfies by convention.
2. The shared batch copied each participant's `TargetOrdinal` verbatim. `TargetOrdinal` is a
   structural blackboard-slot index, identical across every instance of the same tree type by
   design -- so two different agents' own, separately-stored slot 0 collided and were rejected by
   the validator's own same-identity consistency check (`NativeBurstDispatchBindingValidationV2`,
   unmodified -- confirmed by a pre-existing test, `LiveValueRanges_OnlyExactSemanticAliasesAreAccepted`,
   that intentionally locks in "same (Scope, TargetOrdinal) implies same live storage" independent of
   tree instance). Fixed by shifting each participant's own target ordinals into a disjoint range
   before building the shared batch, exactly mirroring the existing `LiveValueOffset` shift.

Verified live: `GroupDispatch_TwoInstances_ShareOneCatalog_AndReachSuccessThroughOneBatchPerWave`
drives two `ProductionTreeHost`s sharing one catalog to `NodeStatus.Success` through exactly one
`NativeBurstDispatchBatchOwnerV2.TryCreate` call per wave (3/3 waves grouped both instances). Full
regression: 796/796 in the directly-relevant assemblies (`AIBT.Runtime.Tests`,
`AIBT.Integration.Tests`, `AIBT.NativeBurstDispatch.Tests`); 613/615 in the remaining assemblies, the
2 failures pre-existing and unrelated (`GeneratedArtifactContractTests` package-path resolution).

## Remaining scope

Step 5, part 2: wire `GeneratedDispatchGroupExecutorV2` into `ProductionTreeScheduler`'s own
admission loop -- build supported-policy masks from backend/catalog capability and profile latency
permission, feed estimates into the existing deterministic `NativeAutoSelectionV1.TrySelect` per
due-group, and drive Immediate/Budgeted through the existing single-instance path while Jobs
policies route through the new group executor, all through one result contract. Needs tests for
forced-policy contradictions, deadline deferral, non-preemptible overrun and disposal while a group
batch is outstanding. Step 6 (explainability snapshot) builds on step 5's own selection/grouping
decisions.
