# P4-004 work-estimation and batching calibration model evidence

## Result

- `Runtime/Scheduling/Native/Estimation/NativeWorkEstimatorV1.cs` (new, internal struct):
  implements `estimated work = runnable agents x expected node steps per agent x calibrated
  node-cost units`. One instance tracks the smoothed "expected node steps per agent" for one
  compiled-program identity/population (the caller keys one estimator per distinct program; this
  type does not do that bookkeeping). `TryObserve(agentCount, totalSteps)` feeds one real
  completed-round observation; the first observation seeds the estimate directly, every later one
  is clamped to +-50% of the prior estimate before a 0.25-weight exponential-smoothing blend --
  so a single observation can move the smoothed estimate by at most 12.5%, however extreme the raw
  spike is (proven directly with a 100x synthetic spike). `CalibratedNanosecondsPerNodeStep =
  678.75` is the pooled median of `elapsedNanoseconds / totalSteps` across all 360 Immediate-policy
  samples in `P4-002`'s recorded cost curves (Immediate specifically, since `P4-002` already showed
  its per-agent cost has no batching overhead to contaminate the per-step figure). Coefficients are
  compile-time constants with their calibration source documented in the type's own XML doc
  comments and recalibrated only by re-running `Benchmarks~/Phase4/CostCurves/` -- never adjusted
  online, per this card's forbidden-changes clause.
