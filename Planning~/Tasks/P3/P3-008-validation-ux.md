# P3-008 — Validation UX

Status: `Done`

## Objective

Surface `AuthoringDiagnostic`/validation results inline in the graph editor at the node or connection they concern.

## Depends on

- `P3-006`.

## Required reading

- `Documentation~/specifications/diagnostics-v1.md`.

## Allowed changes

- `Assets/AIBT/Editor/Validation/` (new).
- `Tests/Editor/Validation/` fixtures.
- `Planning~/Evidence/P3-008/`.

## Forbidden changes

- A separate, editor-local validation implementation; this card only presents diagnostics already produced by the existing `Authoring` validation pipeline (the same one `P3-006` routes edits through).

## Deliverables

- Inline diagnostic markers on the node/connection/field a structured diagnostic targets.
- A document-level diagnostic summary (count by severity, jump-to-node navigation).

## Acceptance criteria

- Every diagnostic code the `Authoring` validation pipeline can produce renders with a stable location in the graph, not just a raw code/message dump.
- Fixing the underlying issue clears the corresponding marker without requiring a manual refresh action.
- A tree with zero diagnostics shows no markers (no false positives from the presentation layer itself).

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <validation UX fixture>
```

## Handoff notes

- None beyond the dependency on `P3-006`'s edit path already surfacing diagnostics consistently.

## Outcome

- `Editor/Validation/DiagnosticGraphLocation.cs` classifies any `Diagnostic` into
  Document/Node/Field purely from its own `Location` (no tree parsing needed);
  `DiagnosticGraphSummary.cs` builds per-severity counts + markers + a jump-to-node list, always
  fresh from the diagnostics passed in.
- 3/3 tests passing, including a `Field`-level `ParameterType` resolution and proof that fixing an
  issue clears its marker with no manual refresh (there is no cache to invalidate).
- **No `Editor/Graph/` UI wiring** — outside this card's `Allowed changes`, same pattern as
  `P3-004` through `P3-007`.
- Full evidence: `Planning~/Evidence/P3-008/README.md`, `verification-results.json`.
