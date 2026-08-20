# P4-006 Auto vs. fixed-policy comparison evidence

## Result

- `Benchmarks~/Phase4/AutoComparison/Unity/AutoComparisonRunner.cs` (new, isolated-project-only):
  measures the three same-frame-capable fixed policies fresh (5 warmup + 15 measured samples,
  same discipline as `P4-001`/`P4-002`) for every implemented `P4-001` scenario at agent counts
  16/64/256/1024, seeds a fresh `NativeWorkEstimatorV1` with this same run's real `(agentCount,
  totalSteps)`, calls `NativeAutoSelectionV1.TrySelect` (`LatencyMode=SameFrame`), and records
  `Auto`'s chosen policy's already-measured cost against the cheapest of the three fixed policies
  at that same point, with `Auto`'s full explanation (reason, confidence, batch size/count,
  worker-utilization proxy, budget comparison) attached to every case.
- `Benchmarks~/Phase4/AutoComparison/Run-AutoComparisonBenchmark.ps1` (new): builds the isolated
  project by copying `Runtime/`, `Authoring/`, `Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs`,
  and `Benchmarks~/Phase4/Scheduling/Unity/SchedulingScenarios.cs` unchanged (reusing the exact
  same 6-scenario catalog `P4-001`/`P4-002` measured against, not a parallel reimplementation),
  alongside this card's own `Unity/` folder. Neither `SchedulingPolicyDriver.cs` nor
  `SchedulingScenarios.cs` was modified -- this card measures, it does not touch `P4-001`'s harness.
- **Result: `Auto` underperforms the best fixed policy in 23 of the 24 measured cases** (one exact
  match at `many-programs-small-populations`/16 agents; the 24th case, `scheduling-baseline-empty-job`/16
  agents, is technically an "Underperforms" by 3.9%, both within noise). Gaps in the remaining 22
  cases range from +188% to +1,774% in nanoseconds-per-agent. This is reported honestly, not
  softened or reconfigured to look better -- this card's own forbidden-changes clause explicitly
  bars changing `Auto`'s selection logic to fix this.
- **Root cause, traced concretely, not just observed**: `Auto`'s decision tree unconditionally
  prefers `BatchedJobsSameFrame` for same-frame-required workloads once the estimated total work
  crosses `minimumJobWorkloadNanoseconds`. But `P4-002`'s own cost curves already showed that
  fixed-batch-size `BatchedJobsSameFrame` carries per-batch Job-scheduling overhead that does not
  amortize at these agent-count scales (16-1024) on this workstation -- `Immediate`/`Budgeted` are
  flat and cheaper in every one of `P4-002`'s 24 measured points. `P4-005`'s decision tree has no
  notion that `BatchedJobsSameFrame` might cost *more* than the simpler policies at a given scale;
  that comparison is exactly what this card exists to surface, not something `P4-005` was built to
  already know.
- Full per-scenario, per-agent-count table with exact numbers in
  `Benchmarks~/Phase4/AutoComparison/README.md`.

## Decision

No architectural or interpretation escalation was needed for this card's own scope (unlike
`P4-001`/`P4-003`/`P4-005`) -- the one real infrastructure gap found (`PipelinedJobs` has no
benchmark-harness measurement path) was escalated and resolved *before* any code was written: the
user chose to scope this comparison to `LatencyMode=SameFrame` only, so `Auto` never selects an
unmeasurable policy here, by construction. See the chat record for that decision; it is also
disclosed in `Benchmarks~/Phase4/AutoComparison/README.md`'s own "Scope" section.

The `Auto` configuration used for this measurement run (`minimumJobWorkloadNanoseconds = 50,000`,
`targetBatchWorkNanoseconds = 50,000`, batch bounds `[1, 256]`) is this run's own choice, not a
calibrated or claimed-shipped default -- chosen as a round number low enough to actually exercise
the `BatchedJobsSameFrame` branch across most of the matrix (rather than trivially staying in
`Immediate` everywhere and comparing nothing meaningful).

## Scope and limitations

- `PipelinedJobs` is excluded entirely (see Decision above) -- this comparison says nothing about
  `Auto`'s performance when pipelined latency is permitted.
- Every case reports `Confidence=Low` because this benchmark seeds a fresh estimator with exactly
  one observation per case (a cold-start caller), never reaching the `Medium`/`High` thresholds a
  real long-running caller's accumulated observations would reach. This is a limitation of this
  benchmark's single-shot design, not a finding about `Auto`'s live behavior after warm-up.
- One run on one workstation; not generalized to other hardware
  (`Planning~/USER_ACTIONS.md` requires owner approval across multiple hardware classes before any
  threshold is adopted).
- No regression threshold, default, or shipping recommendation is drawn from any gap recorded
  here, per this card's own forbidden-changes clause -- `P4-007` is where that judgment is made.
- This finding is not, by itself, evidence for or against runtime autotuning (`OQ-006`): the same
  gap could plausibly be closed by recalibrating `P4-005`'s own deterministic decision-tree
  thresholds, entirely within the fixed-heuristic model, before concluding fixed heuristics are
  insufficient. `P4-007`'s own card text already frames this correctly ("ONLY IF fixed heuristics
  leave meaningful performance on the table" -- not "therefore adaptation").

See `verification-results.json` for exact commands and results.