- `Runtime/Scheduling/Native/Estimation/NativeBatchSizeCalibrationV1.cs` (new, internal static
  class): implements `batch size = target batch work / estimated work per agent`, clamped to
  `[policyMinBatchSize, policyMaxBatchSize]` and further to `memoryLimitBatchSize` (memory always
  wins over policy when the two conflict -- a batch the memory budget cannot hold can never be
  scheduled regardless of policy preference), then adjusted so the resulting batch count reaches
  at least `workerCount` when the population supports it (the spec's "enough batches for worker
  load balancing without flooding the job queue"), never shrinking below any floor.
- `Tests/Runtime/NativeExecution/Scheduling/Estimation/NativeWorkEstimatorTests.cs` (new, 8 tests
  including 24 `[TestCase]` correlation points) and
  `Tests/Runtime/NativeExecution/Scheduling/Estimation/NativeBatchSizeCalibrationTests.cs` (new, 8
  tests): 39 tests total, all passing.
- **The correlation check is real, not asserted**: all 24 (scenario, agent count) points actually
  measured in `Benchmarks~/Phase4/CostCurves/Results/cost-curves-windows-editor-20260820.json`
  (Immediate policy) are hardcoded as test fixtures. Feeding each point's real `(agentCount,
  totalSteps)` into a freshly-seeded estimator and comparing the resulting per-agent estimate
  against that same point's actually-measured median cost, every single point lands within 10% --
  the worst observed deviation across all 24 points is 8.71% (`deep-sequence-selector-traversal`
  at 256 agents: estimate 192,086.25ns vs measured 176,699.22ns). `CalibrationTolerance = 0.10` is
  set from this real result, not assumed in advance.
- Full EditMode suite: 1362 tests (1323 + 39 new), 1359 passed; 3 pre-existing failures unrelated
  to this card (same as every prior P3/P4 evidence file). Confirmed via `git status` inside the
  `AIBT` submodule that this session touched only `Runtime/Scheduling/Native/Estimation/` and
  `Tests/Runtime/NativeExecution/Scheduling/Estimation/` (both new directories, no existing file
  modified) plus `Planning~/`.

## Calibration derivation (full detail)

For each of `P4-002`'s 6 implemented scenarios x 4 agent counts (24 points), Immediate-policy
`totalSteps` is deterministic (verified: every sample at a given scenario/agentCount reports the
identical `totalSteps`, confirming no randomness). Dividing each point's `totalSteps` by its
`agentCount` gives `stepsPerAgent`; multiplying by a single global coefficient and comparing
against that same point's measured `medianNanosecondsPerAgent` gives the per-point error the table
below records (all real numbers, pulled directly from the P4-002 JSON):

| Scenario | agentCount | stepsPerAgent | estimate (steps x 678.75ns) | measured | ratio |
| --- | ---: | ---: | ---: | ---: | ---: |
| scheduling-baseline-empty-job | 16/64/256/1024 | 4 | 2,715.00 | 2,845-2,913 | 0.93-0.95 |
| shallow-tree-cheap-conditions | 16/64/256/1024 | 23 | 15,611.25 | 15,214-15,485 | 1.01-1.03 |
| deep-sequence-selector-traversal | 16/64/256/1024 | 283 | 192,086.25 | 176,699-178,592 | 1.08-1.09 |
| wide-branching-frequent-failures | 16/64/256/1024 | 8 | 5,430.00 | 5,104-5,167 | 1.05-1.06 |
| predominantly-running-actions | 16/64/256/1024 | 5 | 3,393.75 | 3,346-3,550 | 0.96-1.01 |
| many-programs-small-populations | 16/64/256/1024 | 4 | 2,715.00 | 2,868-2,954 | 0.92-0.95 |

The single-leaf scenarios (`scheduling-baseline-empty-job`, `many-programs-small-populations`) run
slightly *below* the global coefficient (~7% under) and the deepest tree
(`deep-sequence-selector-traversal`) runs slightly *above* (~8% over) -- a small, explainable,
per-shape systematic bias: a short tree's fixed per-update dispatch overhead is amortized over
fewer steps, and a very deep tree's overhead is amortized over more, so the *average* ns/step
naturally differs a little by tree shape even though the underlying dispatch mechanics are the
same. `CalibrationTolerance = 0.10` was set after seeing this real spread (max observed 8.7%,
verified per-point in the test fixture above, not per-scenario-median as summarized in this table),
leaving deliberate margin rather than being tuned exactly to the worst case.

## Decision

- **One global coefficient, not per-node-cost-category.** `P4-002`'s data supports only 6
  scenario-level measurements, not a genuine per-node-type cost breakdown (blackboard-heavy,
  command-heavy, async-heavy leaves do not exist as measured scenarios yet -- `P4-001`'s own
  documented placeholders). Building a false-precision multi-category model the data cannot
  actually support would be worse than one honestly-derived global constant with its ~8.7%
  real-measured spread disclosed. If genuinely differentiated node-cost categories are ever
  measured, recalibrating to per-category coefficients is future work, not something this card
  needed to anticipate (its own handoff notes agree: "recalibrating... is expected future work").
  This is a self-directed engineering choice within the card's own latitude (`Documentation~/execution-and-scheduling.md`'s
  formula does not mandate a specific number of cost categories), not an architectural escalation.
- **"Compiled-program identity" keying is the caller's job, not this type's.** `NativeWorkEstimatorV1`
  is a plain struct with no dictionary/identity-keyed storage; a caller constructs and owns one
  instance per distinct compiled program (or population), matching this codebase's existing
  pattern of owning identity-keyed state at the call site rather than inside a shared utility type.
- **Load-balancing rule is a documented heuristic, not derived from `P4-002` data.** The spec's
  "enough batches for load balancing without flooding the queue" has no formula in
  `Documentation~/execution-and-scheduling.md`; `NativeBatchSizeCalibrationV1`'s interpretation
  (shrink toward `ceil(agents/workerCount)` batches when the raw estimate would under-use available
  workers, never below the policy/memory floor) is a reasonable, testable, documented choice, not
  a claim that it is the optimal policy -- `P4-005`/`P4-006` are where policy selection and
  comparison actually happen.

## Scope and limitations

- Calibration is specific to this one workstation's `P4-002` run (Unity 6000.5.8f1, Intel Core
  Ultra 9 275HX); no claim is made about accuracy on other hardware, per this card's own
  forbidden-changes clause. Recalibrating for a new platform means re-running
  `Benchmarks~/Phase4/CostCurves/` and updating the two constants in
  `NativeWorkEstimatorV1.cs`, not touching this model's structure.
- The smoothing/bounding mechanism is proven correct in isolation (synthetic spike tests) and the
  static coefficient is proven to correlate with real measured data, but the two are not proven
  together as a live feedback loop against a real running scheduler yet -- that integration is
  `P4-005`'s (`Auto`) job, which this card's own forbidden-changes clause explicitly reserves.
- No `Auto` selection logic, and no runtime/online adaptation of the coefficients, exists here --
  both forbidden by this card.

## Addendum (2026-08-26): recalibrated against real Player data, not Editor batchmode

`P4-008`'s platform benchmark work found the release Player runs ~13-14x faster than Editor
batchmode *overall*, and `Benchmarks~/Phase4/CostCurves/README.md`'s own addendum (2026-08-26)
checked whether the *per-step* figure this card's coefficient is built from follows that same
multiplier: extending `P4-008`'s Windows and Android platform probes to also record
`totalSteps`/`medianNanosecondsPerStep` per `Immediate`-policy sample found real per-step cost of
61.82 ns/step (Windows x64 Player, this same workstation, 24 samples) and 58.75 ns/step (Android
ARM64 Player, a physical Google Pixel 10 Pro, 18 samples) -- within ~5% of each other despite the
architectures being very different, and both ~11x lower than this card's original 678.75 ns/step
Editor-only figure.

Since this is a real, direct measurement of the exact same quantity this card's coefficient
represents (not a derived estimate), the owner decided to recalibrate rather than leave a
knowingly-wrong constant in place pending a future card: an Editor-calibrated coefficient shipped
into a release build would size every native batch roughly 11x too small, reproducing the very
fixed-batch-size scheduling overhead the batching formula (`NativeBatchSizeCalibrationV1`) exists
to prevent (the effect `P4-002`'s cost curves already demonstrated at fixed batch size 32).

**What changed:**

- `NativeWorkEstimatorV1.CalibratedNanosecondsPerNodeStep`: `678.75` → `60.275` (pooled median of
  all 42 real Player Immediate-policy samples: 24 Windows + 18 Android combined).
- `NativeWorkEstimatorV1.CalibrationTolerance`: `0.10` → `0.25`, re-derived (not carried over) by
  re-running this card's own correlation check against the 42 real Player points instead of the
  original 24 Editor points. Worst observed deviation: **20.98%**
  (`deep-sequence-selector-traversal` at 16 agents, Windows: estimate 17,057.83ns vs measured
  14,100.00ns). Second-worst: 19.63% (`many-programs-small-populations` at 16 agents, Windows).
  Third: 17.92% (`scheduling-baseline-empty-job` at 16 agents, Android). `0.25` leaves deliberate
  margin above the observed worst case, the same philosophy the original 8.71%→10% figure used,
  rather than being tuned exactly to the new worst case.
- `Tests/Runtime/NativeExecution/Scheduling/Estimation/NativeWorkEstimatorTests.cs`'s correlation
  test (`EstimateCorrelatesWithP4002sMeasuredCostWithinTheDocumentedTolerance`, renamed
  `EstimateCorrelatesWithRealPlayerMeasuredCostWithinTheDocumentedTolerance`): its 24 `[TestCase]`
  fixtures were hardcoded literal values from the original Editor JSON
  (`cost-curves-windows-editor-20260820.json`), not read live from that file -- so they did not
  "automatically track" the new constant the way a prior session assumed. Left unchanged, this test
  would have failed by ~92% relative error the first time it actually ran (Editor-scale measured
  values compared against a Player-scale estimate), a real regression that a live test run would
  have caught but that batchmode-lock prevented from running in the same session the constant
  changed. Replaced with all 42 real Player fixtures (Windows + Android) sourced directly from
  `Benchmarks~/Phase4/Platform/{Windows,Android}/Results/*-calibration-20260826.json`.

**Why one pooled constant across two platforms, not two platform-specific ones:** the whole point
of this check was to test whether per-step cost is one predictable Editor-vs-Player multiplier or
device-specific chaos; finding Windows and Android within ~5% of each other on real Player
hardware is direct evidence a single pooled figure generalizes reasonably across at least these two
platforms, consistent with this card's own "one global coefficient, not per-category" precedent
below. This remains one device per platform (per `Planning~/USER_ACTIONS.md`'s standing hardware
generalization caveat), not a claim about all Windows or all Android hardware.

**Not touched by this addendum:** the smoothing/bounding mechanism (`SmoothingAlpha`,
`MaxRelativeStepDeltaPerObservation`), `NativeBatchSizeCalibrationV1`'s formula, and the
"static, not runtime-adaptive" decision (reaffirmed, not revisited, per this card's own
forbidden-changes clause and `P4-007`'s prior rejection of runtime autotuning for the same class of
risk) are all unchanged. Only the two measured constants and their supporting test fixtures moved.

See `Benchmarks~/Phase4/CostCurves/README.md`'s own addendum for the Editor-vs-Player comparison
table and raw data links, and `verification-results.json` for the updated correlation-check
findings.

See `verification-results.json` for exact commands and results.
