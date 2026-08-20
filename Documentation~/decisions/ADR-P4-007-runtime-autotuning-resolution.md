# ADR P4-007: `OQ-006` resolution — runtime autotuning rejected

- Status: Accepted 2026-08-21
- Date: 2026-08-21
- Decision ID: AIBT-013

## Context

`OQ-006` ("decide whether runtime autotuning beats calibrated fixed heuristics") was blocked on
benchmark evidence. `P4-006` (`Planning~/Evidence/P4-006/`) measured `Auto` (`P4-005`) against the
best fixed policy across all 6 implemented `P4-001` scenarios at 4 agent-count points and found
`Auto` underperforming in 23 of 24 cases, by +188% to +1,774% in nanoseconds per agent. Per
`Documentation~/benchmarks.md`'s step 5, this cleared the gate to test lightweight adaptation.

### The adaptation prototype

`Runtime/Scheduling/Native/Auto/NativeAutoPolicyCostTrackerV1.cs` and
`NativeAutoSelectionV1.TrySelectAdaptive` were built: each candidate policy gets its own
bounded-EWMA smoothed recent-cost tracker (the same seed-then-clamp-then-blend mechanism `P4-004`'s
`NativeWorkEstimatorV1` already proved safe — a single spike moves the estimate by at most 12.5%),
and once at least two candidates have a real tracked cost, `Auto` picks whichever is currently
cheapest instead of the fixed decision tree's blind preference for `BatchedJobsSameFrame`.

### The prototype was tested against a realistic feedback model, not an idealized one

`Tests/Runtime/NativeExecution/Scheduling/Auto/NativeAutoAdaptiveRealisticFeedbackTests.cs` used
real `P4-002` numbers (`wide-branching-frequent-failures`/1024 agents: `Immediate` 5,157.42 ns/agent
vs. `BatchedJobsSameFrame` 74,158.79 ns/agent — one of `P4-006`'s worst-gap cases) in a **realistic
single-observer simulation**: a real caller only learns the cost of the policy it actually ran each
round, never an untried alternative's cost. The result, over 50 simulated rounds:

- Cold start reproduces `P4-006`'s actual mistake — the deterministic fallback picks
  `BatchedJobsSameFrame` first, exactly as observed.
- Because only the chosen policy's tracker ever receives an observation, `Immediate`'s tracker
  never accumulates a single data point across all 50 rounds.
- The adaptive comparison requires at least two tracked candidates to activate at all — with only
  one ever populated, it **never activates**, and every one of the 50 rounds stays on
  `BatchedJobsSameFrame`.

A second test confirmed the comparison logic itself is correct once both policies' costs happen to
be known externally (e.g. by an exploration mechanism this card does not build) — the flaw is
specifically the missing exploration, not a bug in the comparison.

## Decision

**Reject runtime autotuning for the gap `P4-006` found.** A purely reactive lightweight-adaptation
tracker — the natural, minimal "test adaptation" per `benchmarks.md`'s step 5 — is mechanically
incapable of closing this gap in a realistic deployment: without an exploration mechanism, it can
never discover that a policy it has not chosen would have been cheaper, so it gets permanently
stuck reinforcing whatever the fixed heuristic's cold-start choice happened to be. Adding an actual
exploration mechanism (periodically trying non-preferred policies to sample their real cost) would
introduce exactly the overhead, instability, and unpredictability `benchmarks.md`'s step 6 says
disqualifies adaptation: a caller would pay for occasional deliberately-suboptimal runs, and the
resulting behavior becomes harder to predict/audit than a fixed rule.

`OQ-006` is resolved: **fixed heuristics are the right tool here, not runtime adaptation** — but
`P4-006`'s gap is real and is traced to a specific, nameable defect in `P4-005`'s own deterministic
decision rule (it prefers `BatchedJobsSameFrame` for same-frame-required large workloads without
any comparison against `Immediate`/`Budgeted`'s own cost, and `P4-002` already showed
`BatchedJobsSameFrame`'s fixed-batch-size overhead does not amortize at these measured scales).
Fixing that rule is a deterministic recalibration — no online learning required — and is left as
follow-up work, not built by this card (this card resolves `OQ-006`'s question; it does not also
re-tune `P4-005`'s shipped baseline).

`AIBT-013`'s own condition ("add runtime autotuning only when benchmarks show value over calibrated
heuristics") was tested directly and not met: the adaptation this card built and measured does not
show value over the fixed baseline, because it cannot even engage without an exploration mechanism
this card also shows would cost more than it is worth.

## Consequences

- No runtime/online adaptation code ships. `Runtime/Scheduling/Native/Auto/NativeAutoSelectionV1.TrySelect`
  (`P4-005`'s original deterministic entry point) remains the only production-facing selection
  function; `TrySelectAdaptive` and `NativeAutoPolicyCostTrackerV1` are retained as this card's
  tested, disclosed experiment (not deleted, since they are correct and their result is exactly
  the evidence this ADR cites), but nothing calls `TrySelectAdaptive` in production.
- `P4-005`'s decision rule has a known, evidence-traced gap (unconditional `BatchedJobsSameFrame`
  preference without a real cost comparison). Recalibrating it is legitimate future work, separate
  from this ADR, and does not require reopening `OQ-006`.
- `P4-008`/`P4-009` may proceed: `OQ-006`, the last item blocking "Auto scheduler finalization" per
  `Planning~/OPEN_QUESTIONS.md`, is resolved.
