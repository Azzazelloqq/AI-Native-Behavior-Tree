# P4-004 — Work-estimation and batching calibration model

Status: `Done`

## Objective

Implement `Documentation~/execution-and-scheduling.md`'s work-estimation and batching formulas as an explicit, inspectable model, calibrated from `P4-002`'s measured cost curves. This is `benchmarks.md`'s "Scheduler research" step 2 ("Calibrate work units and initial batch targets per platform class").

## Depends on

- `P4-002`.

## Required reading

- `Documentation~/execution-and-scheduling.md` ("Work estimation", "Batching").
- `P4-002`'s recorded cost curves.

## Allowed changes

- `Runtime/Scheduling/Native/Estimation/` (new).
- `Tests/Runtime/NativeExecution/Scheduling/Estimation/` (new).

## Forbidden changes

- The `Auto` selection policy itself (`P4-005`) — this card produces the estimate/batch-size model `Auto` will consume, not the selection logic.
- Runtime/online adaptation of any kind (`OQ-006`, `P4-007`) — this model's coefficients are fixed at calibration time, recalibrated only by re-running this card, never adjusted live.
- Any claim this model is accurate on hardware other than where `P4-002`'s curves were measured.

## Deliverables

- `estimated work = runnable agents × expected node steps per agent × calibrated node-cost units`, with calibrated node-cost units derived from `P4-002`'s data per node cost category, compiled-program identity, and available inputs (recent node-step counts, running-path depth, event wakeups, command volume, worker count, platform profile, configured budget).
- Estimate smoothing/bounding so a single spike does not cause an unstable estimate.
- `batch size = target batch work / estimated work per agent`, clamped by policy and memory limits, with the "enough batches for load balancing without flooding the queue" rule from the spec.

## Acceptance criteria

- Given a recorded scenario's actual inputs, the model's work estimate correlates with `P4-002`'s independently measured actual cost within a stated, evidence-based tolerance the card itself records (not an assumed threshold).
- A single artificial cost spike in a synthetic input does not cause the smoothed estimate to swing more than the card's own documented bound.
- Batch size clamps correctly at both the policy limit and the memory limit in constructed edge-case tests.
- The model's coefficients and their calibration source (which `P4-002` curve, which platform) are recorded and inspectable, not opaque constants.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <estimation model fixture>
estimate-vs-measured correlation check against P4-002 data
smoothing/bounding negative tests (synthetic spike)
```

## Handoff notes

- `P4-005` (`Auto`) is the direct consumer of this model.
- Recalibrating this model against a new platform's `P4-002`-equivalent curves is expected future work, not something this card needs to generalize for up front.

## Outcome

Implemented `NativeWorkEstimatorV1` (smoothed/bounded work estimation, seeded and updated from
real `(agentCount, totalSteps)` observations, clamped so a single spike moves the estimate by at
most 12.5%) and `NativeBatchSizeCalibrationV1` (target/estimate batch-size formula, clamped by
policy and memory limits with memory always winning conflicts, plus a load-balancing floor).
`CalibratedNanosecondsPerNodeStep = 678.75` is the pooled median ns/atomic-step from `P4-002`'s
360 Immediate-policy samples; validated directly against all 24 real (scenario, agent count)
points from that same data, with every point's estimate landing within `CalibrationTolerance =
0.10` of the actually-measured cost (worst case 8.71%, `deep-sequence-selector-traversal` at 256
agents) — the tolerance was set from this real result, not assumed. One global coefficient was
used rather than per-node-cost-category ones, since `P4-002`'s data only supports scenario-level
measurement, not a genuine node-type cost breakdown; documented as a self-directed engineering
choice, not an architectural escalation. Full derivation and decision reasoning in
`Planning~/Evidence/P4-004/README.md`.

**Addendum (2026-08-26):** `CalibratedNanosecondsPerNodeStep` recalibrated `678.75` → `60.275` and
`CalibrationTolerance` re-derived `0.10` → `0.25` against real Windows/Android Player data (found
~11x lower than Editor batchmode) instead of the original Editor-only figures — see
`Planning~/Evidence/P4-004/README.md`'s 2026-08-26 addendum for full derivation.
