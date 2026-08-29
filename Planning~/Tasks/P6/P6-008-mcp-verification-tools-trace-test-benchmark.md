# P6-008 — MCP verification tools: test, benchmark

Status: `Draft`

## Scope correction (2026-08-29)

This card originally covered four tools: `trace`, `compare-trace`, `run-tests`, `run-benchmark`.
Investigation before implementation found the `trace`/`compare-trace` half's own premise false: no
production code anywhere wires a real running native tree's lifecycle steps into a
`NativeTraceChannelOwnerV1` (the only two things that ever write trace records are synthetic test
fixtures, never derived from an actual compiled tree's execution) — this is exactly the gap
`Planning~/Evidence/P3-010/README.md` already disclosed and deliberately left open. Building that
wiring is genuine new engineering, not "wrapping an accepted entry point," so per
`DECISION_BOUNDARIES.md` it was spun off into its own decision card, `P6-015`, rather than built
silently here. This card is narrowed to `run-tests` + `run-benchmark` only; `P6-011` should link to
`P6-015` for the trace tool's eventual documentation once that card and its follow-up implementation
land.

`run-tests`/`run-benchmark` also do not literally wrap "already-accepted production entry points"
as originally stated — `BehaviorCaseRunner` (`P1-017`) and `SchedulingPolicyDriver` (`P4-001`) both
live in Editor-only Tests assemblies `AIBT.Mcp` cannot reference without violating
`architecture.md`'s dependency direction. Resolved by promoting the genuinely reusable,
test-framework-free logic into the production layers `AIBT.Mcp` already sits on
(`AIBT.Authoring`/`AIBT.Runtime`), leaving the Tests assemblies as thin callers — see
`Planning~/Evidence/P6-008/README.md` for the full file-by-file account.

## Objective

Expose focused test execution and approved-benchmark-scenario execution from
`Documentation~/ai-and-mcp.md`'s "Core MCP surface > Verification" group over MCP, driving the same
observable logic Phase 1's behavior-case runner and Phase 4's scheduling policy driver already use
(promoted to a production layer, not reimplemented) — no new test or benchmark logic.

## Depends on

- `P6-005` (MCP server host and permission enforcement).

## Required reading

- `Documentation~/ai-and-mcp.md`'s "Core MCP surface > Verification" section.
- `Authoring/BehaviorCases/BehaviorCaseRunner.cs` (promoted from `P1-017`'s original
  `Tests/BehaviorCases/Framework/Runner/BehaviorCaseRunner.cs` by this card) and
  `Authoring/BehaviorCases/ReferenceBehaviorCaseExecutorFactory.cs` (promoted from `Tests/
  Integration/SemanticSlice/ReferenceBehaviorCaseAdapter.cs`) — the only test-execution entry
  points this card may call for "run focused tests."
- `Runtime/Scheduling/SchedulingPolicyDriver.cs` and `Authoring/Benchmarking/
  SchedulingScenarios.cs` (promoted from `P4-001`'s original `Tests/Runtime/Benchmarking/
  SchedulingPolicyDriver.cs` and `Benchmarks~/Phase4/Scheduling/Unity/SchedulingScenarios.cs` by
  this card) and `Planning~/Evidence/P4-001/` — the approved benchmark scenario catalog; only the
  6 scenarios `P4-001` actually implemented end-to-end are callable, the 8 documented placeholders
  remain refused, never silently run as something else.
- `Planning~/USER_ACTIONS.md` — no performance threshold, default, or supported-hardware-class
  claim may be introduced by this card; a benchmark tool returns raw measured numbers only.
- `Documentation~/architecture.md`'s "Dependency direction" — grounds why the promotion above is
  the correct fix rather than a new `AIBT.Mcp` reference to a Tests assembly.

## Allowed changes

- `Authoring/BehaviorCases/` (new — promoted from `Tests/BehaviorCases/Framework/` and `Tests/
  Integration/SemanticSlice/ReferenceBehaviorCaseAdapter.cs`, logic unchanged).
- `Runtime/Scheduling/SchedulingPolicyDriver.cs` (new — promoted from `Tests/Runtime/
  Benchmarking/SchedulingPolicyDriver.cs`, logic unchanged) and `Authoring/Benchmarking/
  SchedulingScenarios.cs` (new — promoted from `Benchmarks~/Phase4/Scheduling/Unity/
  SchedulingScenarios.cs`, logic unchanged).
- The Tests/Benchmarks files left behind by the above promotions, updated only to reference the
  new locations (`using` changes, one path string in `Run-SchedulingBenchmark.ps1`) — no behavior
  change to any of them.
- The MCP assembly's test/benchmark tool module (location per `P6-001`'s ADR).
- `Tests/Editor/Mcp/Testing/` (new, sibling to `P6-007`'s `Tests/Editor/Mcp/Verification/`).
- `Planning~/Evidence/P6-008/`.

## Forbidden changes

- A second test/benchmark implementation — every tool calls the one promoted `BehaviorCaseRunner`/
  `SchedulingPolicyDriver` entry point, never a reimplementation.
- Any performance threshold, regression gate, or "acceptable cost" default.
- Running an unapproved (placeholder) `P4-001` benchmark scenario, or fabricating a result for one.
- Widening any promoted type's accessibility beyond `internal` (the existing `InternalsVisibleTo(
  "AIBT.Mcp")` grants on `AIBT.Authoring`/`AIBT.Runtime` already cover this card's needs; no new
  public API surface).
- Re-running the isolated Player benchmark harness (`Run-SchedulingBenchmark.ps1`) end-to-end as
  part of this card's own verification — its one-line path update is verified by inspection and
  the in-project `SchedulingPolicyDriverTests` regression only; re-running the full Player harness
  is out of proportion for this card and not required by its acceptance criteria.

## Deliverables

- A `run-tests` tool executing a named behavior case through the promoted headless runner and
  returning pass/fail plus diagnostics per case.
- A `run-benchmark` tool executing one of the 6 approved `P4-001` scenarios and returning raw
  measured numbers with environment metadata, refusing any other scenario name with a structured
  diagnostic.

## Acceptance criteria

- A run-tests call for a known-failing fixture returns the same failure the promoted runner
  reports directly.
- A run-benchmark call for a placeholder scenario name is rejected with a structured diagnostic
  naming it as not-yet-implemented, never silently substituted with an implemented one.
- No new numeric threshold, default, or claim appears anywhere in this card's deliverables or
  evidence.
- The promotion in "Allowed changes" leaves every existing consumer (`AIBT.BehaviorCases.Tests`,
  `AIBT.Integration.Tests`, `AIBT.Runtime.Tests`) passing unchanged.

## Required verification

```text
real MCP client: run-tests/run-benchmark calls, parity against direct BehaviorCaseRunner/
  SchedulingPolicyDriver calls
placeholder-scenario refusal proof
regression: AIBT.BehaviorCases.Tests, AIBT.Integration.Tests, AIBT.Runtime.Tests, AIBT.Tests.
  Editor.Mcp.* -- unchanged pass
Verify-Static.ps1
```

## Handoff notes

- `P6-011` (generated agent documentation) documents `run-tests`/`run-benchmark` from this card;
  it should note `trace`/`compare-trace` as pending `P6-015`'s decision and follow-up
  implementation, not yet part of the MCP surface.
