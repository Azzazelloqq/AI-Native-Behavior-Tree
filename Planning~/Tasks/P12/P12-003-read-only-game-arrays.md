# P12-003 — Read-only game arrays in nodes: ADR and prototype

Status: `Draft` — not started. After P12-002; an ADR with an independent read comes first, before any code.

## Objective

Decide whether and how a Burst node may read a game-owned array (a grid, a zone map, a threat list) by
index, and prove the chosen shape with a prototype before anything is built.

## Decision record

- Orchestrator, 2026-11-24, under the owner's delegation of 2026-11-23:
  `C:\UnityProjects\_aibt_acceptance\SweetKingdom\plan-2026-11-24.md`, decision 2 (the consumer's request 2).
  As asked, random access to a bound array needs a new handle and context call in the node ABI, the
  generator, the analyzer and a specification section, all protected; ADR-P10-008 rejected a raw-pointer
  read once. So: a design task first, no code.
- The small-struct case (one row for the whole group) is covered by P12-002.

## Depends on

- P12-002.

## Scope

- An ADR under `Documentation~/decisions/` with an independent read.
- A prototype: a copy per step against a job-safe view; `AtomicSafetyHandle` against game jobs.

## Forbidden changes

- Every protected area named in P12-001 until the ADR is accepted and names what it changes.

## Evidence

`Planning~/Evidence/P12-003/README.md` (to be written).
