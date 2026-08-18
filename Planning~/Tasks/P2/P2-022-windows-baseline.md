# P2-022 — Windows Player conformance and Phase 2 baseline

Status: `Done`

## Objective

Prove the generated native executor in a non-Development Windows x64 IL2CPP/Burst Player and publish a measured Phase 2 microbenchmark baseline.

## Depends on

- `P2-019`.
- `P2-020`.
- `P2-021`.

## Required reading

- `Documentation~/benchmarks.md`
- `Documentation~/specifications/platform-backends-v1.md`
- `Planning~/DEFINITION_OF_DONE.md`

## Allowed changes

- `Benchmarks~/Phase2/Windows/`
- `Tools~/Verification/P2/Windows/`
- `Planning~/Evidence/P2-WINDOWS/`

## Forbidden changes

- Auto/autotuning, regression thresholds, universal crossover/default claims, or conclusions about Android/Web/console.

## Deliverables

- Repeatable Windows Player conformance run and raw baseline scenarios for scheduling overhead, cheap tree, blackboard-heavy, command-heavy, and mixed populations.

## Acceptance criteria

- IL2CPP/Burst Player executes the representative behavior matrix and generated user node without Burst fallback/errors.
- Raw repetitions after warmup record p50/p95/p99 frame contribution, steps/s, commands/s, scheduling/completion cost, native bytes per program/instance, and GC signal.
- Scenario, source, environment, build, and artifact digests are recorded and schema-valid.
- Controlled-invalid scenario fails; generated logs/binaries are not committed.
- No policy threshold or performance default is inferred from one workstation.

## Required verification

```text
non-Development Windows x64 IL2CPP/Burst build
Player behavior-case matrix
allocation summary cross-check
benchmark result schema validation
artifact and source digest verification
```

## Handoff notes

- Phase 4 owns calibrated scheduler policies and broader benchmark research.

