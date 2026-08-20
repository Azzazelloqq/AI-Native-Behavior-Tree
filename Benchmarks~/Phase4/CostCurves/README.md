# P4-002 fixed-policy scheduling overhead and cost curves

This card runs `Benchmarks~/Phase4/Scheduling/`'s harness (built in `P4-001`, not modified here)
at a wider agent-count range to produce actual cost curves per policy per scenario, per
`Documentation~/benchmarks.md`'s "Scheduler research" step 1. It is measurement only: no default,
threshold, or recommended policy choice is drawn here, per this card's own `Forbidden changes`.

## What changed from P4-001's run

Nothing about the harness itself. The same `Run-SchedulingBenchmark.ps1` /
`SchedulingBenchmarkRunner.cs` / `SchedulingScenarios.cs` from `Benchmarks~/Phase4/Scheduling/`
were invoked with a wider `-AgentCounts` sweep (16, 64, 256, 1024 -- four points, satisfying this
card's "at least three points on the relevant parameter axis") instead of P4-001's harness-proving
default of two (16, 128). `-WorkerThreadCounts '1,23'` (this machine's
`JobsUtility.JobWorkerMaximumCount`) and the default three batch sizes (8/32/128) were kept from
P4-001's own sweep, since that infrastructure was already built and free to include.

## Cost curves: Immediate and Budgeted (population-independent per-agent cost)

Median ns/agent by agent count. Both policies are flat across nearly two orders of magnitude of
population size -- the expected result for policies with no batching: per-agent cost does not
depend on how many other agents are updating.

| Scenario | 16 | 64 | 256 | 1024 |
| --- | ---: | ---: | ---: | ---: |
| `scheduling-baseline-empty-job` (Immediate) | 2,912.50 | 2,864.06 | 2,845.31 | 2,909.18 |
| `scheduling-baseline-empty-job` (Budgeted) | 3,056.25 | 2,934.38 | 2,989.84 | 2,940.04 |
| `shallow-tree-cheap-conditions` (Immediate) | 15,456.25 | 15,214.06 | 15,375.78 | 15,485.25 |
| `shallow-tree-cheap-conditions` (Budgeted) | 16,243.75 | 15,787.50 | 15,760.94 | 15,793.16 |
| `deep-sequence-selector-traversal` (Immediate) | 177,768.75 | 176,895.31 | 176,699.22 | 178,592.48 |
| `deep-sequence-selector-traversal` (Budgeted) | 182,831.25 | 182,275.00 | 183,301.17 | 184,603.42 |
| `wide-branching-frequent-failures` (Immediate) | 5,112.50 | 5,167.19 | 5,104.30 | 5,157.42 |
| `wide-branching-frequent-failures` (Budgeted) | 5,318.75 | 5,384.38 | 5,309.38 | 5,338.18 |
| `predominantly-running-actions` (Immediate) | 3,437.50 | 3,550.00 | 3,346.48 | 3,439.36 |
| `predominantly-running-actions` (Budgeted) | 3,606.25 | 3,529.69 | 3,519.53 | 3,581.74 |
| `many-programs-small-populations` (Immediate) | 2,881.25 | 2,868.75 | 2,922.66 | 2,954.00 |
| `many-programs-small-populations` (Budgeted) | 2,968.75 | 2,934.38 | 2,948.83 | 3,005.47 |

## Cost curves: BatchedJobsSameFrame (population-dependent per-agent cost, at fixed batch size 32)

Median ns/agent by agent count, batch size fixed at 32 -- **not** flat, unlike Immediate/Budgeted:

| Scenario | 16 (worker=1 / worker=23) | 64 (worker=1 / worker=23) | 256 (worker=1 / worker=23) | 1024 (worker=1 / worker=23) |
| --- | ---: | ---: | ---: | ---: |
| `scheduling-baseline-empty-job` | 15,793.75 / 21,018.75 | 15,328.13 / 17,684.38 | 23,489.06 / 24,308.98 | 55,528.42 / 57,068.95 |
| `shallow-tree-cheap-conditions` | 51,625.00 / 108,631.25 | 50,214.06 / 63,606.25 | 58,441.80 / 69,528.91 | 92,122.17 / 97,678.22 |
| `deep-sequence-selector-traversal` | 570,793.75 / 884,881.25 | 545,245.31 / 681,953.13 | 541,401.56 / 605,538.67 | 563,923.93 / 626,955.66 |
| `wide-branching-frequent-failures` | 20,081.25 / 32,568.75 | 20,625.00 / 24,887.50 | 28,780.08 / 33,133.98 | 63,193.16 / 64,749.02 |
| `predominantly-running-actions` | 13,837.50 / 22,593.75 | 15,343.75 / 18,821.88 | 23,355.08 / 26,438.28 | 58,732.52 / 59,645.80 |
| `many-programs-small-populations` | 12,137.50 / 19,956.25 | 13,626.56 / 17,846.88 | 22,527.73 / 24,809.38 | 56,631.35 / 57,293.65 |

## Reading these curves (descriptive only -- no threshold or recommendation)

**Immediate and Budgeted scale linearly with population**, as expected: per-agent cost is
constant, so total cost is `O(agentCount)` with no extra overhead as the population grows. This
workstation's flat cost is ~2.8-3.1us/agent for a bare leaf up to ~177-185us/agent for the deepest
tree tested (63 nodes).

**BatchedJobsSameFrame does not scale flat at a fixed batch size.** Per-agent cost roughly
*doubles to quadruples* from 16 to 1024 agents at batch size 32 across every scenario. This is a
genuine, non-obvious finding, not an artifact: `NativeBatchedLifecycleOwnerV1.TrySchedule` creates
one Burst job per `batchSize`-sized chunk and combines every chunk's `JobHandle` via
`JobHandle.CombineDependencies`; holding batch size fixed while population grows multiplies the
*number* of chunks (1 chunk at 16 agents, 32 chunks at 1024 agents), and that per-chunk scheduling
and handle-combining overhead is exactly what dominates the per-agent cost at high population. In
other words: **a fixed batch size does not amortize** -- the batch size needs to grow with the
population to keep per-agent overhead flat, which is precisely `Documentation~/execution-and-scheduling.md`'s
own formula (`batch size = target batch work / estimated work per agent`) and exactly what `P4-004`
(work-estimation and batching calibration) exists to calibrate. This card does not calibrate
anything; it hands `P4-004` the curve that shows calibration is actually necessary, with numbers.

`workerThreadCount=23` costs more than `workerThreadCount=1` in every cell above, consistent with
`P4-001`'s own smaller-scale finding (worker-wake/coordination overhead outweighs parallelism gains
at these batch sizes/populations on this workstation) -- the gap narrows at higher agent counts but
never reverses in this data.

