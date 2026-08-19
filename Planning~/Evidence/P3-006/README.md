# P3-006 semantic graph editing evidence

## Result

- `Editor/Editing/SemanticEditOperations.cs`: mechanical, pure edits over
  `TreeDocument` — `AddNode`, `RemoveNode` (recursive subtree removal with
  parent-reference cleanup; `TreeDocument`'s own raw mutators do **no**
  such cascade, confirmed by inspection), `Connect`/`Disconnect`,
  `SetParameter`. Every call returns a new `TreeDocument`; the input is
  never mutated (verified by test).
- `Editor/Editing/SemanticEditTransaction.cs`: the acceptance gate. Applies
  an edit speculatively, then calls `AIBT.Authoring.ReferenceCompiler.Compile`
  (which itself calls `TreeValidator.Validate`) on the candidate — the
  exact same pipeline an out-of-band `.aibt.json` edit or an AI domain
  operation would go through, per this card's "no separate, weaker
  in-editor validation path" requirement. Rejected edits return the
  pre-edit document unchanged, with the real compiler/validator
  diagnostics attached.
- `Editor/Editing/SemanticEditHistory.cs`: undo/redo as a snapshot stack,
  mirroring `Editor/Organization/LayoutHistory.cs`'s shape.
- 7 tests, all passing:
  - `SemanticEditOperationsTests` (4): add/remove/connect/disconnect/
    set-parameter mechanics, including that removal cascades and the
    original document is never mutated.
  - `SemanticEditTransactionTests` (3):
    - `SequenceOfEditsProducesCanonicalBytesIdenticalToHandAuthoring` — a
      tree built via `AddNode` calls serializes to byte-identical canonical
      `.aibt.json` as the same tree hand-constructed directly, **and**
      compiles successfully.
    - `InvalidEditIsRejectedWithTheSameDiagnosticAnOutOfBandValidationPassWouldProduce`
      — detaching a decorator's only child (violates its
      `ChildPolicy(1, 1, ...)`) is rejected by the transaction, and its
      diagnostic codes are asserted equal to an independent
      `TreeValidator.Validate` call on the same broken document — proving
      the transaction surfaces the real validator's diagnostics, not a
      separate weaker check.
    - `UndoRedoCoversSemanticEdits` — two accepted edits (a parameter
      change and an add-node), undone and redone twice each, restoring the
      exact prior document at every step.

## Decision

No new decision. Two real, checkable findings surfaced along the way
(both fixed in test code, not production code):

1. `AIBT.Authoring.CanonicalTreeJson`'s `ValidateRepresentable` (used by
   both `Serialize` and `ReferenceCompiler.Compile`, which needs the
   canonical bytes for the semantic hash) requires every node's
   `Parameters` and the document's `Tags`/`Metadata` to be **non-null**
   (`SemanticObject.Empty`/`TagSet.Empty`), not C# `null` — undocumented by
   the constructor's own optional-parameter defaults, which default to
   `null`. All test fixtures were fixed to pass explicit empty values.
2. **Phase 1's `ReferenceCompiler` can only execute `BuiltIn`- or
   `TestFixture`-sourced node types.** A `NodeManifest` registered via
   `AddUserExtension` validates structurally but has no reference-handler
   binding and fails compilation with `AIBT3012`. This matches
   `node-contract-v1.md`'s own statement that "Phase 2's public custom-node
   ABI is gated/unopened" — there genuinely is no way to compile a tree
   containing a real custom node type yet. Fixed by registering the test
   leaf via the same internal `AddBuiltInForTest` + explicit
   `NodeHandlerBindingContract` pattern `Tests/Editor/Compilation/ReferenceCompilerTests.cs`
   already establishes for exactly this purpose.

## Scope and limitations

- **No `Editor/Graph/` UI wiring**, same pattern as `P3-004`/`P3-005`: this
  card's `Allowed changes` lists `Editor/Editing/` only. Add/remove/connect
  context menus and inline parameter fields on the live
  `BehaviorTreeGraphView`/`BehaviorTreeNode` are a disclosed follow-up, not
  silently done or silently skipped. The API this UI would call
  (`SemanticEditOperations`, `SemanticEditTransaction`, `SemanticEditHistory`)
  is complete and independently tested.
- `RemoveNode` always removes the whole subtree (no partial "promote
  grandchildren" mode) — the card doesn't specify subtree semantics; whole-
  subtree removal is the safer default (a partial removal would otherwise
  leave orphaned nodes an editor UI would need special handling for, and
  `TreeValidator` would flag as unreachable/dangling regardless).
- `Connect`/`Disconnect` perform no local cycle or multiple-parent check —
  by design, per this card's own "no separate, weaker in-editor validation"
  principle: structural correctness is `TreeValidator`'s job (invoked by
  `SemanticEditTransaction`), not something the mechanical operations
  should reimplement.

See `verification-results.json` for exact commands and results.
