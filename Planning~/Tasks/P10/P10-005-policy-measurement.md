# P10-005 — Jobs stage J3: measurement of the native policies and a proposal for automatic selection

Status: `Review`

## Objective

Measure the three native scheduling policies (`Immediate`, `BatchedJobsSameFrame`,
`JobsAgentFrame`) and the managed layer across agent counts, trees, job worker counts and mixed
populations, and write a proposal the owner can decide from: whether the automatic policy selector
should pick `JobsAgentFrame`, from which agent count, and what that would change for users.

This card measures and proposes. It changes no runtime behavior.

## Owner decisions for this card

- Measurement and proposal only. The owner decides later whether the selector changes.
- Unity 6000.7.0b1 was removed from the machine before the measurement. The owner chose to run the
  whole stage on Unity 6000.6.9f1. Numbers are therefore compared between policies inside one
  Player build. A quick-set run on the new build is shown next to the P10-004 numbers as "another
  Unity version", not as a before/after comparison.

## Depends on

- P10-004 (`JobsAgentFrame`, the step limit and the quick benchmark variants).

## Required reading

- `AGENTS.md`, `Planning~/AGENT_WORKFLOW.md`, `Documentation~/performance.md`,
  `Documentation~/decisions/ADR-P10-002-agent-frame-jobs.md`,
  `Planning~/Evidence/P10-001/README.md` … `Planning~/Evidence/P10-004/README.md`,
  `Benchmarks~/Phase10/PlayerPerf/README.md`.

## Scope

- `Benchmarks~/Phase10/PlayerPerf/` (new scenario sets, CPU-time columns, `-Set`, the J3 analysis
  script), `Documentation~/performance.md` (a "which policy when" table), `CHANGELOG.md`,
  `Planning~/Evidence/P10-005/`.

## Forbidden changes

- Scheduler defaults, the automatic policy selector, execution semantics, ABI v2
  (`ExpectedPublicAbiV2.txt`), the package version, existing public APIs.
- Runtime code at all, except a small, clearly correct, test-covered fix that changes no observable
  behavior. Any other defect found is reported, not fixed.
- Pushing, or changing/committing the parent repository outside the submodule.

## Measurement plan

All in an IL2CPP non-development Windows Player of the harness, policies of one tree and N adjacent
in the plan, so every run interleaves them:

1. Small N (`j3-small`): N = 1, 10, 50, 100, 200, 500; `io-wait1`, `shared-loop16`,
   `shared-reactive16`, `io-move1-fact-effect`; three policies. Crossover: the smallest N from which
   `JobsAgentFrame` is at least 10% faster than `Immediate` on the main thread, and whether it is
   ever more than 5% slower. µs per frame, not only ratios.
2. Scale (`j3-scale`): N = 1 000, 5 000, 20 000, same trees and policies, plus `m-shared-shape15` and
   `m-unity15-both`; main thread, frame wall time, total CPU.
3. Worker count (`j3-workers` with `-perfJobWorkers 0, 1, 3, 7` and the default): a simulation on a
   desktop CPU of what fewer cores could see.
4. Mixed population (`j3-mixed`): half the `JobsAgentFrame` hosts fall back (`StepBudget`).
5. The non-quick scenarios of the harness under `Immediate`, once (`full-immediate`).
6. Native `UpdateCadence` with staggered enabling under `JobsAgentFrame` (`j3-cadence`).
7. The quick set on the new build, to relate it to P10-004.

## Deliverables

- Evidence with tables, crossover per tree, the proposal with options (keep manual / selector above
  a threshold / default for `NativeTreeAgent`), the native-vs-managed standing per agent, the largest
  remaining main-thread cost under `JobsAgentFrame`, and a "Not done / not verified" section.
- `Documentation~/performance.md`: "which policy when" table; harness README; `CHANGELOG.md`.

## Required verification

```text
Tools~/Verification/Verify-Static.ps1
git diff --stat (no file under Runtime/, Agents/, Authoring/, Editor/ changed; else the four test assemblies)
```

## Handoff notes

- Evidence: `../../Evidence/P10-005/README.md`. Raw data:
  `C:\UnityProjects\_aibt_acceptance\perf-data\P10-005\`.
