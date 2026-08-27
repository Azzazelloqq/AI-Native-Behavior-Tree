# P6-007 — MCP verification tools: validate, compile, simulate, explain

Status: `Draft`

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
