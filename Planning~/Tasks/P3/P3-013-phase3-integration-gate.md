# P3-013 — Phase 3 integration gate

Status: `Done`

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

## Outcome

- **Accepted 2026-08-19 against commit `4700b22e4a17de5d8c118c5d22dfb271a04177fc`.**
  A fresh, otherwise-empty Unity project referencing `com.azzazello.aibt` as a
  local `file:` UPM package (plus its own declared dependencies) compiled
  cleanly and passed the full detached EditMode regression: **953/953**, 0
  failed, 0 skipped -- with none of the 3 failures repeatedly seen inside the
  host `Modules` project across `P3-009` through `P3-012`'s own evidence
  (confirming they were host-project noise, not AIBT defects).
- `P3-007`'s layout/semantic isolation proof re-ran and passed individually
  against this exact snapshot, not merely cited.
- Public API surface recorded across all three production assemblies for
  the first time (`AIBT.Runtime` + `AIBT.Authoring` + `AIBT.Editor`, since
  `Editor/` barely existed at `P2-GATE`): 382 types, 1994 members.
- Assembly dependency audit confirmed `Runtime`/`Authoring` reference no
  `UnityEditor`/MCP/LLM/`Unity.Entities`, and `Editor` depends on
  `Authoring`/`Runtime` only, never the reverse.
- `P3-012`'s large-graph numbers reconfirmed as measurements only, no
  performance default or supported-size claim introduced.
- No defect was found while running this gate; every contract held on first
  measurement in the clean harness.
- `phase4-inputs.md` and `phase5-inputs.md` were produced, extending
  `P2-GATE`'s own Phase 4 handoff and giving Phase 5 (hot reload) the
  editor/compiler revision-stability guarantees (`P3-007`'s isolation proof)
  its design depends on.
- Full evidence: `Planning~/Evidence/P3-GATE/`.
