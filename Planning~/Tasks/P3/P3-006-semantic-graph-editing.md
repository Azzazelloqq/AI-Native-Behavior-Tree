# P3-006 — Semantic graph editing

Status: `Draft`

## Objective

Let a user add, remove, connect, and reconfigure nodes through the graph UI, writing changes back through the existing Authoring/compiler pipeline.

## Depends on

- `P3-003`.

## Required reading

- `Documentation~/specifications/canonical-json-v1.md`.
- `Documentation~/specifications/node-contract-v1.md`.
- `Documentation~/editor-and-layout.md` (AI-editing behavior contract — human and AI edits go through the same path).

## Allowed changes

- `Assets/AIBT/Editor/Editing/` (new).
- `Tests/Editor/Editing/` fixtures.
- `Planning~/Evidence/P3-006/`.

## Forbidden changes

- Any new compiler entry point or bypass of the existing Authoring validation/compilation pipeline — editing must go through the same path an AI author or hand-written `.aibt.json` would.
- `Runtime` changes; this card is a consumer of the existing public node registry and compiled-program contracts only.

## Deliverables

- Add/remove node, connect/disconnect, and reconfigure-node-fields interactions in the `P3-003` adapter.
- Every edit round-trips through canonical parse → validate → compile, surfacing the same structured diagnostics an out-of-band `.aibt.json` edit would.

## Acceptance criteria

- A sequence of graph edits produces the identical canonical `.aibt.json` as hand-authoring the same tree directly, byte-for-byte after canonicalization.
- An edit that would produce an invalid tree (e.g. a required child missing) is rejected or flagged with the same diagnostic an out-of-band validation pass would produce — no separate, weaker in-editor validation path.
- Undo/redo covers every semantic edit.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <semantic editing fixture>
canonical round-trip comparison against hand-authored fixtures
```

## Handoff notes

- `P3-007`, `P3-008`, `P3-009`, `P3-010`, and `P3-012` all build on this card's editing surface — keep the edit-to-compile path's shape stable once accepted.
- `P3-007`'s isolation proof depends on this card producing a *minimal* diff per edit (only the touched node/field changes) so the "layout-only edit changes nothing here" comparison is meaningful.
