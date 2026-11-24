# P12-002 — Indexed input channel: group or squad data read by every member

Status: `Ready` — the shape is decided (2026-11-24, `Planning~/Evidence/P12-002/decision.md`, after the
independent design read `Planning~/Evidence/P12-002/design-read.md`); starts after P12-001's acceptance.

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

- P12-001 (one writer in the checkout; its documentation sections are extended here).

## Decided shape

One additive overload `TreeGroup.TryBindInput<T>(InputChannel<T> channel, NativeArray<T> rows,
NativeArray<int> rowOfSlot)`; the two-argument method keeps "row = slot". The nine semantics items of
`Planning~/Evidence/P12-002/decision.md` are the specification: rows of any length copied whole at the start
of the step; indices at least `Capacity` long, copied at the start of the step; a negative index reads zeros;
an index beyond the rows in a live agent's slot refuses the step with `InvalidArgument`; wake on change is the
existing value compare through the index; one routine chooses the row for jobs, in place, wake and trace;
the trace and MCP name the row if that is additive; an amendment to ADR-P10-008 comes with the code.

## Still open (asked of the consumer, not blocking)

- Whether squad facts must also be readable by standard library nodes through blackboard keys (asked of the
  consumer in the reply of 2026-11-24).

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format, `CodeGen~`,
  `package.json`, pins under `Tests/Fixtures`, `Documentation~/specifications`, execution semantics,
  existing public signatures (only additions).

## Evidence

`Planning~/Evidence/P12-002/README.md` (to be written).
