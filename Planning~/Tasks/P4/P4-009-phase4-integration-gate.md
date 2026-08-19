# P4-009 — Phase 4 integration gate

Status: `Draft`

## Objective

Verify the complete benchmark/scheduler research output, evidence boundaries, and repository hygiene from a clean committed snapshot, mirroring `P2-025`/`P3-013`'s shape.

## Depends on

- `P4-001`.
- `P4-002`.
- `P4-003`.
- `P4-004`.
- `P4-005`.
- `P4-006`.
- `P4-007`.
- `P4-008`.

## Required reading

- Every P4 card and its accepted decisions.
- `Planning~/DEFINITION_OF_DONE.md`.
- `Planning~/Evidence/P3-GATE/` (the immediately preceding gate, same shape).
- `Planning~/USER_ACTIONS.md` ("Required before public 1.0 claims").

## Allowed changes

- `Planning~/Evidence/P4-GATE/`.
- Integration-owned package metadata, asmdefs, changelog, README, planning status/index, and public API baselines after verification.

## Forbidden changes

- New semantics, relaxed tests, runtime fixes, or claims stronger than evidence.
- Introducing any performance default, regression threshold, or supported-hardware-class claim that `Planning~/USER_ACTIONS.md` requires explicit owner approval for. This gate confirms the evidence exists and no such claim was smuggled in, not that the owner has approved one.

## Deliverables

- Clean detached-package verification report (mirroring `P3-013`'s clean detached UPM harness), full P1+P2+P3+P4 regression, `OQ-006`'s resolution confirmed closed, public API hashes, dependency report, claims inventory, known limitations, and Phase 5 inputs (hot reload needs a finalized scheduler contract to build against).

## Acceptance criteria

- Static/schema, compile, and full P1+P2+P3+P4 focused/full suites pass from a clean committed snapshot.
- `PipelinedJobs`'s equivalence proof (`P4-003`) and `Auto`'s determinism-on-rerun proof (`P4-005`) re-run and pass against the committed snapshot, not merely cited from an earlier run.
- `OQ-006` is confirmed `Resolved` in `Planning~/OPEN_QUESTIONS.md` with a linked accepted ADR.
- Every benchmark claim in the package, `README.md`, `CHANGELOG.md`, or documentation is confirmed no stronger than its evidence -- specifically, no regression threshold, scheduling default, or supported-hardware-class claim exists without the explicit owner approval `USER_ACTIONS.md` requires.
- Every required verification command in this card passes, and results are recorded under `Planning~/Evidence/P4-GATE/`.

## Required verification

```text
clean detached UPM harness
all P1, P2, P3, and P4 focused/full suites
PipelinedJobs equivalence re-run (P4-003)
Auto determinism-on-rerun re-run (P4-005)
OQ-006 resolution audit
public API, generated artifact, dependency, cleanliness, and diff checks
```

## Handoff notes

- Follows the same self-verification shape as `P2-025`/`P3-013` (no separate reviewer requirement); see `Planning~/AGENT_WORKFLOW.md`.
- Phase 5 (hot reload) depends on this gate's confirmation that the scheduler contract is stable and that no tree-semantic change can result from a scheduling decision, per `Documentation~/execution-and-scheduling.md`'s own "Semantic guarantees."
