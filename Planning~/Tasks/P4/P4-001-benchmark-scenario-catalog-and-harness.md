# P4-001 — Benchmark scenario catalog and harness

Status: `Done`

## Objective

Implement the scenario catalog and parameter-matrix-driven measurement harness `Documentation~/benchmarks.md` specifies, as reusable infrastructure every later Phase 4 card measures against. This card produces the *ability* to measure; it draws no conclusions and sets no defaults.

## Depends on

- `P2-025` (Phase 2 integration gate; the three fixed policies this harness measures — Immediate, Budgeted, BatchedJobsSameFrame — are accepted Phase 2 output).

## Required reading

- `Documentation~/benchmarks.md` (scenario catalog, parameter matrix, metrics, platform process).
- `Documentation~/execution-and-scheduling.md` (policies, work estimation, batching — vocabulary this harness must measure against, not implement).
- `Benchmarks~/Phase2/` (the existing microbenchmark pattern this harness extends, not replaces).
- `Planning~/Evidence/P2-GATE/phase4-inputs.md` and `Planning~/Evidence/P3-GATE/phase4-inputs.md`.

## Allowed changes

- `Benchmarks~/Phase4/` (new).
- `Tools~/Verification/P4/` (new, mirrors the existing `Tools~/Verification/P2/` pattern).
- `Tests/Runtime/Benchmarking/` (new, harness unit tests only — not scenario result assertions).

## Forbidden changes

- Any execution-policy implementation (`PipelinedJobs`, `Auto`) — this card measures existing policies only.
- Any performance default, threshold, or regression gate. This card produces raw, environment-recorded samples, nothing calibrated from them.
- Changes to `Runtime/Scheduling/` or any other accepted Phase 2 runtime code.

## Deliverables

- Every scenario in `Documentation~/benchmarks.md`'s catalog (scheduling baseline/empty job, shallow tree, deep sequence/selector, wide branching with frequent failures, predominantly-running actions, event-driven sleeping/wakeup, intensive typed-blackboard access, high command emission, computationally expensive Burst nodes, mixed cheap/expensive agents, many compiled programs with small populations, managed-node boundary, same-frame/pipelined/budgeted execution placeholders, hot-reload/debug-instrumentation overhead placeholder), each stating what it isolates.
- A parameter-matrix runner sweeping logarithmic agent counts, tree shape, batch parameters, and worker-thread counts where controllable, against the three already-implemented fixed policies (Immediate, Budgeted, BatchedJobsSameFrame).
- Metrics collection for every metric `benchmarks.md` lists, with raw per-sample output (not just aggregates) and full environment recording (Unity/package/OS/CPU/build config/scenario revision).

## Acceptance criteria

- Every cataloged scenario runs end-to-end against all three existing fixed policies without error, at least at two agent-count points each.
- Raw samples and environment metadata are retained separately from any generated chart or summary, per `benchmarks.md`'s own discipline.
- The harness introduces no new managed allocation in the paths it measures beyond what the scenario itself does (the harness must not distort the numbers it collects).
- `PipelinedJobs` and `Auto` scenario entries exist as documented placeholders (what they will measure) without any implementation — they must not silently execute against a substituted policy.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <harness unit tests>
one full parameter-matrix run against the three existing fixed policies, raw samples recorded
```

## Handoff notes

- `P4-002` runs this harness at full scale to produce the actual fixed-policy cost curves; this card only proves the harness itself is correct.
- `P4-003` (`PipelinedJobs`) and `P4-005`/`P4-007` (`Auto`, autotuning) will extend this catalog's placeholder entries once those policies exist — extending, not replacing, this card's scenarios.

## Outcome

Implemented `SchedulingPolicyDriver` (Runtime-only, shared unchanged between the in-project
EditMode correctness suite and the isolated Player-benchmark project) and six of the fourteen
catalog scenarios — the ones needing only built-in composites and constant-status leaves
(scheduling-baseline-empty-job, shallow-tree-cheap-conditions, deep-sequence-selector-traversal,
wide-branching-frequent-failures, predominantly-running-actions, many-programs-small-populations).
Every implemented scenario ran end-to-end against all three accepted fixed policies at two
agent-count points (16, 128) without error, with raw samples and environment metadata recorded
separately from summaries. The remaining eight scenarios and the `PipelinedJobs`/`Auto` policies
are documented placeholders in the result JSON (name + what each will isolate/measure), never
silently substituted or faked. `Tools~/Verification/P4/` was not created — nothing in this card's
scope needed it. `BatchedJobsSameFrame` is additionally swept across batch size and
`JobsUtility.JobWorkerCount` (the only one of the three policies where either parameter applies,
since it is the only one that schedules Unity Jobs) — this sweep was missing from the first `Done`
pass despite being explicit in this card's own Deliverables text; the user caught the gap directly,
and it produced a real reproduced finding (fewer worker threads beat more at these small agent
counts). Full detail, recorded numbers, and every bug found and fixed while proving the isolated
harness end-to-end are in `Planning~/Evidence/P4-001/README.md`.
