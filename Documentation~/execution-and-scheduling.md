# Execution and scheduling

## Scheduling responsibility

AIBT does not assign operating-system threads. Unity Job System owns worker threads and work stealing. AIBT's execution scheduler selects when and how work is submitted:

- immediate or scheduled;
- same-frame or pipelined;
- batch size and number of work items;
- per-frame budget and update cadence;
- fallback behavior for managed nodes.

## Policies

| Policy | Intended use | Latency |
| --- | --- | --- |
| `Immediate` | Small workloads, tests, debugging | Current frame |
| `BatchedJobsSameFrame` | Parallel speed-up where results are needed immediately | Current frame after completion |
| `PipelinedJobs` | High throughput where one-frame delay is acceptable | Next frame or explicit pipeline stage |
| `Budgeted` | Very large populations or non-critical AI updates | Configurable |
| `Auto` | Explainable selection among supported policies | Determined by allowed latency |

Automatic policy never opts into extra semantic latency unless the user explicitly permits it.

## Work estimation

Agent count is not a sufficient threshold. The scheduler estimates work using observable units:

```text
estimated work = runnable agents
               × expected node steps per agent
               × calibrated node-cost units
```

Inputs may include compiled program identity, recent node-step counts, running-path depth, event wakeups, command volume, cost categories, worker count, platform profile, and configured budget. Estimates are smoothed and bounded so a single spike does not cause unstable policy changes.

The initial implementation uses deterministic benchmark-calibrated heuristics. Runtime adaptation is added only if it consistently outperforms fixed policies without unacceptable measurement cost or oscillation.

## Batching

Batching targets useful work per batch, not a universal number of agents:

```text
batch size = target batch work / estimated work per agent
```

The result is clamped by policy and memory limits. Cheap homogeneous trees can use larger batches; expensive or divergent trees use smaller batches. The scheduler creates enough batches for worker load balancing without flooding the job queue with tiny tasks.

Agents are primarily grouped by compiled program and compatible execution phase. Small groups may be coalesced into a shared work queue to avoid scheduling one job per tree.

## Explainability and overrides

The profiler exposes:

- chosen policy and reason;
- workload estimate and confidence;
- batch size and batch count;
- scheduling and completion cost;
- worker utilization proxy;
- node steps, commands, wakeups, and deferred agents;
- comparison with the configured budget.

Users can force a policy, minimum job workload, target batch work, batch bounds, update budget, latency mode, and tree-specific update cadence.

## Semantic guarantees

- Scheduler changes cannot reorder children within an agent's behavior-tree semantics.
- Explicitly shared blackboard writes require a declared conflict policy.
- Command application order is deterministic when deterministic mode is enabled.
- Pipelined and budgeted latency is visible and never silently selected.
- Managed nodes execute only in an allowed main-thread phase.
