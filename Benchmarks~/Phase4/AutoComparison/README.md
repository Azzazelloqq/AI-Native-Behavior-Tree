# P4-006 Auto vs. fixed-policy comparison

This benchmark runs `Auto` (`P4-005`) against `P4-001`'s implemented scenario catalog and compares
its chosen policy's measured cost against the best of the three same-frame-capable fixed policies
(`Immediate`, `Budgeted`, `BatchedJobsSameFrame`) at the same point. It is evidence for `P4-007`'s
`OQ-006` decision, not a decision itself: no threshold, default, or shipping recommendation is
drawn here, per this card's own forbidden-changes clause.

## Scope: LatencyMode=SameFrame only

`P4-001`'s benchmark harness (`SchedulingPolicyDriver`) was never wired to measure `PipelinedJobs`
-- it was built entirely separately in `P4-003`, in `Runtime/Scheduling/Native/`, with no benchmark
integration. Restricting this comparison to `NativeAutoLatencyModeV1.SameFrame` means `Auto` never
selects `PipelinedJobs` here, so no unmeasurable choice can occur -- by construction, not by
silently ignoring a case that came up. This is a real, disclosed scope limit, not a shortcut: it
means this comparison says nothing about how `Auto` would perform if pipelined latency were
permitted.

## Measured work

For each of `P4-001`'s 6 implemented scenarios, at agent counts 16/64/256/1024:

1. `Immediate`, `Budgeted`, and `BatchedJobsSameFrame` (fixed batch size 32) are each measured
   fresh in this same run (5 warmup + 15 measured samples, same discipline as `P4-001`/`P4-002`).
   The cheapest of the three is recorded as `bestFixedPolicy`.
2. A fresh `NativeWorkEstimatorV1` is seeded with this same run's real `(agentCount, totalSteps)`
   and produces a work estimate exactly as a real caller would.
3. `NativeAutoSelectionV1.TrySelect` is called with that estimate and a documented configuration
   (`minimumJobWorkloadNanoseconds = 50,000`, `targetBatchWorkNanoseconds = 50,000`, batch bounds
   `[1, 256]`, `LatencyMode = SameFrame`) -- this measurement run's own configuration choice, not a
   claimed shipped default; it is not calibrated against anything, simply a round, documented
   number chosen to actually exercise the same-frame branches of the decision tree.
4. `Auto`'s chosen policy's cost is that same policy's already-measured cost from step 1 -- `Auto`
   invents no new execution semantics, so there is nothing separate to measure.

## Result: Auto underperforms in 23 of 24 measured cases

| Outcome | Count |
| --- | ---: |
| Matches or beats the best fixed policy | 1 |
| Underperforms the best fixed policy | 23 |

This is a real, substantial, and somewhat uncomfortable finding, reported honestly rather than
softened or re-tuned away (this card's own forbidden-changes explicitly bars changing `Auto`'s
selection logic -- this measures, it does not tune). The mechanism is exactly what `P4-002`'s cost
curves already predicted: at these agent-count scales (16-1024) on this workstation, fixed-batch-size
`BatchedJobsSameFrame` carries per-batch Job-scheduling overhead that does not amortize away, so it
is *worse* than `Immediate`/`Budgeted` in every scenario at every tested scale --
`P4-002`'s own README already showed this. `Auto`'s decision tree, however, unconditionally prefers
`BatchedJobsSameFrame` once the estimated workload crosses `minimumJobWorkloadNanoseconds` and
`LatencyMode` requires same-frame results -- it has no notion that `BatchedJobsSameFrame` might
actually cost *more* than the simpler policies at this workstation's measured scale, because that
comparison is exactly what this card exists to surface, not something `P4-005`'s decision tree
was built to already account for.

