# P7-006 — Migration tooling implementation

Status: `Draft`

## Objective

Build the migration mechanism `P7-005`'s ADR decides, replacing or extending
`McpMigrationsDocumentGenerator`'s current stub with the real thing.

## Depends on

- `P7-005` (the accepted design decision this card implements).

## Required reading

- The accepted `Documentation~/decisions/ADR-P7-005-*.md`.
- `Planning~/Evidence/P7-005/` (the spike this card's production implementation must match, not
  diverge from without a new escalation).

## Allowed changes

- The production location `P7-005`'s ADR names (expected `Authoring/Migration/` or `MCP/Migration/`
  depending on the decided mechanism — confirmed by the ADR, not assumed).
- `MCP/Documentation/McpMigrationsDocumentGenerator.cs`, if the decided mechanism changes what the
  generated migrations document should say.
- `Tests/Editor/Migration/` (new).
- `Planning~/Evidence/P7-006/`.

## Forbidden changes

- Any change to `P7-005`'s own decided scope boundary (e.g. adding automatic rewriting if the ADR
  decided a diagnostic-driven manual workflow instead) — a scope change here is a new escalation,
  not a local judgment call.
- Weakening any existing compiler/validator diagnostic to make a migration path falsely appear
  successful.

## Deliverables

- The real migration mechanism, built and tested against at least the version-change categories
  `P7-005`'s ADR states it handles.
- `McpMigrationsDocumentGenerator`'s own generated content updated to describe the real mechanism,
  not the placeholder stub language, with a regenerate-and-diff-clean proof mirroring `P6-011`'s own
  drift-check discipline.

## Acceptance criteria

- Every version-change category the ADR claims is handled has a real, passing test proving it
  against an authored document, not a synthetic in-memory object.
- A version-change category the ADR explicitly excludes is proven to fail with a clear, structured
  diagnostic rather than silently producing a corrupted document.
- Regression: the full existing test suite passes unchanged.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
migration mechanism run against every version-change category the ADR names as handled
generated migrations.md regenerate-and-diff-clean check
```

## Handoff notes

- None; this closes `P7-005`'s own deferred implementation.
