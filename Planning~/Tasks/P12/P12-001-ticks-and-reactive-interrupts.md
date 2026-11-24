# P12-001 — Durations in ticks and reactive interrupts in the simulation container

Status: `Review` — done 2026-11-24 from the orchestrator's brief
(`C:\UnityProjects\_aibt_acceptance\SweetKingdom\brief-P12-001.md`), local commits on
`feature/aibt-consumer-workflow`; acceptance is the orchestrator's. Evidence: `Planning~/Evidence/P12-001/README.md`.

## Objective

Make two answers to the SweetKingdom consumer true and pinned by tests, with no new node, no ABI change and
no change of execution semantics:

- **Durations in ticks (request 3).** The container clock is `tick x TickMicroseconds` and every library
  duration is `deadline = now + d`, reached when `now >= deadline`. So a duration of `n x TickMicroseconds`
  ends after exactly n ticks, and with `TickMicroseconds = 1` every "microseconds" parameter is a tick
  count. `aibt.stdlib.wait` counts its own Tick calls (thinks), not simulation ticks.
- **Reactive interrupts (request 4).** `aibt.core.reactive-selector-retaining` with a guard before a long
  branch, a dedicated trigger input channel with wake on change and a positions channel without wake,
  preempts in the step after the write; what wake on change costs at 500 agents is measured.

## Decision record

- Orchestrator, 2026-11-24, under the owner's delegation of 2026-11-23:
  `C:\UnityProjects\_aibt_acceptance\SweetKingdom\plan-2026-11-24.md`, decisions 3 and 4. Tick-native
  nodes and decorator versions are not reopened (owner's D7(a) of P10-009). The facts the decisions rest on:
  `requests-fact-check.md` in the same folder.

## Depends on

- P10-012 (the simulation container G4), P11-002 (the native node library).

## Required reading

- `Documentation~/simulation-container.md`, `Documentation~/native-node-library.md`.
- `Documentation~/decisions/ADR-P10-008-simulation-container.md` (D7, D9, Q1, Q2).
- `Tests/Integration/Simulation/NativeLibraryTimingSimulationTests.cs`,
  `Tests/Integration/Simulation/TreeSimulationIoTests.cs`,
  `Tests/Integration/Simulation/TreeSimulationActionTests.Retaining.cs`.

## Scope

- New and changed tests under `Tests/Integration/Simulation/`.
- `Documentation~/simulation-container.md` (two sections), `CHANGELOG.md` (one line), a sentence elsewhere
  in `Documentation~` that says or suggests that `aibt.stdlib.wait` counts simulation ticks.
- Display-only fixes for simulation time shown to a person or a tool, if small.
- A benchmark scenario or harness for the wake cost measurement.
- `Planning~/Evidence/P12-001/README.md`; raw data in `C:\UnityProjects\_aibt_acceptance\P12-001\`.

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format and the bytes of
  programs that compile today, `CodeGen~`, `package.json`, pins under `Tests/Fixtures`,
  `Documentation~/specifications`, execution semantics, existing public signatures.

## Deliverables

- Tests that pin both recipes for every time node and decorator, the `Spread(5)` next-think rounding, the
  in-place and jobs equality, and what `aibt.stdlib.wait` counts.
- A list of every display of simulation time and whether it misleads with `TickMicroseconds = 1`.
- Tests of the consumer's exact reactive pattern and of its three conditions, and one of `TryWakeMany`.
- Sections "Durations in ticks" and "Reactive interrupts" in the container documentation; a CHANGELOG line.
- An interleaved measurement of the wake cost at 500 agents, `Spread(5)`, in place and in jobs.

## Acceptance criteria

- The new tests pass; every statement in the two sections names a test.
- No protected file changed; `Tools~/Verification/Verify-Static.ps1` passes.
- The measurement reports medians and spread over at least five interleaved rounds; a difference inside the
  spread is called "not confirmed".
- If a fact differs from the decision (a duration not exact, no preemption in the same step), the evidence
  says so instead of a test bent to pass.

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1, then Console check
Tools/Unity/Invoke-UnityTests.ps1 -Filter <each new or changed test class>
Tools~/Verification/Verify-Static.ps1
Full run of the ten assemblies: the orchestrator, at acceptance.
```

## Evidence

`Planning~/Evidence/P12-001/README.md`.
