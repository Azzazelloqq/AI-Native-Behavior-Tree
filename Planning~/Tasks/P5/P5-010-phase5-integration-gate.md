# P5-010 — Phase 5 integration gate

Status: `Draft`

## Objective

Verify the complete hot-reload output, evidence boundaries, and repository
hygiene from a clean committed snapshot, mirroring `P2-025`/`P3-013`/`P4-009`'s
shape.

## Depends on

- `P5-001` through `P5-009`.

## Required reading

- Every P5 card and its accepted decisions, including `P5-001`'s ADR.
- `Planning~/DEFINITION_OF_DONE.md`.
- `Planning~/Evidence/P4-GATE/` (the immediately preceding gate, same shape).
- `Planning~/USER_ACTIONS.md` ("Required before public 1.0 claims").

## Allowed changes

- `Planning~/Evidence/P5-GATE/`.
- Integration-owned package metadata, asmdefs, changelog, README, planning
  status/index, and public API baselines after verification.

## Forbidden changes

- New semantics, relaxed tests, runtime fixes, or claims stronger than
  evidence.
- Introducing any performance default, regression threshold, or
  supported-hardware-class claim that `Planning~/USER_ACTIONS.md` requires
  explicit owner approval for. This gate confirms the evidence exists and no
  such claim was smuggled in, not that the owner has approved one.

## Deliverables

- Clean detached-package verification report (mirroring `P4-009`'s clean
  detached UPM harness), full P1+P2+P3+P4+P5 regression, `OQ-007`'s
  resolution confirmed closed, public API hashes, dependency report, claims
  inventory, known limitations, and Phase 6 inputs (AI/MCP integration needs
  a finalized hot-reload contract to build tooling against, per
  `Documentation~/roadmap.md`'s Phase 6 scope).

## Acceptance criteria

- Static/schema, compile, and full P1+P2+P3+P4+P5 focused/full suites pass
  from a clean committed snapshot.
- Every reload strategy's golden-equivalence/state-preservation proof
  (`P5-004` through `P5-006`) and the scheduler-interaction proof (`P5-007`)
  re-run and pass against the committed snapshot, not merely cited from an
  earlier run.
- `OQ-007` is confirmed `Resolved` in `Planning~/OPEN_QUESTIONS.md` with a
  linked accepted ADR.
- Every hot-reload claim in the package, `README.md`, `CHANGELOG.md`, or
  documentation is confirmed no stronger than its evidence -- specifically,
  no regression threshold, "acceptable reload cost," or supported-scale
  claim exists without the explicit owner approval `USER_ACTIONS.md`
  requires.
- Every required verification command in this card passes, and results are
  recorded under `Planning~/Evidence/P5-GATE/`.

## Required verification

```text
clean detached UPM harness
all P1, P2, P3, P4, and P5 focused/full suites
full-restart, subtree-restart, and migration state-preservation re-runs (P5-004/P5-005/P5-006)
scheduler-interaction re-run (P5-007)
live interactive Editor workflow re-run (P5-008)
OQ-007 resolution audit
public API, generated artifact, dependency, cleanliness, and diff checks
```

## Handoff notes

- Follows the same self-verification shape as `P2-025`/`P3-013`/`P4-009` (no
  separate reviewer requirement); see `Planning~/AGENT_WORKFLOW.md`.
- Phase 6 (AI and MCP) depends on this gate's confirmation that the
  hot-reload contract is stable enough for MCP tooling to trigger and
  observe reloads programmatically, per `Documentation~/ai-and-mcp.md`.
