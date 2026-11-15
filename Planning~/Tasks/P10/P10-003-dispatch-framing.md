# P10-003 — Cheaper managed dispatch framing around native node bodies (stage F1)

Status: `Review`

## Objective

The P10-002 frame profile shows that dispatch takes 70–90% of a native frame under both Immediate
and BatchedJobsSameFrame, and that most of it is the managed framing around the Burst node body
(activation generation, memory clear, tree-value snapshot and commit, workspace lease, command
publication, trace recording, effect capture). Before stage J2, make that framing cheaper on the
main thread, so both single-threaded and Jobs execution gain, with identical observable behavior.

## Depends on

- P10-002 (quick benchmark, frame profile, equivalence test).

## Required reading

- `AGENTS.md`, `Planning~/AGENT_WORKFLOW.md`, `Documentation~/performance.md`,
  `Planning~/Evidence/P10-002/README.md`, `Benchmarks~/Phase10/PlayerPerf/README.md`.

## Scope

- `Runtime/Integration/` (`ProductionTreeHost`, generated dispatch adapter, group executor),
  `Runtime/Scheduling/Production/` (dispatch resolver, batched group driver, frame profile aid),
  and the internals they call: `Runtime/Execution/Burst/Dispatch/` (workspace owner, input
  validation, bridge core), `Runtime/Nodes/Contracts/` (binding bridge core; internal members of
  the carrier structs only), `Runtime/Scheduling/Native/` (round pool).
- `Tests/Integration/NativeRuntime/` (equivalence and allocation tests),
  `Tests/Runtime/NativeExecution/Dispatch/` (input validation).
- `Benchmarks~/Phase10/PlayerPerf/` (profile parsing), `Documentation~/performance.md`,
  `Documentation~/decisions/ADR-P10-002-agent-frame-jobs.md` (a note only, status stays Proposed),
  `CHANGELOG.md`, `Planning~/Evidence/P10-003/`.
- The `ProductionBatchedGroupDriverV1.Dispose()` pool leak when disposed during a round (P10-002
  defect), with a test.

## Forbidden changes

- Execution semantics (tick order, statuses, traces, effects and their order, diagnostics),
  scheduler defaults, ABI v2 (`ExpectedPublicAbiV2.txt`), the package version, existing public or
  low-level APIs (additive changes only), and anything that requires regenerated catalogs on the
  consumer side. A speed-up that needs any of these is described with its measured gain, not made.

## Deliverables

- A per-step profile of one dispatch on the single-host path and the group path
  (`AIBT_SCHEDULER_FRAME_PROFILE` only), measured in the IL2CPP Player on the quick set, with the
  share of the Burst body; raw data kept.
- Framing optimizations, largest measured cost first, each kept only when it measurably helps.
- An explanation of the group-path outlier (`n-io-move1-fact-effect-jobs`, N = 5000) and a fix if
  one exists without a semantic change.
- Tests that fail on changed behavior; a warmed-up Immediate zero-allocation check through
  `NativeTreeAgent` (or its measured number).
- Interleaved (ABBA) before/after IL2CPP numbers against a Player built from `4dee8ec`, and native
  Immediate versus the managed controls.

## Acceptance criteria

- Equivalence tests pass before and after each runtime change.
- No single-threaded quick scenario is more than 5% slower in both interleaved comparisons.
- Public API identical to `4dee8ec` except additive changes; ABI v2 unchanged.
- Existing tests unchanged and green (known host-only failures excepted).

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Assemblies AIBT.Runtime.Tests, AIBT.Editor.Tests,
    AIBT.Integration.Tests, AIBT.BehaviorCases.Tests   (and AIBT.PlayMode.Tests)
Tools~/Verification/Verify-Static.ps1
Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1 against 4dee8ec
AibtPerfTools/Invoke-Perf.ps1 -Step Run -Quick   (IL2CPP, interleaved before/after)
```

## Handoff notes

- See `../../Evidence/P10-003/README.md`. Raw data: `C:\UnityProjects\_aibt_acceptance\perf-data\P10-003\`.
- Result: native frames 40–58% faster under Immediate and 31–78% under BatchedJobsSameFrame
  (interleaved, IL2CPP); the group-path outlier is fixed; the pool leak is fixed with a test.
- Not made (forbidden here, described with estimates in the evidence): a Burst-compiled
  `ExecuteImmediate` (generator change), dropping the generated per-call catalog check (generator
  change), carriers that hold a handle instead of a copy of the backing (public struct layout).
- Open defect found, not fixed: destroying the scheduler inside a batched round throws
  `ArgumentOutOfRangeException` in `ProductionTreeScheduler` after the round (evidence, section 8).
