# P10-014 — Fixes from the P10-013 code review

Status: `Review` — part A (defects) and part B (coverage, docs) done 2026-11-19, touched tests pass (evidence `Planning~/Evidence/P10-014/README.md`); the full run of all nine assemblies on `c68df77` passed at acceptance on 2026-11-19 (same evidence, "Acceptance run"); the owner kept the F-S2-2 fix and answered the four open questions the same day (evidence, "Owner decisions on the open questions"; follow-ups P10-015 and P10-016). Part C
(the IL2CPP slowdown of v1 loop trees) was removed from this task by the owner on 2026-11-18; the
performance check and any fix become a separate task.

## Objective

Act on the independent code review of P10-013 (`1dd7dd0..3e4b09e`): fix the native defects D1, D2
and D3, assess F-S2-2 and the review risks, add the missing native coverage and correct the
documentation. (Finding the cause of the ≈ +5 µs per `Step` slowdown of v1 loop trees measured in
IL2CPP after P10-013 was part C, removed by the owner on 2026-11-18.)

## Decision record

Owner's decisions 2026-11-18: fix D1, D2 and D3, which may change native v1 behaviour where it is a
bug that diverges from the reference executor and the normative specifications; add the missing
tests; fix the docs; part C removed later the same day. No ADR: no normative choice is made; every
behaviour change aligns native with the reference executor, except the open question about a tree
stop that replaces an abort in progress (evidence §D1).

Owner's decision 2026-11-19: the F-S2-2 fix (full hot-reload restart of a v1 reactive composite
with a running branch), made in this task without prior approval, stays.

## Depends on

- P10-013 (stages 1–3 committed, `3e4b09e`).
- The review report (session notes, `P10-013-code-review.md`, summarised in the evidence).

## Scope

- Part A: `Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs`,
  `Runtime/State/Native/NativeInstanceArenaContracts.cs` (internal fields of
  `NativeParallelBranchStateV1`), `Runtime/Execution/Native/HotReload/NativeHotReloadFullRestart.cs`,
  `Runtime/Execution/Native/HotReload/NativeHotReloadStateMigration.cs`; native tests.
- Part B: native and simulation tests; `Documentation~/simulation-container.md`, the P10-013 ADR
  (status lines, non-normative notes).
- Part C: removed by the owner on 2026-11-18 (no Player builds, no measurements in this task).
- Evidence `Planning~/Evidence/P10-014/README.md`, `CHANGELOG.md`, this card, `work-items.json`.

## Forbidden changes

- The meaning of the normative specifications; the reference executor's behaviour;
  `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`; the compiled format version; the trace
  format; the package version; public API other than additive; weakening an existing test
  expectation (a native expectation that encoded D1/D2 may be corrected only where reference and
  spec agree, listed in the evidence); Player builds and measurements; pushing; committing the
  parent repository.

## Acceptance criteria

- Each defect reproduced by a test that failed before the fix (failing output in the evidence),
  passing after it; a finding that does not reproduce is reported with the test that shows the code
  is right.
- D2: one restore for every program; where the old copy was correct the active and suspended frames
  are identical (algorithm test plus the unchanged Parallel suites).
- Part B tests and doc corrections as listed in the evidence.
- `Tools~/Verification/Verify-Static.ps1` passes; `ExpectedPublicAbiV2.txt` byte-identical; local
  commits only, and only of changes whose tests ran.
