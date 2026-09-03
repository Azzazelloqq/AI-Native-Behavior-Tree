# P7-018 — Tree-format v2 promotion to default

Status: `Draft`

## Objective

`P7-001`'s public-API/persisted-format stability review found every writer in the codebase still
defaults `*.aibt.json` to `formatVersion: 1`; version 2 (extended blackboard bindings, Agent/Shared
scope contracts) is reader-only-so-far, gated behind `ReferenceCompilationPolicy.Phase1` disabling
the capability flags it needs in production — the same gap `P6-014` ("Agent/Shared blackboard
scope") already found and deferred. Put to the owner directly during `P7-016`'s gate session
(2026-09-03): the owner's decision is that v2 should become the real default, not stay an
indefinitely-experimental opt-in — this card is that promotion.

## Depends on

- `P6-014` (Agent/Shared blackboard scope — read its own disclosed blocker before assuming this
  card's scope; it may turn out to already contain most of the real unblocking work).
- `P7-001` (the stability review that surfaced this gap) and `Planning~/Evidence/P7-GATE/
  p7-001-stability-decision.md` (the recorded owner decision this card implements).

## Required reading

- `Planning~/Evidence/P6-014/` (if it exists by the time this card is assigned — the prior
  investigation into why `ReferenceCompilationPolicy.Phase1` disables Agent/Shared capability
  flags).
- `Documentation~/blackboards.md` / whichever spec document defines the v1/v2 format-version
  contract and the Agent/Shared scope semantics v2 unlocks.
- Every current writer of `*.aibt.json` (confirm the full list before assuming scope — this may
  span `Authoring/`, `Editor/`, and `MCP/`).

## Allowed changes

- `Runtime/`/`Authoring/` — enabling the Agent/Shared capability flags `ReferenceCompilationPolicy`
  currently disables (`Phase1` → a new policy value or `Phase1`'s own flags flipped, whichever the
  investigation finds correct — this is exactly the kind of decision `DECISION_BOUNDARIES.md` flags
  as escalate-first if it turns out to be a genuine public/cross-assembly contract change).
- Every production writer of `*.aibt.json`, changed to default `formatVersion: 2`.
- `Planning~/Evidence/P7-018/`.

## Forbidden changes

- Silently deciding the capability-flag-enable shape without checking `DECISION_BOUNDARIES.md`
  first — if it is a cross-assembly API/persisted-format shape change beyond "flip a policy
  default," escalate before implementing.
- Breaking v1-document round-tripping — v1 documents must keep reading and compiling exactly as
  before; this card changes the *default for new writes*, not v1's own continued support.

## Deliverables

- A real, working default-v2 write path, proven against the full existing test suite plus new
  coverage for whatever Agent/Shared scope behavior v2 actually unlocks.
- `Planning~/Evidence/P7-GATE/p7-001-stability-decision.md`'s open item 3 updated to point at this
  card's own evidence once done.

## Acceptance criteria

- Full regression passes with zero v1-path regressions.
- A real document round-trip (author → v2 write → v2 read → compile → execute) demonstrates the
  Agent/Shared scope capability actually works end-to-end, not just that the flag compiles.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
a real Agent/Shared-scope document authored, written as v2, read back, compiled, and executed
```

## Handoff notes

- Spun off from `P7-016`'s own gate session, per the owner's decision recorded in
  `Planning~/Evidence/P7-GATE/p7-001-stability-decision.md` — not required for `P7-016`'s own
  verdict (real production work, forbidden inside a gate task by every prior gate's own
  discipline), but required before the tree format can honestly be called stable for `1.0`.
