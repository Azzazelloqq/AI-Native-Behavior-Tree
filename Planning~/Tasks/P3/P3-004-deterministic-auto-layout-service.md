# P3-004 — Deterministic auto-layout service

Status: `Draft`

## Objective

Implement the auto-layout algorithm specified in `P3-002`: given a semantic tree and no prior layout, produce a deterministic initial node arrangement.

## Depends on

- `P3-003`.

## Required reading

- `Documentation~/specifications/editor-layout-v1.md`.
- `Documentation~/editor-and-layout.md` (readability requirements).

## Allowed changes

- `Assets/AIBT/Editor/Layout/` (new).
- `Tests/Editor/Layout/` fixtures.
- `Planning~/Evidence/P3-004/`.

## Forbidden changes

- Reading or writing `.aibt.json`; this service only produces `.aibt.layout.json` content from an already-loaded semantic tree.
- Any change to `P3-002`'s accepted algorithm contract; implement it, do not redesign it here.

## Deliverables

- An auto-layout service consumed by `P3-003`'s adapter when a document has no existing `.aibt.layout.json`.
- A golden test corpus: representative trees (shallow/wide, deep, mixed) with their expected deterministic layout output committed as fixtures.

## Acceptance criteria

- Running the service twice on the same tree produces byte-identical `.aibt.layout.json` output.
- Running it on a tree that is a superset of a previous tree (nodes appended, none removed) does not reposition previously-placed nodes, per the "scoped re-layout" requirement in `Documentation~/editor-and-layout.md`.
- Large representative trees (same scale class as `P3-001`'s spike fixture) lay out without becoming visually degenerate (overlapping nodes, unreadable edge crossings beyond what the golden fixtures document as acceptable).

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <auto-layout fixture>
```

## Handoff notes

- `P3-005` overrides this service's output via pinning; keep the two concerns separable (auto-layout never overwrites a pinned position).
