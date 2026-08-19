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
(16 and 128 by default).

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
  -BatchSize 32 `
  -OutputPath '.\Results\my-run.json'
```

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
calibration) needs to characterize. No threshold or policy recommendation is
drawn here.

This Editor batchmode run measures one workstation; it is not a
cross-hardware-class result. `Planning~/USER_ACTIONS.md` requires owner
approval of acceptable regression thresholds across multiple hardware classes
before any such threshold is adopted.

## Recorded evidence

The canonical 2026-08-19 isolated run is preserved as
[raw JSON](Results/scheduling-windows-editor-20260819-162029.json). The
adjacent Unity log from that run is not committed, per repository policy
against committing raw Unity logs. It used Unity 6000.5.8f1, Collections
6.5.0, five warmups, fifteen measured samples per case, agent counts 16 and
128, budget step limit 4, and batch size 32.

| Scenario | Nodes | Immediate median ns/agent (16 / 128) | BatchedJobsSameFrame median ns/agent (16 / 128) |
| --- | ---: | ---: | ---: |
| `scheduling-baseline-empty-job` | 1 | 3,231.25 / 3,135.16 | 17,862.50 / 18,093.75 |
| `shallow-tree-cheap-conditions` | 5 | 15,012.50 / 15,173.44 | 118,906.25 / 70,286.72 |
| `deep-sequence-selector-traversal` | 63 | 175,368.75 / 176,136.72 | 1,196,581.25 / 741,054.69 |
| `wide-branching-frequent-failures` | 17 | 5,187.50 / 5,184.38 | 35,425.00 / 27,702.34 |
| `predominantly-running-actions` | 5 | 3,381.25 / 3,346.88 | 21,156.25 / 19,851.56 |
| `many-programs-small-populations` | 1 | 2,856.25 / 2,797.66 | 19,150.00 / 19,245.31 |

These are descriptive observations only. The JSON is authoritative for all
raw samples, `Budgeted` figures, environment metadata, and min/p95/max
summaries; the table does not define a threshold.
