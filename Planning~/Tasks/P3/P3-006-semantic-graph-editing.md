# P3-006 — Semantic graph editing

Status: `Done`

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

## Outcome

- `Editor/Editing/`: `SemanticEditOperations` (pure add/remove/connect/
  disconnect/set-parameter over `TreeDocument`, with cascade cleanup on
  removal that the raw `TreeDocument` mutators don't provide),
  `SemanticEditTransaction` (gates every edit through the real
  `ReferenceCompiler.Compile`/`TreeValidator.Validate` pipeline — accept iff
  compile succeeds, otherwise the pre-edit document is returned unchanged
  with the real diagnostics), `SemanticEditHistory` (undo/redo snapshot
  stack).
- 7/7 tests passing, including a byte-identical-to-hand-authoring proof and
  an out-of-band-diagnostic-equivalence proof for a rejected edit.
- Two real findings along the way (both fixed in test code): canonical
  serialization requires non-null `SemanticObject`/`TagSet` everywhere, and
  Phase 1's compiler can only execute `BuiltIn`/`TestFixture`-sourced node
  types (no working custom-node ABI yet) — see
  `Planning~/Evidence/P3-006/README.md`'s Decision section.
- **No `Editor/Graph/` UI wiring** — outside this card's `Allowed changes`,
  same pattern as `P3-004`/`P3-005`.
- Full evidence: `Planning~/Evidence/P3-006/README.md`, `verification-results.json`.
