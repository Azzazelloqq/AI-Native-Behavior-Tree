# P10-002 — Jobs speed-up, stage J1: quick benchmark, frame profile, round overhead, equivalence guard, J2 design

Status: `Review`

## Objective

`BatchedJobsSameFrame` is slower on the main thread than `Immediate` at every measured agent count
and allocates about 1.1 KB per agent tick (P10-001). Stage J1 makes that measurable quickly,
replaces the code-reading explanation with a measured frame profile, removes the per-round
overhead and allocations without changing semantics, decides whether idle agents can be skipped,
adds an Immediate-versus-Jobs equivalence test as the safety net for later stages, and writes a
spike-verified design for stage J2 (a job advances an agent through its whole frame).

## Depends on

- P10-001 (managed zero-allocation tick, native facade scheduling, Player harness).

## Required reading

- `AGENTS.md`, `Planning~/AGENT_WORKFLOW.md`, `Documentation~/performance.md`,
  `Planning~/Evidence/P10-001/README.md`, `Benchmarks~/Phase10/PlayerPerf/README.md`.

## Scope

- `Runtime/Scheduling/Production/` (batched group driver, scheduler, frame profile aid),
  `Runtime/Scheduling/Native/` (round pool), `Runtime/Integration/ProductionTreeHost.cs`
  (profile laps only).
- `Tests/Integration/NativeRuntime/` (equivalence and allocation tests).
- `Benchmarks~/Phase10/PlayerPerf/` (quick mode, frame profile), `Spikes~/P10-002-J2/`.
- `Documentation~/decisions/` (draft ADR for J2, status Proposed), `Documentation~/performance.md`,
  `Planning~/Evidence/P10-002/`.

## Forbidden changes

- Execution semantics (tick order, statuses, effect delivery order, traces, diagnostics),
  scheduler defaults, ABI v2 (`ExpectedPublicAbiV2.txt`), the package version, existing public APIs
  (additive changes only). A change that needs any of these is reported, not made.

## Deliverables

- `Invoke-Perf.ps1 -Quick` (20 cases, 2 runs) with a saved baseline and a before/after comparison
  that flags single-threaded scenarios more than 5% slower; its duration recorded.
- A per-stage profile of a Jobs frame and an Immediate frame in the IL2CPP Player
  (`AIBT_SCHEDULER_FRAME_PROFILE`, compiled out otherwise).
- Reused round storage in the BatchedJobsSameFrame driver and a zero-allocation test.
- Idle-agent skipping: implemented with proven equivalence, or the reason it is not.
- `JobsPolicyEquivalenceTests`: default, forced Immediate and forced BatchedJobsSameFrame give the
  same per-frame traces, statuses and effects.
- Draft ADR for J2 (Proposed) with spike evidence; quick before/after numbers (IL2CPP).

## Acceptance criteria

- The equivalence test passes before and after the runtime change.
- A warmed-up BatchedJobsSameFrame frame of non-restarting trees allocates no managed memory (test).
- Quick IL2CPP numbers: Jobs scenarios faster than the baseline; no single-threaded scenario more
  than 5% slower.
- Existing tests unchanged and green (known host-only failures excepted).

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Assemblies <every AIBT test assembly>   (and -Mode playmode)
Tools~/Verification/Verify-Static.ps1
Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1 against c54bbaf
AibtPerfTools/Invoke-Perf.ps1 -Step Run -Quick   (IL2CPP, before and after)
```

## Handoff notes

- See `../../Evidence/P10-002/README.md` for numbers, the profile, the J2 design summary and what
  was not done. Raw measurement data: `C:\UnityProjects\_aibt_acceptance\perf-data\P10-002\`.
- Outcome:
  - **Allocations.** A warmed-up BatchedJobsSameFrame frame allocates 0 B; the test fails on
    `c54bbaf` and passes now.
  - **Equivalence.** The equivalence test passes before and after.
  - **Jobs timings.** Jobs quick cases take 11–48% less main-thread time.
  - **Single-threaded timings.** The interleaved (ABBA) re-run shows no single-threaded case more
    than 5% slower. Three cases were flagged in the sequential run (+5.6%, +5.6%, +6.4%); they were
    run-to-run noise, which reaches up to 7.3% for the same Player.
  - **Not done.** Idle-agent skipping (needs new node contracts) and the after-profile.
