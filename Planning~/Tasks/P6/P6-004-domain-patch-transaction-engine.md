# P6-004 — Domain-patch transaction engine

Status: `Done`

**Pre-implementation location correction (2026-08-27):** this card's original text placed
the engine in `Authoring/Patching/` and cited `Authoring/Editing/` as where `P3-006`'s
operations live. Both are wrong, confirmed by reading the real files and the real `.asmdef`
graph: `SemanticEditOperations`/`SemanticEditTransaction` are `AIBT.Editor.Editing`
(physically `Editor/Editing/`), and `LayoutOrganizationOperations` (`ADR-P6-002`'s layout
primitive) is `AIBT.Editor.Organization` (`Editor/Organization/`) -- both part of the
`AIBT.Editor` assembly. `Editor/AIBT.Editor.asmdef` references `AIBT.Runtime` and
`AIBT.Authoring`; nothing under `Authoring/` may reference `Editor/` at all
(`architecture.md`'s dependency direction). This engine is corrected to live in
**`Editor/Patching/`**, matching where its own dependencies actually live and consistent
with `architecture.md`'s description of `Editor` owning orchestration over Authoring
primitives (the same shape `P5-008`'s `HotReloadWorkflowWindow` already uses). Not an
"Authoring-layer API" as originally stated -- an Editor-layer one, still with no MCP
dependency.

## Objective

Implement `P6-002`'s accepted domain-patch model (`ADR-P6-002-domain-patch-revision-and-diff-model.md`,
`AIBT-025`) as real production code in `Editor/Patching/`: a semantic-patch transaction
(expected-revision precondition, atomicity, diff) built on `SemanticEditTransaction`, and a
layout-patch transaction (content-hash precondition, diff) built on
`LayoutOrganizationOperations` -- reusing both rather than duplicating their gating.

## Depends on

- `P6-002` (accepted ADR; this card implements it directly).

## Required reading

- `Documentation~/decisions/ADR-P6-002-domain-patch-revision-and-diff-model.md` (the accepted
  decision this card implements).
- `Editor/Editing/SemanticEditOperations.cs`, `Editor/Editing/SemanticEditTransaction.cs`
  (`P3-006`'s operations and the already-accepted accept-or-reject-unchanged transaction
  primitive — add/remove/connect/disconnect/set-parameter, gated by
  `ReferenceCompiler`/`TreeValidator`) — reuse both directly; do not build a second,
  weaker validation path.
- `Editor/Organization/LayoutOrganizationOperations.cs` (pin/unpin/position/group/note/
  reroute — the layout-patch primitive `ADR-P6-002` decided on).
- `Editor/Layout/CanonicalLayoutJsonWriter.cs`, `Runtime/Core/Identity/StableHash.cs` — the
  content-hash mechanism for the layout precondition (`StableHash.Sha256Hex` over
  `CanonicalLayoutJsonWriter.Write`), per `ADR-P6-002`.
- `Planning~/Evidence/P3-007/` (layout/semantic isolation invariant this
  engine's diff separation must preserve).
- `Documentation~/specifications/diagnostics-v1.md` (a rejected patch
  surfaces structured diagnostics, not a bespoke error type).

## Allowed changes

- `Editor/Patching/` (new).
- `Tests/Editor/Patching/` (new).
- `Planning~/Evidence/P6-004/`.

## Forbidden changes

- `Editor/Editing/`'s existing GraphView-facing undo/redo path — this card
  adds a headless entry point alongside it, mirroring `P3-009`/`P5-008`'s
  "own facade crossing the Editor/Authoring boundary" pattern; it does not
  replace the interactive editor workflow.
- Weakening `P3-006`'s "every semantic edit is gated by the real compiler/
  validator" contract to make transactions more convenient.
- Any MCP dependency — `P6-006`/`P6-007` expose this over MCP, not this card.

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

## Outcome

Done. `Editor/Patching/SemanticPatchTransaction.cs`/`LayoutPatchTransaction.cs` implement
`ADR-P6-002` as real production code, corrected to live in `Editor/Patching/` rather than the
card's original `Authoring/Patching/` (confirmed by reading `Editor/AIBT.Editor.asmdef`'s own
references: `SemanticEditTransaction`/`SemanticEditOperations`/`LayoutOrganizationOperations`
are all `AIBT.Editor`, which `Authoring` cannot reference under `architecture.md`'s dependency
direction) -- disclosed and corrected before any code was written, not silently fixed. 8 new
tests pass live against the real Unity `6000.5.8f1` Editor via Unity MCP (atomicity,
revision/hash preconditions rejecting before any operation runs, structured
`SemanticDiff`/`LayoutDiff`, dry-run-is-free), plus a 9/9 regression re-run of `P3-007`'s
isolation suite and the existing `SemanticEditOperations`/`SemanticEditTransaction` tests.
Full detail in `Planning~/Evidence/P6-004/`.
