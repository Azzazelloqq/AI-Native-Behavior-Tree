# Phase 4 inputs

Prepared 2026-08-18 for the `P2-025` review. Phase 4 owns benchmark research, the
remaining execution policies, and calibrated scheduling. Phase 2 deliberately
produced no threshold, default, or crossover point for it to inherit.

## What Phase 4 inherits

- Three implemented fixed policies with proven identical semantics: Immediate,
  one-step Budgeted, and BatchedJobsSameFrame.
- A reproducible isolated Player harness pattern with frozen analyzer, Runtime, and
  harness digests, plus schema-validated raw and acceptance evidence. The Windows
  variant under `Benchmarks~/Phase2/Windows/` is the reference shape for future
  platform harnesses.
- An allocation gate that measures initialized windows with a controlled sensitivity
  canary, so a zero-GC assertion is falsifiable.
- Microbenchmark results for generated dispatch on the reference workstation and
  one headless Web run, in `Benchmarks~/Phase2/` and `Benchmarks~/Platform/Web/`.

## Required before any scheduling claim

1. Close `P2-022` first. A scheduler cannot be calibrated against a platform that
   has never produced a Player baseline.
2. Build the scenario catalog before the policies: scheduling overhead, cheap
   trees, blackboard-heavy, command-heavy, async-heavy, and mixed populations at
   several agent counts.
3. Implement `PipelinedJobs` and prove semantic equivalence against the reference
   oracle before measuring it.
4. Define work estimation as an explicit, inspectable model. `Auto` must be able to
   explain a decision, not only make one.
5. Resolve `OQ-006` with evidence: runtime autotuning must beat calibrated fixed
   heuristics on recorded hardware, or it is not adopted.
6. Establish regression thresholds only after multiple hardware classes exist, with
   owner approval per `USER_ACTIONS.md`.

## Constraints Phase 4 must not violate

- No default, threshold, or crossover point derives from a single workstation.
- Scheduling may change timing and latency, never tree semantics.
- Every published number records environment, build, warmup, and raw samples.
- `GC.GetTotalMemory` is not a zero-allocation proof; the Unity recorder is.
- Android device and Safari or mobile Web claims wait for the hardware access
  listed in `USER_ACTIONS.md`.
