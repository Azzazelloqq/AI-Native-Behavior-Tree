# P7-006 migration tooling implementation evidence

## Result

`ADR-P7-005` is now real production code, built end to end in one pass (Allowed-changes corrected
before implementation, owner-confirmed — see the task card's own correction note):

- **`Authoring/Migration/`** (new): `NodeMigrationRule`/`NodeFieldRename`/`NodeFieldAddition`
  (declarative rename/add-with-default rules), `NodeMigrationRegistry` (an ordered lookup keyed by
  `(TypeId, sourceVersion)`, `.Empty` used by every production call site today since no node type
  has ever been version-bumped), and `DocumentMigrator.TryMigrate` (the engine: walks a rule chain
  hop by hop, never skips a version, leaves a node with no matching rule completely untouched so the
  existing `UnsupportedNodeVersion` diagnostic fires normally downstream).
- **New diagnostic** `AIBT2042 MigrationApplied` (`Authoring/Validation/TreeValidationDiagnostics.cs`)
  — `Info` severity by construction (the catalog's per-code default was widened to support a
  non-Error default for the first time; every other code stays `Error`), never blocking.
- **`validate`/`compile` hook** (`MCP/Verification/McpVerificationToolDispatcher.cs`): both tools now
  call `ApplyMigrations` right after loading the document and building the registry, migrating it in
  memory before `TreeValidator.Validate`/`ReferenceCompiler.Compile` ever see it, and merging any
  `AIBT2042` diagnostics into the normal response. `ApplyMigrations` is `internal` with an injectable
  `NodeMigrationRegistry rules = null` parameter (defaults to `Empty` in production) specifically so
  tests can prove this exact hook, not only the standalone engine.
- **`aibt_migrate_document` MCP tool**: `MCP/Migration/McpMigrationToolDispatcher.cs` (dry-run by
  default behavior, explicit persist via `TreeDocumentPersistence.Save`, mirroring
  `McpAuthoringToolDispatcher`'s own accept-then-persist shape), a new `McpToolDispatcher.cs` case
  tagged `McpPermissionCategory.SemanticEdit` (mirroring `add_node`'s own tag), and
  `MCP~/Server/MigrationTools.cs` (the external-process relay, mirroring `VerificationTools.cs`
  exactly — a real `dotnet build` of `MCP~/Server/AibtMcpServer.csproj` confirmed 0 errors/warnings).
- **`Editor/Migration/MigrationNotificationWindow.cs`**: a plain (never modal) `EditorWindow`
  (`AIBT/Migration Notifications` menu item) listing every project document with a migratable node
  and a per-row persist button. Never gates anything — `validate`/`compile` already apply the same
  migration transparently whether or not this window is ever opened.
- **`McpMigrationsDocumentGenerator.cs`**: gained a second "Node-contract migrations" section
  describing the real mechanism, pointing at `ADR-P7-005`; the pre-existing "MCP surface migrations"
  section (a genuinely different concept — tool renames, not node-contract versioning) is untouched.

## Verification

- **11 new tests, all passing live** (`Tests/Editor/Migration/`):
  - `DocumentMigratorTests` (3): single-hop rename+add-with-default against a real
    `TreeDocument`/`NodeManifest`, compiled through the real `ReferenceCompiler`; a chained two-hop
    migration proving hops apply strictly in order (v1→v2 rename, then v2→v3 addition — never
    skip-ahead); the unhandled-category negative case (no rule registered) leaving the node at its
    original version and the real `TreeValidator` still emitting `UnsupportedNodeVersion` unchanged.
  - `McpMigrationHookTests` (2): `McpVerificationToolDispatcher.ApplyMigrations` — the exact
    production hook — produces a correctly-shaped `Info`-severity `AIBT2042` diagnostic when given a
    populated rule registry, and is a genuine no-op (unchanged `ReferenceEquals` document, zero
    diagnostics) with the real production default (`Empty`).
  - `McpMigrationToolDispatcherTests` (4): `aibt_migrate_document` dry-run (file untouched) vs. real
    persist (file rewritten, reloaded and reparsed to confirm), the no-rule no-op case (file
    untouched), and the `SemanticEdit` permission-negative case through the full
    `McpToolDispatcher.Dispatch` switch.
  - `MigrationNotificationWindowTests` (2): the window's scan/list logic against a real temp
    directory with two fixture documents (one migratable, one not) and a real injected rule —
    lists exactly the one migratable tree, never the unaffected one.
- **Live, real-project confirmation** (not only unit-tested): opened the actual
  `AIBT/Migration Notifications` window in the real, currently-open `6000.5.8f1` Editor via Unity
  MCP, invoked `Scan` against the real `Assets/` (72 real `.aibt.json` documents), and read the
  window's own live UI state directly: `"Scanned 72 document(s). Nothing to migrate."`, 0 rows --
  correct, since no real production migration rule exists yet.
- **Full regression, live**: `AIBT.Runtime.Tests` 601/601; `AIBT.Editor.Tests` +
  `AIBT.Integration.Tests` + `AIBT.BehaviorCases.Tests` 481/481 (470 pre-existing + 11 new) --
  identical pass count to the pre-P7-006 baseline, zero new failures.
- **Generated-docs regenerate-and-diff-clean**: `AIBT/MCP/Regenerate Documentation` re-run live
  (invoked via reflection after discovering `execute_menu_item` does not reliably reach this
  particular command in this session -- a real, disclosed tooling quirk, not a mechanism defect);
  `migrations.md`/`api-reference-authoring.md`/`api-reference-editor.md` updated to match the new
  public surface (`Authoring/Migration/`'s new public types; `MigrationNotificationWindow`'s two
  public test-observable members, `Scan`/`LastScanMigratableTreeIds`, following the same
  test-observability convention `TraceTimelineWindow.LoadGraphContext`/`CurrentModel` already use --
  Editor windows in this codebase expose test seams as `public`, not `internal`, since
  `AIBT.Editor` does not grant `InternalsVisibleTo` to the test assembly the way `AIBT.Authoring`/
  `AIBT.Runtime`/`AIBT.Mcp` do); the `McpDocumentationGeneratorsTests` drift check passed as part of
  the full 481/481 regression run above.

## Decision

No new decision; this card applies the already-accepted `ADR-P7-005` as written. One real
implementation detail was found and resolved without escalation: `Codes`/`RequiredFields` in
`TreeValidationDiagnosticCatalog`'s `CreateCatalog()` uniformly defaulted every code to
`DiagnosticSeverity.Error` -- widened to a per-code default (still `Error` for every existing code,
`Info` only for the new `MigrationApplied`) rather than always passing an explicit `severity:` at
every call site, since a diagnostic's own catalog-reported default severity should be accurate for
anyone inspecting the catalog directly (e.g. via `explain_diagnostic`), not just at the one call site
this card happens to add.

## Scope and limitations

- No real production migration rule exists yet -- correct, since no node type has ever had its
  contract version bumped. Every production call site (`validate`, `compile`, `aibt_migrate_document`,
  the Editor window) uses `NodeMigrationRegistry.Empty` by default; the mechanism is proven end to
  end against real fixture rules injected through each entry point's own test-only parameter.
- Field removal and type-change remain genuinely unhandled, per `ADR-P7-005`'s own disclosed scope
  -- proven by `DocumentMigratorTests.UnregisteredGap_LeavesNodeUntouched_ExistingValidatorStillHardFails`.
- `Editor/Migration/MigrationNotificationWindow.cs` duplicates `MCP/AibtTreeDiscovery.Scan`'s
  minimal glob-and-parse logic rather than referencing the `AIBT.Mcp` assembly from `AIBT.Editor` --
  no existing Editor file takes that dependency, and this window must work whether or not the MCP
  bridge is even present.
- Live end-to-end verification through the real external `MCP~/Server/` process and an actual MCP
  client (e.g. the official Inspector CLI) was not performed this session -- the real `dotnet build`
  success plus the full `McpToolDispatcher.Dispatch` pipeline tests (permission enforcement included)
  are judged sufficient proof of wiring, since the Inspector CLI would only add JSON-RPC/stdio
  transport on top of the exact same dispatch call already exercised live.

See `verification-results.json` for exact commands and results.