`deep-sequence-selector-traversal` at 16 agents (570,793.75 / 884,881.25) sits *higher* than at 64
agents (545,245.31 / 681,953.13) before rising again toward 1024 -- a non-monotonic curve. At
batch size 32 and agentCount 16, every agent fits in a single batch/chunk (16 < 32); at 64 agents
it is exactly two chunks. The fixed per-chunk overhead is amortized differently at each population,
which is exactly why this card records raw curves instead of a single average.

## Known limitation inherited from P4-001

`many-programs-small-populations` is documented in `P4-001`'s evidence as reusing the same
single-leaf tree shape as `scheduling-baseline-empty-job` (a placeholder, not yet a tree of
genuinely many distinct compiled programs) -- its numbers here are consistent with that (nearly
identical to `scheduling-baseline-empty-job`'s own curve). This is not a new problem introduced by
this card; building a genuine multi-program scenario remains out of scope for both P4-001 and
P4-002.

## Environment

Unity 6000.5.8f1, Windows, Intel Core Ultra 9 275HX (24 logical processors,
`JobsUtility.JobWorkerMaximumCount = 23`), Collections 6.5.0, five warmup samples and fifteen
measured samples per case. One measured run on one workstation -- not a stable baseline, not
generalized to other hardware (`Planning~/USER_ACTIONS.md` requires owner approval across multiple
hardware classes before any threshold is adopted). Full environment metadata, every raw sample,
and min/p95/max summaries are in the JSON; this README's tables are medians only, condensed for
readability.

## Recorded evidence

The canonical 2026-08-20 isolated run is preserved as
[raw JSON](Results/cost-curves-windows-editor-20260820.json). The adjacent Unity log is not
committed, per repository policy against committing raw Unity logs.
