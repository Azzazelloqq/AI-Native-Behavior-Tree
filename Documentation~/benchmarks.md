# Benchmark and platform research

## Purpose

Benchmarks determine execution-policy and batching defaults, expose regressions, and define evidence-based platform support. They do not exist to produce one universal agent-count threshold.

## Scenario catalog

- scheduling baseline and empty job;
- shallow tree with cheap conditions;
- deep sequence and selector traversal;
- wide branching and frequent failures;
- predominantly running actions;
- event-driven sleeping and wakeup;
- intensive typed-blackboard access;
- high command emission;
- computationally expensive Burst nodes;
- mixed cheap and expensive agents;
- many compiled tree programs with small populations;
- managed-node boundary;
- same-frame, pipelined, and budgeted execution;
- hot-reload and debug-instrumentation overhead.

Every synthetic scenario states what it isolates. Representative game-like scenarios combine features without replacing focused microbenchmarks.

## Parameter matrix

- logarithmic agent counts from one agent to platform capacity;
- tree depth, width, active-path length, and wakeup ratio;
- batch-size and batch-count ranges;
- worker-thread counts where controllable;
- Immediate, fixed Jobs, Budgeted, and Auto policies;
- Burst enabled and disabled for diagnosis;
- same-frame and pipelined latency;
- debug instrumentation levels;
- Development and non-Development Player builds.

## Metrics

- main-thread scheduling and completion time;
- worker execution time and throughput;
- node steps and commands per unit time;
- p50, p95, and p99 frame contribution;
- budget misses and deferred work;
- GC allocations after warmup;
- native memory footprint per compiled tree and agent;
- batch imbalance and variance;
- compilation/import and hot-reload cost where relevant.

## Platform process

Run in Player builds on identified hardware. Record Unity, package, OS, CPU architecture, logical and worker counts, build configuration, thermal/power conditions where relevant, scenario revision, and raw samples.

Initial targets are selected before Phase 4. Desktop development results cannot be generalized to mobile, console, or WebGL. Unsupported platforms remain explicitly unclaimed.

## Scheduler research

1. Measure scheduling overhead and fixed-policy curves.
2. Calibrate work units and initial batch targets per platform class.
3. Implement deterministic explainable heuristics.
4. Compare Auto against best fixed policies across the scenario matrix.
5. Test lightweight adaptation only if fixed heuristics leave meaningful performance on the table.
6. Reject adaptation if its overhead, instability, or unpredictability outweighs improvement.

Raw results and analysis are retained separately from generated charts. Regression thresholds use statistically stable ranges rather than a single best run.

