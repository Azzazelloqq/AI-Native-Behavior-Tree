# P6-012 — Phase 6 integration gate

Status: `Draft`

## Objective

Verify Phase 6 as a whole from a clean, detached snapshot, mirroring
`P2-025`/`P3-013`/`P4-009`/`P5-010`'s own gate shape, and hand off to
Phase 7 (production hardening).

## Depends on

- `P6-001` through `P6-011` (all `Done`).

## Required reading

- `Planning~/Evidence/P5-GATE/` (the gate-package shape to mirror:
  `contract-checklist.md`, `claims-inventory.md`, `known-limitations.md`,
  `gate-runbook.md`, `assembly-dependencies.json`, `public-api.txt`/
  `.sha256`, `verification-results.json`, `phase7-inputs.md`).
- Every Phase 6 card's own Outcome section.
- `Documentation~/roadmap.md`'s Phase 6 exit criterion: "an AI agent can
  safely create, inspect, test, diagnose, and modify a tree without editing
  Unity serialization or guessing node contracts."
- `Planning~/USER_ACTIONS.md` (no threshold/default/hardware-class claim
  may be introduced by this gate).

## Allowed changes

- `Planning~/Evidence/P6-GATE/`.
- `README.md`/`CHANGELOG.md` updates strictly matching verified Phase 6
  behavior.
- `Planning~/OPEN_QUESTIONS.md`, `Planning~/MASTER_PLAN.md` status updates.

## Forbidden changes

- Any production code change to close a gap found here — a gap becomes a
  disclosed limitation or a new follow-up card, never a same-gate fix that
  bypasses the individual card's own verification.
- Any new performance threshold, supported-platform claim, or hardware
  class.

## Deliverables

- A clean detached-UPM-harness compile and full EditMode regression run
  from a fresh project referencing `com.azzazello.aibt` as a local `file:`
  package, matching every prior gate's methodology.
- A real end-to-end proof of the roadmap's exit criterion: one real MCP
  client session that discovers the project, creates a tree, adds/connects/
  configures nodes, validates, compiles, simulates, generates and applies
  one custom node, inspects a trace, and runs one approved benchmark —
  without any Unity Editor serialization edit or node-contract guess by
  the operator driving the client.
- Public API diff versus `P5-GATE`'s recorded surface (391 types, 2024
  members), confirmed additive-only unless a deviation was explicitly
  authorized.
- `contract-checklist.md` mapping every Phase 6 card's contract, plus
  `phase6-inputs.md`'s inherited Phase 5 constraints, to its evidence.
- `claims-inventory.md` and `known-limitations.md` carrying forward every
  unresolved Phase 5 gap (native-backend hot reload, idle-only migration,
  no production Play-mode host, Phase 1-fixture-only leaf set unless
  `P6-009` genuinely changed that) plus any new Phase 6 gap found honestly.
- `phase7-inputs.md` handing off to production hardening.

## Acceptance criteria

- Every Phase 6 card's acceptance criteria are independently re-verified
  from the committed snapshot, not merely cited from an earlier session.
- The end-to-end MCP session proof runs against the real server, not an
  in-process test harness standing in for the transport.
- No claim in `README.md`/`CHANGELOG.md`/generated documentation is
  stronger than the verified evidence.

## Required verification

```text
Verify-Static.ps1
Verify-Schemas.ps1
detached UPM harness: clean import, full EditMode regression
public API surface diff vs P5-GATE
real end-to-end MCP session proof (roadmap exit criterion)
git status / git rev-parse HEAD clean at every checkpoint
```

## Handoff notes

- `phase7-inputs.md` must state plainly whether native-backend hot reload
  and a production Play-mode host remain open before Phase 7 (production
  hardening) begins, since both affect what "production" can honestly mean.
