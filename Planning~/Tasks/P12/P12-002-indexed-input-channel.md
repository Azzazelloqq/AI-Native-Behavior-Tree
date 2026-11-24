# P12-002 — Indexed input channel: group or squad data read by every member

Status: `Blocked` — waits for a design decision: an independent design read of the API shape is running
(2026-11-24). Implementation does not start before the orchestrator records the decision.

## Objective

A game binds an array of rows of any length (one row per squad or group) and a per-agent row index; every
member's Burst node reads its squad's row through the ordinary `[AibtInput]` handle. No per-agent copy of
the facts; the game writes rows and indices between steps; same values in place and in jobs.

## Decision record

- Orchestrator, 2026-11-24, under the owner's delegation of 2026-11-23:
  `C:\UnityProjects\_aibt_acceptance\SweetKingdom\plan-2026-11-24.md`, decision 1 (the consumer's request 1,
  "shared group blackboard"). An input channel whose row is chosen per agent by an index, not Shared-scope
  blackboard keys: the row a node reads is chosen at run time from `agent.Row`
  (`Runtime/Simulation/SimulationNativeV1.cs`, `Runtime/Simulation/TreeGroup.cs`), so this needs runtime
  and `TreeGroup` additions only and touches no protected area.
- The Shared-scope route stays the provisional slot of ADR-P10-008.

## Depends on

- No work item. It waits for the design read and the orchestrator's decision on it.

## Open before implementation (the design read)

- Index type and ownership; bounds and faults; wake on change through the index; a single group-level row;
  when the game may write rows and indices.
- Whether squad facts must also be readable by standard library nodes through blackboard keys (asked of the
  consumer in the reply of 2026-11-24).

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format, `CodeGen~`,
  `package.json`, pins under `Tests/Fixtures`, `Documentation~/specifications`, execution semantics,
  existing public signatures (only additions).

## Evidence

`Planning~/Evidence/P12-002/README.md` (to be written).
