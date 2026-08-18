# P3-008 — Validation UX

Status: `Draft`

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