| Scenario | agentCount | Best fixed (ns/agent) | Auto chose | Auto (ns/agent) | Gap |
| --- | ---: | ---: | --- | ---: | ---: |
| scheduling-baseline-empty-job | 16 | Budgeted 3,506.25 | Immediate | 3,643.75 | +3.9% |
| scheduling-baseline-empty-job | 64 | Budgeted 3,684.38 | BatchedJobsSameFrame | 21,042.19 | +471.1% |
| scheduling-baseline-empty-job | 256 | Budgeted 3,961.33 | BatchedJobsSameFrame | 29,635.94 | +648.1% |
| scheduling-baseline-empty-job | 1024 | Immediate 3,958.11 | BatchedJobsSameFrame | 74,174.61 | +1,774.0% |
| shallow-tree-cheap-conditions | 16 | Budgeted 18,056.25 | BatchedJobsSameFrame | 122,312.50 | +577.4% |
| shallow-tree-cheap-conditions | 64 | Immediate 18,846.88 | BatchedJobsSameFrame | 77,440.63 | +310.9% |
| shallow-tree-cheap-conditions | 256 | Immediate 18,904.30 | BatchedJobsSameFrame | 85,348.05 | +351.5% |
| shallow-tree-cheap-conditions | 1024 | Immediate 21,094.63 | BatchedJobsSameFrame | 104,864.94 | +397.1% |
| deep-sequence-selector-traversal | 16 | Budgeted 204,175.00 | BatchedJobsSameFrame | 1,125,068.75 | +451.0% |
| deep-sequence-selector-traversal | 64 | Immediate 216,929.69 | BatchedJobsSameFrame | 803,685.94 | +270.5% |
| deep-sequence-selector-traversal | 256 | Immediate 216,925.78 | BatchedJobsSameFrame | 645,672.27 | +197.6% |
| deep-sequence-selector-traversal | 1024 | Immediate 218,687.89 | BatchedJobsSameFrame | 630,483.30 | +188.3% |
| wide-branching-frequent-failures | 16 | Immediate 5,981.25 | BatchedJobsSameFrame | 42,462.50 | +609.9% |
| wide-branching-frequent-failures | 64 | Immediate 6,234.38 | BatchedJobsSameFrame | 29,637.50 | +375.4% |
| wide-branching-frequent-failures | 256 | Immediate 6,113.67 | BatchedJobsSameFrame | 35,360.16 | +478.4% |
| wide-branching-frequent-failures | 1024 | Immediate 6,710.45 | BatchedJobsSameFrame | 74,158.79 | +1,005.1% |
| predominantly-running-actions | 16 | Budgeted 4,156.25 | BatchedJobsSameFrame | 29,293.75 | +604.8% |
| predominantly-running-actions | 64 | Budgeted 4,217.19 | BatchedJobsSameFrame | 20,759.38 | +392.3% |
| predominantly-running-actions | 256 | Immediate 4,115.23 | BatchedJobsSameFrame | 28,614.06 | +595.3% |
| predominantly-running-actions | 1024 | Immediate 4,442.58 | BatchedJobsSameFrame | 67,889.36 | +1,428.2% |
| many-programs-small-populations | 16 | Immediate 3,393.75 | Immediate | 3,393.75 | 0.0% (match) |
| many-programs-small-populations | 64 | Immediate 3,260.94 | BatchedJobsSameFrame | 19,281.25 | +491.3% |
| many-programs-small-populations | 256 | Immediate 3,547.27 | BatchedJobsSameFrame | 27,307.42 | +669.8% |
| many-programs-small-populations | 1024 | Immediate 3,777.83 | BatchedJobsSameFrame | 67,384.28 | +1,683.7% |

`Auto`'s confidence is `Low` in every case here -- this benchmark seeds a fresh estimator with
exactly one observation per case (matching how a real caller would call it on a cold start), so it
never reaches the `Medium`/`High` thresholds a long-running real caller's accumulated observations
would. This is a real limitation of this benchmark's single-shot design, not a claim about `Auto`'s
behavior after warm-up in a live system.

## What this means for P4-007

`benchmarks.md`'s step 5 ("test lightweight adaptation ONLY IF fixed heuristics leave meaningful
performance on the table") is squarely triggered by this data: the fixed heuristic, as configured
here, leaves *substantial* performance on the table in 23 of 24 measured cases. This is not, by
itself, an argument for runtime autotuning (`OQ-006`) -- the same gap could equally be closed by
recalibrating `P4-005`'s decision tree's own thresholds/rule (e.g., the `BatchedJobsSameFrame`
preference for same-frame-required large workloads does not currently account for the possibility
that `BatchedJobsSameFrame` costs more than `Immediate` at the measured scale, which is exactly
what would need fixing first, entirely within the deterministic-heuristic model `P4-005` already
established, before concluding that fixed heuristics themselves are insufficient). `P4-007` is
where that judgment call is made; this card only supplies the numbers.

## Environment

Unity 6000.5.8f1, Windows, Intel Core Ultra 9 275HX, `JobsUtility.JobWorkerCount = 23`, five
warmups, fifteen measured samples per case. One run on one workstation -- not generalized to other
hardware.

## Recorded evidence

The canonical 2026-08-21 isolated run is preserved as
[raw JSON](Results/auto-comparison-windows-editor-20260821.json), including every case's full
`Auto` explanation (reason, confidence, batch size/count, worker-utilization proxy, budget
comparison). The adjacent Unity log is not committed, per repository policy against committing raw
Unity logs.
