# P4-008 Windows x64 platform benchmark evidence

Runs `P4-001`'s exact scenario/policy sweep (`SchedulingScenarios.cs` and
`SchedulingPolicyDriver.cs`, copied in unchanged) inside a real, non-development, IL2CPP,
Burst-enabled Windows x64 Standalone Player -- every other Phase 4 benchmark ran only in Editor
batchmode. This is evidence, not a threshold: `Planning~/USER_ACTIONS.md` requires owner approval
of hardware classes and thresholds after this research exists, not as a byproduct of running it.

## Scope: Windows x64 only

This session had no Android ARM64 device/emulator and no WebGL-capable browser access, so only
the Windows x64 mandatory pre-1.0 target is measured here. Android ARM64 and single-thread Unity
Web are **not measured** -- a disclosed gap, not a silent omission; per `benchmarks.md`'s own rule,
results from one platform are never presented as establishing support for another.

## Build and run

`Run-WindowsPlatformBenchmark.ps1` builds a fresh isolated Unity project (copying `Runtime/`,
`Authoring/`, `Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs`, and
`Benchmarks~/Phase4/Scheduling/Unity/SchedulingScenarios.cs` unchanged, alongside this card's own
`Unity/` folder), builds a **release** (non-development), **IL2CPP**, **Burst-enabled**
`StandaloneWindows64` Player from it (`Unity/Editor/WindowsPlatformBenchmarkBuild.cs`), then runs
the built `.exe` -- `Unity/Runtime/WindowsPlatformSchedulingProbe.cs` runs the same 6-scenario x
4-agent-count x 3-policy sweep via a `[RuntimeInitializeOnLoadMethod]` hook and writes JSON before
quitting. Build proof (target, architecture, scripting backend, Burst, non-development) is
recorded in the adjacent `-build.raw.json`; the PowerShell driver verifies all of it before
accepting the run.

```powershell
.\Run-WindowsPlatformBenchmark.ps1
```

## Result: the release Player is ~13-14x faster than the same scenarios in the Editor

Every prior Phase 4 benchmark (`P4-001`, `P4-002`, `P4-006`) ran in Editor batchmode. Comparing
`Immediate`-policy medians for the same scenarios at the same agent counts:

| Scenario | Editor median ns/agent (16 / 1024) | Player median ns/agent (16 / 1024) | Editor/Player ratio (16 / 1024) |
| --- | ---: | ---: | ---: |
| `scheduling-baseline-empty-job` | 2,912.50 / 2,909.18 | 218.75 / 225.98 | 13.31x / 12.87x |
| `shallow-tree-cheap-conditions` | 15,456.25 / 15,485.25 | 1,125.00 / 1,083.30 | 13.74x / 14.30x |
| `deep-sequence-selector-traversal` | 177,768.75 / 178,592.48 | 12,625.00 / 12,437.99 | 14.08x / 14.36x |
| `wide-branching-frequent-failures` | 5,112.50 / 5,157.42 | 356.25 / 375.59 | 14.35x / 13.73x |
| `predominantly-running-actions` | 3,437.50 / 3,439.36 | 268.75 / 254.39 | 12.79x / 13.52x |
| `many-programs-small-populations` | 2,881.25 / 2,954.00 | 218.75 / 221.48 | 13.17x / 13.34x |

The ratio is remarkably consistent (12.8x-14.4x) across every scenario and both agent-count
extremes -- this is not noise, it is release IL2CPP/Burst-compiled native code genuinely running
much faster than the same call paths under the Editor's Mono/JIT execution and additional Editor
overhead. **Every Editor-measured number in `P4-001`/`P4-002`/`P4-005`/`P4-006`/`P4-007`'s evidence
understates real release performance by roughly an order of magnitude on this workstation.** This
is a real, actionable finding for anyone reading those cards' numbers as a performance signal, not
a new claim this card is authorized to turn into a threshold.

`BatchedJobsSameFrame`'s own fixed-batch-size overhead (the mechanism `P4-002`/`P4-006` already
traced) is present in the Player too -- e.g. `deep-sequence-selector-traversal` at 1024 agents:
148,613.57 ns/agent (`BatchedJobsSameFrame`) vs. 12,437.99 ns/agent (`Immediate`), an 11.95x gap,
in the same range as the Editor's own 3.1x-3.5x-to-orders-of-magnitude gaps recorded in
`Benchmarks~/Phase4/CostCurves/README.md`. The underlying scheduling-overhead mechanism `P4-002`
found is not an Editor artifact.

## Environment

Unity 6000.5.8f1, `StandaloneWindows64` / x86_64, IL2CPP, Burst enabled, non-development build,
five warmup samples and fifteen measured samples per case, agent counts 16/64/256/1024. Full
environment (OS, CPU, logical/worker counts, pinned package versions) is recorded per case-run in
the JSON. One run on one workstation; not generalized to other hardware
(`Planning~/USER_ACTIONS.md` requires owner approval across multiple hardware classes before any
threshold is adopted).

## Recorded evidence

The canonical 2026-08-21 run is preserved as
[raw JSON](Results/windows-player-scheduling-20260821.json) and
[build evidence](Results/windows-player-scheduling-20260821-build.raw.json). The adjacent Unity
build/player logs are not committed, per repository policy against committing raw Unity logs.
