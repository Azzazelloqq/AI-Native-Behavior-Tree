# P7-033 evidence

Status: **In Progress**. Steps 0-4 of `implementation-plan.md` are done; step 5 (deterministic policy
selection integrated with real population-level generated-dispatch grouping) and step 6
(explainability) remain.

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

## Remaining scope

Step 5 is the largest remaining piece: `GeneratedTreeDispatchAdapterV2` (P7-037) currently dispatches
one node for one instance per call -- no cross-instance grouping exists yet. Real `BatchedJobsSameFrame`/
`PipelinedJobs` support requires building that grouping, wiring `NativeWorkEstimatorV1`/
`NativeAutoSelectionV1.TrySelect` into the coordinator's own per-due-group decision, and driving
Immediate/Budgeted through the existing single-instance path unchanged. Step 6 (explainability
snapshot) builds on step 5's own selection/grouping decisions.
