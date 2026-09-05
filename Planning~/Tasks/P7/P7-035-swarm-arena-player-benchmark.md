# P7-035 — Swarm Arena deterministic Player benchmark

Status: `Draft`

## Objective

Measure the real Swarm Arena workload in release Player builds. Produce reproducible evidence for
global budget behavior, custom profiles, `Auto`, plain-loop policies, same-frame Jobs and
`PipelinedJobs` without changing the workload between comparisons.

This card supplies the evidence needed to calibrate any numeric built-in profile/budget defaults. It
does not begin with a desired result.

## Depends on

- `P7-033` — the production coordinator and policy/profile instrumentation.
- `P7-034` — the finished deterministic gameplay workload.
- `P7-024` — reporting/provenance discipline and current fixed-policy Player baseline.
- `P7-037` — production generated-node dispatch measured by the benchmark.
- `P4-008` — release Player benchmark/build conventions.

## Required reading

- `Documentation~/benchmarks.md`, `Documentation~/scheduling-benchmark-report.md` and
  `Documentation~/compatibility-matrix.md`.
- `Benchmarks~/Phase4/Platform/` harnesses and raw-result conventions.
- P7-033/P7-034 evidence and the exact scripted workload contract.

## Measurement design

- Run the same fixed seed, spawn layout, input timeline, tree documents, node catalog and gameplay
  phases for every policy/profile comparison.
- Separate warmup from measured samples and retain raw samples.
- Choose at least three population points after a disclosed smoke calibration. Keep overload and
  losing points; never rerun merely to improve presentation.
- Record AIBT execution time separately from movement/spatial-query work, rendering and whole-frame
  time. FPS alone is not evidence of scheduler cost.
- Include a rendering-disabled run for CPU isolation and a normal rendered run for end-to-end
  playability, both using the same simulation work.
- Compare `Auto`, `Immediate`, `Budgeted`, `BatchedJobsSameFrame` and `PipelinedJobs` only where the
  active backend supports them. Record pipeline latency and result-application phase explicitly.

## Required recorded fields

- median/p95/p99 AI time per frame and ns/agent;
- node steps per agent and per frame;
- registered, runnable, completed and deferred agents;
- allocated/consumed global budget and over-budget frames;
- requested versus observed update latency per profile;
- chosen policy/reason, confidence, batch size/count and worker count;
- managed allocation after warmup;
- movement/spatial-query, rendering and total frame timings;
- workload/content hashes, seed, scene/build identity, Unity/package versions, backend, Burst,
  IL2CPP, development flag, OS, CPU/device and thermal/power disclosure.

## Allowed changes

- `Benchmarks~/Phase7/SwarmArena/` isolated build/run harness and deterministic result schema.
- Benchmark-only instrumentation required to separate the timing domains above, without changing
  production behavior.
- Player result JSON, concise report/chart updates and `Planning~/Evidence/P7-035/`.

## Forbidden changes

- No scheduler, profile, tree, node, gameplay or population-selection tuning after measurement
  begins. A discovered defect stops the canonical run and is fixed in its owning card with the
  attempted result retained as invalid evidence.
- No Editor numbers presented as Player performance.
- No cross-device/platform generalization from one machine.
- No hidden discarded samples, fabricated throughput, fixed pass threshold or default-policy change
  inside the measurement card.
- No benchmark build that bypasses the public integration used by the imported showcase.

## Acceptance criteria

- A Windows x64 non-development IL2CPP/Burst Player completes the deterministic matrix and writes
  valid raw JSON with verified workload/content identity.
- Every reported number is reproducible from committed raw samples through a committed script.
- Results distinguish scheduler cost, gameplay simulation cost, rendering and total frame time.
- `PipelinedJobs` is measured with its real delayed result semantics or explicitly blocked by a
  verified implementation defect; it is never represented by same-frame data.
- Budget/profile results report latency and starvation, not just throughput.
- Allocation claims are measured after warmup.
- Android is added only on a real ARM64 device; lack of a device is disclosed and does not inherit
  Windows results.
- Conclusions remain bounded to the measured scene, populations and hardware.

## Required verification

```text
Verify-Static.ps1
benchmark schema/provenance validation
release Windows x64 IL2CPP/Burst Player build and run
spot-check every reported aggregate against raw JSON
git diff --check
```

The output informs a separate accepted decision for numeric defaults. Measurement alone does not
silently change public scheduler behavior.
