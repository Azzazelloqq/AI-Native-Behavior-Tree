# P4-005 Auto policy: deterministic explainable heuristic selection evidence

## Result

- `Runtime/Scheduling/Native/Auto/NativeAutoContracts.cs` (new): the four-policy enum
  (`NativeAutoPolicyV1`), a caller-supplied capability declaration
  (`NativeAutoSupportedPoliciesV1`, since no Web-backend code exists anywhere in this package to
  detect real platform capability from), a binary latency-permission gate
  (`NativeAutoLatencyModeV1`), a coarse documented confidence bucket
  (`NativeAutoConfidenceV1`), an explicit selection-reason enum
  (`NativeAutoSelectionReasonV1`), the full override-surface input struct
  (`NativeAutoConfigurationV1`), the work-estimate input struct (`NativeAutoWorkloadV1`), and the
  narrowed explainability output struct (`NativeAutoExplanationV1`).
- `Runtime/Scheduling/Native/Auto/NativeAutoSelectionV1.cs` (new): `TrySelect`, a pure,
  deterministic decision tree -- forced policy (validated against both the supported-policy set
  and the latency mode) beats everything; otherwise below-minimum-workload picks `Immediate`,
  a configured update budget picks `Budgeted`, pipelining-permitted-and-preferred picks
  `PipelinedJobs`, same-frame-required-and-large picks `BatchedJobsSameFrame`, and a
  restricted-capability fallback picks whatever single policy remains available. Every branch sets
  an explicit `NativeAutoSelectionReasonV1` -- never a black-box score.
- `Tests/Runtime/NativeExecution/Scheduling/Auto/NativeAutoSelectionTests.cs` (new): 24 tests
  covering invalid inputs, forced-policy validation (both unsupported-backend rejection and the
  latency-mode contradiction rejection below), every automatic-selection branch, batch
  size/count/worker-utilization population (and their absence for non-batched policies),
  confidence-bucket thresholds, determinism on 20 repeated identical-input calls, and a dedicated
  determinism check against all 6 real `P4-001` catalog scenarios (real `(agentCount, totalSteps)`
  pairs fed through a real `NativeWorkEstimatorV1`, then selected 5 times each).
- Full EditMode suite: 1386 tests (1362 + 24 new), 1383 passed; 3 pre-existing failures unrelated
  to this card (same as every prior P3/P4 evidence file). Confirmed via `git status` inside the
  `AIBT` submodule that this session touched only `Runtime/Scheduling/Native/Auto/` and
  `Tests/Runtime/NativeExecution/Scheduling/Auto/` (both new directories) plus `Planning~/`.

## Decision: explainability surface narrowed before implementation

Escalated via `AskUserQuestion` before writing code: this card's acceptance criteria demands
"every field the explainability surface promises is populated and independently verifiable
against the same run's raw scheduling data," but the full field list in
`Documentation~/execution-and-scheduling.md` ("node steps, commands, wakeups, and deferred
agents"; "scheduling and completion cost") includes fields with **no existing data source
anywhere in `Runtime/Scheduling/Native/`**:
- Command and wakeup counting is a leaf-node/Commands-subsystem concern (a different, already-done
  P2 card's area), not something the scheduler layer tracks at all.
- `P4-004`'s `NativeWorkEstimatorV1` models only per-atomic-step cost; it has no calibrated model
  for per-batch Job-scheduling overhead (the very overhead `P4-002` showed makes
  `BatchedJobsSameFrame` non-flat) -- a real, separate calibration gap, not something this card
  can fabricate a number for.

The user chose to narrow the explainability surface to fields with a genuine, verifiable source
today (see `NativeAutoExplanationV1`'s own XML doc comment for the exact list and the reasoning),
rather than building new command/wakeup-counting infrastructure or a new per-batch cost model --
both of which are separate, larger pieces of work outside this card's own allowed-changes scope.
This mirrors the discipline `P4-001` already established for its own unimplemented scenario
placeholders: a documented gap, not a silently faked field.

Two further design decisions, made within this card's own engineering latitude (not escalated,
since they are algorithm-choice questions the card's text does not dictate a specific answer to):

- **A forced `PipelinedJobs` selection still must respect `LatencyMode`.** The spec's rule is
  "forcing an unsupported policy on the active backend... is not silently replaced," which is
  about backend capability. It says nothing about a force that contradicts the caller's own
  configured latency permission. Rather than treat the force as automatic permission (which would
  let a caller silently violate their own stated same-frame requirement through a second,
  uncoordinated configuration field), a force of `PipelinedJobs` while `LatencyMode == SameFrame`
  is rejected with the same structured diagnostic as an unsupported-backend force --
  `Documentation~/execution-and-scheduling.md`'s "never does so silently" guarantee is treated as
  applying regardless of how a policy was chosen, not only to the fully-automatic path.
- **Confidence is a coarse, documented observation-count bucket** (`Low` under 3 observations,
  `Medium` under 10, `High` otherwise), not a statistical model -- there is no existing variance/
  stability tracking anywhere to build a real statistical confidence interval from, and inventing
  one would be exactly the kind of unverifiable field this card's own narrowing decision rejects
  for other fields.

## Scope and limitations

- `NativeAutoSupportedPoliciesV1` is a caller-supplied declaration, not live platform/backend
  detection -- no code anywhere in this package implements the Web backend
  `Documentation~/specifications/platform-backends-v1.md` describes, so there is nothing to detect
  from. Wiring a real per-backend capability query is future integration work, not this card's.
  The negative test (`ForcingAPolicyNotInTheSupportedSetReturnsAStructuredDiagnosticNotASilentSubstitution`)
  uses a synthetic restricted set (`Immediate`-only) to prove the rejection path without needing a
  real Web backend to exist.
- `UpdateCadence` is accepted, recorded, and echoed back in the explanation for inspectability, but
  nothing in this card acts on it -- driving an actual per-tree update cadence is an
  integration-layer concern this card does not claim to implement.
- No runtime/online adaptation exists anywhere here; the selection rule is a fixed, deterministic
  decision tree, exactly the baseline `P4-007`'s conditional autotuning card (`OQ-006`) is meant to
  compare against, per this card's own forbidden-changes clause.
- This card selects a policy and computes its batch parameters; it does not itself drive any
  policy's controller (`P2-019`/`P4-003`) or own a `NativeWorkEstimatorV1` instance -- both remain
  the caller's responsibility, matching this codebase's existing pattern of owning identity-keyed
  state at the call site.

See `verification-results.json` for exact commands and results.
