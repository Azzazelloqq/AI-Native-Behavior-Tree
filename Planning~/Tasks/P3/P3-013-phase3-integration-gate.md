# P3-013 — Phase 3 integration gate

Status: `Draft`

## Objective

Verify the complete visual editor and layout system, evidence boundaries, and repository hygiene from a clean committed snapshot.

## Depends on

- `P3-002`.
- `P3-003`.
- `P3-004`.
- `P3-005`.
- `P3-006`.
- `P3-007`.
- `P3-008`.
- `P3-009`.
- `P3-010`.
- `P3-011`.
- `P3-012`.

## Required reading

- Every P3 card and its accepted decisions/specs.
- `Planning~/DEFINITION_OF_DONE.md`
- `Planning~/Evidence/P2-GATE/` (the immediately preceding gate, same shape)

## Allowed changes

- `Planning~/Evidence/P3-GATE/`
- Integration-owned package metadata, asmdefs, changelog, README, planning status/index, and public API baselines after verification.

## Forbidden changes

- New semantics, relaxed tests, runtime fixes, or claims stronger than evidence.

## Deliverables

- Clean detached-package verification report, contract checklist, public API hashes, dependency report, claims inventory, known limitations, and Phase 4/5 inputs (Phase 4's benchmark research already depends on `P3-012`'s raw editor measurements; Phase 5's hot reload depends on the editor/compiler revision-stability this phase establishes).

## Acceptance criteria

- Static/schema, compile, full P1+P2 regression, and every P3 focused suite pass from a clean committed snapshot.
- `P3-007`'s layout/semantic isolation proof re-runs and passes against the committed snapshot, not merely cited from an earlier run.
- The editor introduces no `Runtime` dependency on `UnityEditor`/MCP/LLM/DOTS Entities beyond what `Editor`'s own asmdef already declares, and `Editor` depends on `Authoring`/`Runtime` only, never the reverse.
- Node coordinates, colors, groups, and comments are confirmed not to influence compiled output (via `P3-007`), not merely asserted by policy.
- Large-graph measurements from `P3-012` are recorded, not converted into a performance default or supported-size claim.
- Every required verification command in this card passes, and results are recorded under `Planning~/Evidence/P3-GATE/`.

## Required verification

```text
clean detached UPM harness
all P1, P2, and P3 focused/full suites
layout/semantic isolation proof (P3-007)
large-graph measurement audit (P3-012)
public API, generated artifact, dependency, cleanliness, and diff checks
```

## Handoff notes

- Follows the same self-verification shape as `P2-025` (no separate reviewer requirement); see `Planning~/AGENT_WORKFLOW.md`.
