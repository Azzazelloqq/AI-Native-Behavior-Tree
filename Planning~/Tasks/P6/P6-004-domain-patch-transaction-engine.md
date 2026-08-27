# P6-004 — Domain-patch transaction engine

Status: `Draft`

## Objective

Implement `P6-002`'s accepted domain-patch model as a headless Authoring-layer
API: apply an atomic, revision-checked, dry-run-capable, diff-producing patch
to a `TreeDocument`, built on top of `P3-006`'s existing operations rather
than duplicating their validation/compilation gating.

## Depends on

- `P6-002` (accepted ADR; this card implements it directly).

## Required reading

- `Documentation~/decisions/ADR-P6-002-*.md` (the accepted decision this
  card implements).
- `Authoring/Editing/` (`P3-006`'s operations — add/remove/connect/
  disconnect/set-parameter, gated by `ReferenceCompiler`/`TreeValidator`,
  with undo/redo) — reuse the operations and their compiler/validator
  gating; do not build a second, weaker validation path.
- `Planning~/Evidence/P3-007/` (layout/semantic isolation invariant this
  engine's diff separation must preserve).
- `Documentation~/specifications/diagnostics-v1.md` (a rejected patch
  surfaces structured diagnostics, not a bespoke error type).

## Allowed changes

- `Authoring/Patching/` (new).
- `Tests/Editor/Patching/` (new).
- `Planning~/Evidence/P6-004/`.

## Forbidden changes

- `Editor/Editing/`'s existing GraphView-facing undo/redo path — this card
  adds a headless entry point alongside it, mirroring `P3-009`/`P5-008`'s
  "own facade crossing the Editor/Authoring boundary" pattern; it does not
  replace the interactive editor workflow.
- Weakening `P3-006`'s "every semantic edit is gated by the real compiler/
  validator" contract to make transactions more convenient.
- Any MCP dependency — this is a plain Authoring API (`P6-006`/`P6-007`
  expose it over MCP).

## Deliverables

- A patch-transaction executor accepting an expected revision and an
  ordered list of `P6-002`-shaped operations, returning: the resulting
  revision, structured diagnostics (if rejected, nothing persisted),
  a semantic diff, and a layout diff.
- Dry-run mode: identical computation, zero persistence, verified by a
  direct before/after document-state comparison in tests, not by
  inspection alone.
- Revision-mismatch rejection: a patch built against a stale expected
  revision is rejected with a stable diagnostic before any operation runs.

## Acceptance criteria

- A multi-operation patch with one invalid operation persists nothing —
  proven by a test that inspects document state after a rejected patch and
  finds it byte-identical to before.
- A layout-only patch (move/pin/group/comment) produces an empty semantic
  diff and an unchanged semantic revision, consistent with `P3-007`.
- A dry-run patch never mutates the document, the compiled program, or the
  revision, proven by direct comparison.
- Revision-mismatch, invalid-operation, and successful-apply are each
  covered by at least one test using a real `TreeDocument`, not a mock.

## Required verification

```text
focused Patching tests (atomicity, dry-run, diff separation, revision)
P3-007 isolation proof re-run unaffected
Verify-Static.ps1
```

## Handoff notes

- `P6-006` (MCP authoring tools) and `P6-007` (MCP validate/compile/
  simulate tools, for its dry-run/validate-only path) both call this engine
  directly; neither should reimplement transaction semantics.
