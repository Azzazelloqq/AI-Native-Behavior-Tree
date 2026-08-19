# P4-001 scheduling scenario catalog and benchmark harness

This benchmark measures fixed-policy scheduling overhead (step 1 of
`Documentation~/benchmarks.md`'s "Scheduler research" process) across the
structural scenario catalog. It is evidence, not a performance gate: the
runner records every timing and allocation observation and defines no
pass/fail threshold, no scheduling policy recommendation, and no "supported
agent count" claim.

## Scope

`Documentation~/benchmarks.md` catalogs fourteen scheduling scenarios. Six are
implemented here -- the ones that need only built-in composites and
constant-status leaves:

| Scenario | Isolates |
| --- | --- |
| `scheduling-baseline-empty-job` | Fixed per-tick overhead on the smallest possible tree (one leaf) |
| `shallow-tree-cheap-conditions` | A small, flat sequence of cheap leaves |
| `deep-sequence-selector-traversal` | Traversal cost through many nested composite levels |
| `wide-branching-frequent-failures` | Early-exit/failure-path cost under a wide composite |
| `predominantly-running-actions` | Cost of resuming an already-entered tree without re-traversal |
| `many-programs-small-populations` | Per-program (not per-agent) scheduling overhead |

The remaining eight need leaf semantics that do not exist in a reusable form
anywhere in AIBT yet (typed blackboard access, command emission, async
operations, a managed-node boundary, deliberately expensive Burst work, event
wakeups, cross-policy pipelined/`Auto` comparison, and hot-reload/debug
overhead). Building those is out of this card's scope. They are listed in
every result's `documentedNotYetImplementedScenarios` with their `isolates`
description -- documented placeholders, not silently faked measurements. This
mirrors the same discipline the card already applies to the `PipelinedJobs`
and `Auto` policies themselves, both later P4 cards.

Every implemented scenario runs against all three accepted fixed policies
(`Immediate`, `Budgeted`, `BatchedJobsSameFrame`) at two agent-count points
(16 and 128 by default). `BatchedJobsSameFrame` is additionally swept across
batch size and `JobsUtility.JobWorkerCount` (three batch sizes x two worker
counts by default) -- `Documentation~/benchmarks.md`'s own deliverable text
calls for sweeping "batch parameters, and worker-thread counts where
controllable," and `JobsUtility.JobWorkerCount` has a public setter, so it is
controllable. `Immediate` and `Budgeted` are plain managed loops with no Jobs
involved, so neither parameter applies to them (`batchSize`/`workerThreadCount`
are recorded as `-1` on their cases).

## Measured work

`SchedulingPolicyDriver` (copied in from `Tests/Runtime/Benchmarking/`, shared
unchanged with the in-project EditMode correctness tests) constructs N
independent native tree-instance agents from one already-compiled
`CompiledProgram` and drives them one tick under the selected policy. Each
sample creates a fresh agent set, times exactly one
`TryRunImmediate`/`TryRunBudgeted`/`TryRunBatchedJobsSameFrame` call, and
disposes the agents -- matching `DispatchBenchmarkRunner`'s per-sample
isolation. Agent construction/disposal, JSON serialization, and the
inter-case `GC.Collect` calls are outside the timed and managed-allocation
window. Defaults are five warmup samples and fifteen measured samples per
case.

## Run

From this directory in PowerShell:

```powershell
.\Run-SchedulingBenchmark.ps1
```

If local execution policy blocks scripts, invoke it explicitly without
changing the machine policy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\Run-SchedulingBenchmark.ps1
```

An explicit reproducible invocation is:

```powershell
.\Run-SchedulingBenchmark.ps1 `
  -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' `
  -WarmupSamples 5 `
  -MeasuredSamples 15 `
  -AgentCounts '16,128' `
  -BudgetStepLimit 4 `
  -BatchSizes '8,32,128' `
  -WorkerThreadCounts '1,23' `
  -OutputPath '.\Results\my-run.json'
```

`WorkerThreadCounts` entries must fall within `[1, JobsUtility.JobWorkerMaximumCount]`
on the machine that runs the benchmark; the runner throws before doing any
work if one does not. The original `JobsUtility.JobWorkerCount` is restored
(including on failure) once the sweep finishes.

The script creates a fresh isolated Unity project under the system temporary
directory, copies `Runtime/`, `Authoring/`, `Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs`,
and this directory's `Unity/` folder into it, and pins Burst 1.8.30,
Collections 6.5.0, and Newtonsoft JSON 3.2.2. It leaves the isolated project
in place for inspection and prints its path; removal is intentionally manual.

The benchmark assembly deliberately uses the existing friend assembly name
`AIBT.Runtime.Tests` (the same technique `Benchmarks~/Phase2/Dispatch/` uses
with `AIBT.NativeBurstDispatch.Tests`) so it can construct the internal
Runtime native-execution types without widening production visibility.
Sources live under `Benchmarks~` and are only imported into the isolated
project by the runner; the package itself does not compile a duplicate test
assembly.

## Evidence and interpretation

The JSON contains environment/package metadata, command line, per-scenario
raw samples (elapsed ticks, nanoseconds per agent, managed-allocation delta),
descriptive min/median/p95/max summaries, and the documented-placeholder
list. The adjacent Unity log is the compilation/run diagnostic.

`BatchedJobsSameFrame` consistently measures far higher nanoseconds-per-agent
than `Immediate`/`Budgeted` at these small agent counts (16/128) and small
tree sizes -- this is a descriptive observation about fixed per-batch
scheduling overhead at this scale, not a claim that the policy is worse in
general; larger populations are exactly what a follow-up P4 card (batch-size
calibration) needs to characterize.

Within `BatchedJobsSameFrame` itself, `workerThreadCount=1` measures **lower**
median ns/agent than `workerThreadCount=23` (this machine's
`JobsUtility.JobWorkerMaximumCount`) in every implemented scenario at 16
agents, and the gap narrows -- sometimes to a near-tie -- at 128 agents. This
reproduced across independent runs and is a genuine, non-obvious observation:
at these small populations the fixed cost of waking/coordinating more worker
threads outweighs the parallelism gained, so "more worker threads" is not
free. `batchSize` (8/32/128) has a visibly smaller effect than
`workerThreadCount` throughout. None of this is a recommended worker-thread
setting -- it is exactly the kind of curve `Documentation~/execution-and-scheduling.md`'s
batch-size/worker-count calibration work (a later P4 card, at full population
scale) needs as raw input. No threshold or policy recommendation is drawn
here.

This Editor batchmode run measures one workstation; it is not a
cross-hardware-class result. `Planning~/USER_ACTIONS.md` requires owner
approval of acceptable regression thresholds across multiple hardware classes
before any such threshold is adopted.

## Recorded evidence

The canonical 2026-08-19 isolated run is preserved as
[raw JSON](Results/scheduling-windows-editor-20260819-165205.json). The
adjacent Unity log from that run is not committed, per repository policy
against committing raw Unity logs. It used Unity 6000.5.8f1, Collections
6.5.0, five warmups, fifteen measured samples per case, agent counts 16 and
128, budget step limit 4, batch sizes 8/32/128, and worker-thread counts
1/23 (this machine's `JobsUtility.JobWorkerMaximumCount`).

Immediate/Budgeted median ns/agent (batch size and worker-thread count are
not applicable to either policy):

| Scenario | Nodes | Immediate (16 / 128) | Budgeted (16 / 128) |
| --- | ---: | ---: | ---: |
| `scheduling-baseline-empty-job` | 1 | 2,862.50 / 2,846.09 | 3,000.00 / 2,989.06 |
| `shallow-tree-cheap-conditions` | 5 | 15,193.75 / 15,262.50 | 16,381.25 / 17,183.59 |
| `deep-sequence-selector-traversal` | 63 | 176,437.50 / 182,066.41 | 182,300.00 / 186,853.13 |
| `wide-branching-frequent-failures` | 17 | 5,075.00 / 5,189.84 | 5,300.00 / 5,568.75 |
| `predominantly-running-actions` | 5 | 3,500.00 / 3,328.13 | 3,575.00 / 3,491.41 |
| `many-programs-small-populations` | 1 | 2,862.50 / 2,887.50 | 2,993.75 / 2,979.69 |

`BatchedJobsSameFrame` median ns/agent by worker-thread count, batch size 32
(the middle of the swept range -- batch size moves these numbers far less
than worker-thread count does; see the JSON for all three batch sizes):

| Scenario | workerThreadCount=1 (16 / 128 agents) | workerThreadCount=23 (16 / 128 agents) |
| --- | ---: | ---: |
| `scheduling-baseline-empty-job` | 16,743.75 / 17,663.28 | 23,025.00 / 20,094.53 |
| `shallow-tree-cheap-conditions` | 73,662.50 / 64,017.97 | 127,075.00 / 71,092.19 |
| `deep-sequence-selector-traversal` | 791,487.50 / 542,257.81 | 1,118,168.75 / 648,411.72 |
| `wide-branching-frequent-failures` | 20,006.25 / 24,746.88 | 36,200.00 / 28,322.66 |
| `predominantly-running-actions` | 14,093.75 / 17,468.75 | 24,750.00 / 22,132.03 |
| `many-programs-small-populations` | 12,181.25 / 17,282.03 | 19,850.00 / 20,118.75 |

`workerThreadCount=1` beats `workerThreadCount=23` in every one of these
rows at 16 agents, sometimes by more than 2x; the gap shrinks and in a few
cases nearly closes at 128 agents. This reproduced across independent runs.
It is a genuine, non-obvious observation, not a recommendation: at these
small populations, the fixed cost of waking and coordinating more worker
threads outweighs the parallelism gained.

These are descriptive observations only, condensed for readability -- not
every scenario/agent-count/batch-size/worker-thread-count combination is
reproduced in these tables. The JSON is authoritative for all raw samples
(6 scenarios x 2 agent counts x [2 non-batched policies + 3 batch sizes x 2
worker-thread counts for `BatchedJobsSameFrame`] = 96 cases), environment
metadata, and min/p95/max summaries; the tables do not define a threshold.
