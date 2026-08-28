# P6-007 — MCP verification tools: validate, compile, simulate, explain

Status: `Done`

## Objective

Expose validation, compilation, behavior-case simulation, and
diagnostic-explanation from `Documentation~/ai-and-mcp.md`'s "Core MCP
surface > Verification" group over MCP, wrapping only already-accepted
production entry points (`TreeValidator`, `ReferenceCompiler`,
`ReferencePreviewDriver`) — this card adds no new validation or execution
logic of its own.

## Depends on

- `P6-005` (MCP server host and permission enforcement).

## Required reading

- `Documentation~/ai-and-mcp.md`'s "Core MCP surface > Verification" and
  "Structured diagnostics" sections.
- `Authoring/Validation/TreeValidator.cs` and `Authoring/Compilation/`
  (`ReferenceCompiler`) — the only validate/compile entry points this card
  may call.
- `Authoring/Execution/ReferencePreviewDriver.cs` (`P3-009`'s public
  `AIBT.Authoring` facade over `ReferenceExecutionMachine`) — the only
  simulation entry point this card may call.
- `Documentation~/specifications/diagnostics-v1.md` (exact diagnostic JSON
  shape a `validate`/`compile` tool must return, unmodified).
- `Documentation~/specifications/behavior-case-v1.md` (the input shape a
  `simulate` tool accepts).

## Allowed changes

- The MCP assembly's verification-tool module (location per `P6-001`'s
  ADR), scoped to validate/compile/simulate/explain-diagnostics only.
- `Tests/Editor/Mcp/Verification/` (new) or the equivalent test location.
- `Planning~/Evidence/P6-007/`.

## Forbidden changes

- Any second validator, compiler, or executor — every tool in this card
  calls the one accepted production entry point.
- Any claim that simulation exercises anything beyond the reference
  executor and its Phase 1 fixture/built-in node set, per
  `Planning~/Evidence/P5-GATE/known-limitations.md`; a `simulate` tool
  response must state which backend and node set it used.
- Trace/test/benchmark tools (`P6-008`'s job).

## Deliverables

- A `validate` tool returning the exact structured-diagnostic JSON shape
  from `diagnostics-v1.md`, including project-policy diagnostics.
- A `compile` tool returning success/failure plus diagnostics, never a
  bare boolean.
- A `simulate` tool running one behavior case through
  `ReferencePreviewDriver` and returning step-by-step status/trace
  summary, explicitly labeled with backend and node-set scope.
- An `explain-diagnostic` tool that, given a diagnostic code, returns its
  stable meaning and any machine-applicable suggested operation already
  present on the diagnostic (never fabricating one that was not in the
  original record).

## Acceptance criteria

- A validate call on a known-invalid fixture document returns the exact
  same diagnostics `TreeValidator` produces directly, byte-for-byte in
  meaning.
- A compile call on a valid document returns the compiled program's
  identity/content hash, matching a direct `ReferenceCompiler` call.
- A simulate call on a known behavior case reproduces the same status
  sequence the existing headless behavior-case runner already asserts for
  that case.
- An explain-diagnostic call for a code with no suggested operation returns
  none, never an invented one.

## Required verification

```text
real MCP client: validate/compile/simulate/explain-diagnostic calls, parity
  against direct TreeValidator/ReferenceCompiler/ReferencePreviewDriver calls
Verify-Static.ps1
```

## Handoff notes

- `P6-008` continues this group with trace/test/benchmark; keep the
  diagnostic-JSON and backend-disclosure conventions identical across both
  cards.

## Outcome

Done — see `Planning~/Evidence/P6-007/` for the full account. Summary:

- All 4 Verification tools implemented in `MCP/Verification/`, each wrapping exactly one
  already-accepted entry point (`TreeValidator`, `ReferenceCompiler`, `ReferencePreviewDriver`),
  wired through `McpToolDispatcher.cs` (4 new permission-tagged cases: `validate`/`compile` ->
  `Compilation`, `simulate` -> `TestExecution`, `explain_diagnostic` -> `Read`) and relayed by 4
  new thin server methods in `MCP~/Server/VerificationTools.cs`.
- Five real gaps found and resolved/disclosed before or while implementing (all detailed in the
  evidence): only 2 of ~12 `DiagnosticCatalog`s in the whole codebase are reachable from a new
  `AIBT.Mcp` module (no `InternalsVisibleTo` grant exists for it anywhere) — `explain-diagnostic`
  honestly reports `catalogReachable: false` for everything else rather than fabricating or
  silently widening assembly visibility; `ReferencePreviewDriver` has no event/completion/
  resume/abort/step-budget injection API and no caller control over
  `updateId`/`snapshotRevision`/`treeInstanceId`/`rootSeed` — `simulate` is scoped to plain
  `update` steps only, validated against the driver's own sequential assignment; no existing real
  `*.aibtcase.json` fixture is actually drivable through `ReferencePreviewDriver` (one references
  a tree file that doesn't exist anywhere, the other uses node types outside the Phase 1 fixture
  set) — substituted with `P3-009`'s own proven `success-then-running.aibt.json` fixture, disclosed
  as a deliberate substitution; `TreeValidator.Validate` and `.aibt/policy.json`'s own
  `ProjectPolicySnapshot` are two unrelated types with no existing conversion — mapped
  field-by-field, confirmed against `policy.schema.json`'s own declared enums; `P6-006`'s existing
  diagnostic JSON is non-canonical — this card's tools use the real
  `AIBT.Authoring.DiagnosticJson.Serialize` instead (P6-006's files untouched, the inconsistency
  disclosed as follow-up work).
- Verified: 14 new EditMode tests (including a byte-for-byte diagnostic parity proof against a
  direct `TreeValidator` call, a content-hash parity proof against a direct `ReferenceCompiler`
  call, and a simulate trace proof matching `P3-009`'s own established oracle behavior), a 62/62
  regression re-run with no regressions, and full live verification via the official Inspector CLI
  against the real permanent server and real Unity bridge (validate/compile on real trees,
  simulate against the real project's own `tree.test.preview-success-then-running` fixture,
  explain-diagnostic, and a permission-negative check) — all fixture files cleaned up afterward.
  `Verify-Static.ps1` and `git diff --check` both pass.
