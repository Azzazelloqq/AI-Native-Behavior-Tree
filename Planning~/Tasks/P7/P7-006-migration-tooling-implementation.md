# P7-006 — Migration tooling implementation

Status: `Done`

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

**Correction (2026-09-02, before implementation started, owner-confirmed):** this list was written
during the original Phase 7 decomposition, before `ADR-P7-005` existed. The accepted ADR's own
Consequences section is broader than what was listed here — it explicitly assigns `P7-006` the
persist-to-disk MCP tool and the Editor notification surface, neither named below originally. Rather
than silently narrowing the card to only what was pre-written, or silently expanding it without
asking, this was raised to the owner directly; the owner chose to widen this list now to match the
ADR and build everything in one pass. The list below is the corrected, authoritative scope.

- `Authoring/Migration/` (new) — the migration-rule registry and engine `ADR-P7-005` decides.
- `Authoring/Validation/TreeValidationDiagnostics.cs` — the new `Info`-severity migration-applied
  diagnostic code (proposed `AIBT2042`, confirmed against the catalog's real next-free code at
  implementation time, not assumed).
- Wherever `MCP/Verification/McpVerificationToolDispatcher.cs`'s `validate`/`compile` tools load a
  document (`LoadTreeOrThrow`) — the in-memory migration hook, applied before validation/compilation
  ever sees the document, per the ADR's own "in-memory only, both the Editor and MCP paths" decision.
- `MCP/` — a new `aibt_migrate_document` tool: `MCP/McpToolDispatcher.cs` (dispatcher case,
  `McpPermissionCategory.SemanticEdit`, mirroring `add_node`'s own tag since this tool writes to
  disk), a new dispatcher method (mirroring `MCP/Authoring/McpAuthoringToolDispatcher.cs`'s own
  accept-then-explicitly-persist shape), and `MCP~/Server/` (the external-process relay method,
  mirroring `MCP~/Server/VerificationTools.cs`'s existing `aibt_validate`/`aibt_compile` pattern).
- `Editor/Migration/` (new) — the non-blocking Editor notification window listing documents with a
  migratable node and a persist action, mirroring `Editor/HotReload/HotReloadWorkflowWindow.cs`'s own
  UI shape (toolbar + scroll list) as the closest existing precedent, never gating the MCP path.
- `MCP/Documentation/McpMigrationsDocumentGenerator.cs`, updated to describe the real mechanism.
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

## Outcome

Done. `Authoring/Migration/` (new) implements the ADR's declarative rename/add-with-default rule
engine (`NodeMigrationRule`/`NodeMigrationRegistry`/`DocumentMigrator`); a new `AIBT2042
MigrationApplied` (Info) diagnostic is hooked into `McpVerificationToolDispatcher.Validate`/`Compile`
right after document load, migrating in memory before validation/compilation ever runs; a new
`aibt_migrate_document` MCP tool (`MCP/Migration/`, `McpToolDispatcher.cs` case tagged
`SemanticEdit`, `MCP~/Server/MigrationTools.cs` relay, real `dotnet build` confirmed) persists that
same migration to disk on explicit request (dry-run by default); a new non-blocking
`Editor/Migration/MigrationNotificationWindow.cs` lists migratable documents in the real Editor,
verified live scanning the real project (72 real documents, correctly "Nothing to migrate" since no
production rule exists yet); `McpMigrationsDocumentGenerator.cs` gained a real "Node-contract
migrations" section alongside its pre-existing, unrelated "MCP surface migrations" one. 11 new tests
pass live, full regression (`AIBT.Runtime.Tests` 601/601, `AIBT.Editor.Tests`+`AIBT.Integration.Tests`+
`AIBT.BehaviorCases.Tests` 481/481) shows zero new failures against the pre-P7-006 baseline. No scope
change from the ADR; one real implementation detail resolved locally (the diagnostic catalog's
per-code default severity was widened from a single hardcoded `Error` for every code to a per-code
default, so `explain_diagnostic` reports `MigrationApplied`'s true default correctly). See
`Planning~/Evidence/P7-006/README.md`.
