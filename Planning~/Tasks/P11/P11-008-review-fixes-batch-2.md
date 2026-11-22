# P11-008 — Review fixes, batch 2 (stale pin, F8, F9, F10, F11, F13 ADR)

Status: `in_progress` — the fixes are committed on `feature/aibt-consumer-workflow`; F9 and the native
half of F10 are stopped for the owner (see the evidence). The orchestrator runs the full test run and
moves the item to `Review`.

## Objective

Fix the second batch of findings of the P11 production-readiness review
(`Planning~/Evidence/P11-REVIEW/README.md`), approved by the owner on 2026-11-22 ("Да, пакет 2"):

- Refresh the stale compiled pin `Tests/Fixtures/P2/CodeGen/generated-compiled-v2.json` (stale since
  `5c4bdfe`, approved P10-013 registry-hash change): `contentHash` and `header.nodeRegistryHash` only.
- F8: `ProductionTreeHost.TryResolveExternalTreeRead<T>` must refuse a `T` that is not the managed type
  of `expectedType`; signature unchanged; the read after resolve stays allocation-free.
- F9: a native tree with an Agent-scope key ends in the internal error AIBT3017; a clear refusal with its
  own diagnostic at the earliest stage, unless the case is claimed as supported and the cause is a small
  bug (then stop and report).
- F10: observers: explicit refusal in the managed agent and on native hosts at load, validation or
  compile time, and a note in the specification of where observers are implemented; stop if a refusal
  would break an existing passing test or a shipped sample.
- F11: AIBT2042 and AIBT2043 each have two meanings; the later meaning by git history gets a new free
  number; every place that names the codes is updated; a test that no code has two meanings.
- F13: an ADR only (status Proposed) for reproducible random streams; no production change.

For each code finding: a test that fails before the fix, the fix, the test passing, one commit.

## Depends on

- `P11-007` (batch 1).

## Scope

- Pin: `Tests/Fixtures/P2/CodeGen/generated-compiled-v2.json` (named by the brief; nothing else under
  `Tests/Fixtures`).
- F8: `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs`, the doc comment of
  `ProductionTreeHost.TryResolveExternalTreeRead<T>`.
- F10: `Agents/Runtime/ManagedTreeProgram.cs`, `Editor/Registry/ProjectNodeCatalog.cs`,
  `Editor/Graph/TreeCatalogEntry.cs`, a new code in `Authoring/Validation/TreeValidationDiagnostics.cs`,
  the note in `Documentation~/specifications/execution-semantics-v1.md`.
- F11: the two validator codes and every place naming them: sources, tests, documentation (generated
  pages through their generators), the code reference in `Documentation~/specifications/blackboard-v1.md`,
  notes in three ADRs, `CHANGELOG.md`.
- F13: `Documentation~/decisions/ADR-P11-008-reproducible-random-streams.md`.
- Tests under `Tests/Integration/NativeRuntime/`, `Tests/Editor/Agents/`, `Tests/Editor/Diagnostics/`,
  `Tests/Editor/Validation/`; `README.md`.

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format, `CodeGen~`,
  execution semantics, `package.json`, existing public APIs (only additions), other behaviour of
  `ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`, normative specifications beyond
  the F10 note and the F11 code references, files under `Tests/Fixtures` beyond the pin.

## Acceptance criteria

- Each finding fixed or stopped with a reason, in its own commit with its test.
- The affected tests green, the Console free of new errors, the submodule checkout clean.
- `Planning~/Evidence/P11-008/README.md` with the proof per finding.
- `Tools~/Verification/Verify-Static.ps1` passes.

## Evidence

`Planning~/Evidence/P11-008/README.md`; raw files in `C:\UnityProjects\_aibt_acceptance\P11-008\`.
