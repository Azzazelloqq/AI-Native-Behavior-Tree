# P6-008 — MCP verification tools: trace, test, benchmark

Status: `Draft`

## Objective

Expose runtime-trace inspection/comparison, focused test execution, and
approved-benchmark-scenario execution from `Documentation~/ai-and-mcp.md`'s
"Core MCP surface > Verification" group over MCP, wrapping only
already-accepted production entry points from Phase 3/4 — no new trace,
test, or benchmark logic.

## Depends on

- `P6-005` (MCP server host and permission enforcement).

## Required reading

- `Documentation~/ai-and-mcp.md`'s "Core MCP surface > Verification"
  section.
- `Editor/Debugger/NativeExecutionDebuggerSession.cs` (`P3-010`) and
  `Editor/Trace/TraceTimelineModel.cs` (`P3-011`) — the only trace entry
  points this card may call. Both are scoped to self-driven sessions; no
  production Play-mode host exists (`Planning~/Evidence/P5-GATE/
  known-limitations.md`), so a trace tool must not claim it can attach to
  an arbitrary running game.
- The Phase 1 behavior-case runner (`P1-017`) — the only test-execution
  entry point this card may call for "run focused tests."
- `Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs` and
  `Planning~/Evidence/P4-001/` — the approved benchmark scenario catalog;
  only the 6 scenarios `P4-001` actually implemented end-to-end are
  callable, the 8 documented placeholders remain refused, never silently
  run as something else.
- `Planning~/USER_ACTIONS.md` — no performance threshold, default, or
  supported-hardware-class claim may be introduced by this card; a
  benchmark tool returns raw measured numbers only.

## Allowed changes

- The MCP assembly's trace/test/benchmark tool module (location per
  `P6-001`'s ADR — expected to reference `AIBT.Editor` for the trace tool,
  per `architecture.md`'s diagram placing `MCP` beside `Editor`, confirmed
  by `P6-001`'s ADR).
- `Tests/Editor/Mcp/Verification/` (extends `P6-007`'s test location) or
  the equivalent location `P6-001`'s ADR names.
- `Planning~/Evidence/P6-008/`.

## Forbidden changes

- A second trace/debugger/benchmark implementation — every tool calls the
  one accepted Phase 3/4 entry point.
- Any performance threshold, regression gate, or "acceptable cost" default.
- Running an unapproved (placeholder) `P4-001` benchmark scenario, or
  fabricating a result for one.
- Claiming Play-mode attach capability that does not exist.

## Deliverables

- A `trace` tool returning a per-step active-node history and diagnostics
  from a self-driven session, explicitly labeled as self-driven (not
  attached to an external running game).
- A `compare-trace` tool diffing two trace captures (e.g. before/after a
  domain patch), reusing `TraceTimelineModel`'s own comparison logic if
  present, or a thin wrapper if not.
- A `run-tests` tool executing a named subset of behavior cases through the
  existing headless runner and returning pass/fail plus diagnostics per
  case.
- A `run-benchmark` tool executing one of the 6 approved `P4-001` scenarios
  and returning raw measured numbers with environment metadata, refusing
  any other scenario name with a structured diagnostic.

## Acceptance criteria

- A trace call over MCP reproduces the same per-step history a direct
  `TraceTimelineModel` replay produces for the same session.
- A run-tests call for a known-failing fixture returns the same failure
  the headless runner reports directly.
- A run-benchmark call for a placeholder scenario name is rejected with a
  structured diagnostic naming it as not-yet-implemented, never silently
  substituted with an implemented one.
- No new numeric threshold, default, or claim appears anywhere in this
  card's deliverables or evidence.

## Required verification

```text
real MCP client: trace/compare-trace/run-tests/run-benchmark calls, parity
  against direct NativeExecutionDebuggerSession/TraceTimelineModel/behavior-
  case-runner/SchedulingPolicyDriver calls
placeholder-scenario refusal proof
Verify-Static.ps1
```

## Handoff notes

- `P6-011` (generated agent documentation) must state the trace tool's
  self-driven-only scope plainly wherever it documents this tool.
