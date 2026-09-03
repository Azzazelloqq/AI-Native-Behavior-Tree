# P7-023 — Showcase example behavior trees

Status: `Draft`

## Objective

Nobody has ever actually looked at an AIBT behavior tree in the visual `AIBT Graph` editor
(`AIBT/Graph Editor` menu item, `Editor/Graph/BehaviorTreeGraphWindow.cs`) rendering a real,
illustrative example — confirmed live this session: the only `.aibt.json` documents that exist
anywhere in the repository are either tiny 3-5 node test/golden fixtures built for assertions (not
for looking at), or `Samples~/FullExample`'s 3-node hot-reload-before/after pair. `Samples~/
README.md` itself already lists "patrol and combat," "event-driven reactions," "async commands,"
and "visual organization" among its own planned-but-not-yet-built sample coverage. This card builds
one or more real, deliberately-designed example trees — using real production node types, not
test-only stand-ins — with a proper `.aibt.layout.json` so the graph reads cleanly when opened, and
wires them into the documented onboarding path (`README.md`/`Documentation~/`).

Live-verified this session: opening even an existing golden fixture (`tree.golden.parallel-decorator`)
in `AIBT Graph` already renders correctly (`aibt.core.parallel` → `aibt.core.inverter`/
`aibt.core.repeater` → leaves) — the graph editor itself needs no code change for this card. The gap
is purely content: no example tree exists that is actually designed to be looked at.

## Depends on

- None structurally — the graph editor (`P3` decorators/graph work) and node registry already work.
  This is a content/documentation card.

## Required reading

- `Samples~/README.md` (the existing, not-yet-fulfilled sample-coverage promise this card partially
  fulfills).
- `Samples~/BurstNodes/`, `Samples~/SemanticSlice/` (existing custom production node types available
  to build a richer example tree than built-ins alone allow).
- `Editor/Graph/BehaviorTreeGraphWindow.cs`/`BehaviorTreeGraphView` (how a tree is opened/rendered —
  confirm layout-file wiring, since none of the existing fixtures ship one).
- `Documentation~/data-formats.md`'s `.aibt.layout.json` section (the presentation-layer format a
  clean example needs).

## Allowed changes

- New `Samples~/` content: one or more real `.aibt.json` trees (with `.aibt.layout.json`) built from
  real production node types (built-in composites/decorators plus `BurstNodes`/`SemanticSlice`
  custom leaves where illustrative), each with a clear name and purpose (e.g. a patrol-and-react
  tree, a parallel/decorator showcase).
- `README.md`/`Documentation~/` — a short pointer to where to open an example (`AIBT/Graph Editor` +
  the new sample path), not a rewrite of existing sections.
- `Planning~/Evidence/P7-023/`.

## Forbidden changes

- No change to `BehaviorTreeGraphWindow`/`BehaviorTreeGraphView` or any other production code —
  confirmed live this session that the existing graph editor already renders a real tree correctly
  with zero diagnostics; this card is content-only.
- Do not touch existing test/golden fixtures — new example trees are additive, separate files.

## Deliverables

- At least one new, real example tree under `Samples~/`, opens cleanly in `AIBT Graph` with a
  populated layout (not all nodes stacked at the origin) and zero diagnostics.
- A one-line pointer in `README.md` (or wherever the onboarding path already lives) saying how to
  open it.

## Acceptance criteria

- Live proof: the new tree, opened via `AIBT Graph` against the real project, renders with 0
  diagnostics and a legible (non-overlapping) layout.

## Required verification

```text
Verify-Static.ps1
live open via AIBT/Graph Editor against the real project, screenshot proof
```

## Handoff notes

- Spun off from a direct owner request this session (2026-09-03) — "I have never actually looked at
  a graph with my own eyes" — after live-demoing the existing `parallel-decorator` golden fixture in
  `AIBT Graph` (owner confirmed it renders correctly via a real screenshot). Owner confirmed this is
  in scope for `1.0`, not deferred to a post-release backlog.
