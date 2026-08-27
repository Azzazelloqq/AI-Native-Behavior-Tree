# P6-004 domain-patch transaction engine evidence

## Result

Done. `Editor/Patching/` implements `ADR-P6-002`'s domain-patch model as real production
code: `SemanticPatchTransaction` (expected-revision precondition around
`SemanticEditTransaction`, purpose-built `SemanticDiff`) and `LayoutPatchTransaction`
(content-hash precondition around `LayoutOrganizationOperations`, purpose-built
`LayoutDiff`).

## Location correction (found and applied before any code was written)

The task card's original text placed this engine in `Authoring/Patching/` and cited
`Authoring/Editing/` as where `P3-006`'s operations live. Both were wrong: reading the real
files and the real `.asmdef` graph found `SemanticEditOperations`/`SemanticEditTransaction`
are `AIBT.Editor.Editing` (`Editor/Editing/`), `LayoutOrganizationOperations` is
`AIBT.Editor.Organization` (`Editor/Organization/`) -- both part of the `AIBT.Editor`
assembly, which references `AIBT.Runtime` and `AIBT.Authoring` (confirmed directly from
`Editor/AIBT.Editor.asmdef`). `architecture.md`'s dependency direction forbids anything
under `Authoring/` from referencing `Editor/`. An `Authoring/Patching/` engine could not have
called any of the three types `ADR-P6-002` decided it must be built on. The engine was
implemented in `Editor/Patching/` instead, and the task card corrected in the same commit as
this evidence -- disclosed, not silently fixed.

## Implementation

`Editor/Patching/`:
- `SemanticPatchTransaction.cs` -- revision precondition, then delegates entirely to
  `SemanticEditTransaction.Apply` (no second validation/compilation path), then builds a
  `SemanticDiff` on acceptance.
- `SemanticDiff.cs`/`SemanticDiffEntry.cs` -- structured `Added`/`Removed`/`Changed` entries by
  `NodeId`, using `NodeDocument`'s own `IEquatable<NodeDocument>` implementation for the
  "changed" check (covers type/parameters/children, not just parameters -- broader and more
  correct than the disposable spike's narrower "parameter changed" idea).
- `SemanticPatchDiagnostics.cs` -- `AIBT9009` (revision mismatch).
- `LayoutPatchTransaction.cs` -- content-hash precondition
  (`StableHash.Sha256Hex(CanonicalLayoutJsonWriter.Write(...))`), then runs the composed
  operations, catching `ArgumentException` (the one real failure mode
  `LayoutOrganizationOperations` has, confirmed real via `AddOrUpdateGroup`'s own
  group-membership-conflict check) as a rejection mirroring
  `SemanticEditTransaction`'s accept-or-reject-unchanged contract.
- `LayoutDiff.cs`/`LayoutDiffEntry.cs` -- structured node (moved/pinned/added/removed) and
  group/note/reroute (added/removed/changed, via direct field comparison since none of
  `LayoutGroup`/`LayoutNote`/`LayoutReroute` implement `IEquatable`) entries.
- `LayoutPatchDiagnostics.cs` -- `AIBT9010` (hash mismatch), `AIBT9011` (operation rejected).

No `dryRun` parameter exists. Per `ADR-P6-002`, dry-run is "call the transaction, don't
persist" -- neither transaction has a persistence step to skip. Proven by a test showing two
identical calls produce byte-identical/hash-identical results with the original input
document never mutated.

`Tests/Editor/Patching/` (8 tests, all real):
- `SemanticPatchTransactionTests.cs`: valid multi-op composition + diff correctness (found
  during test-writing that adding a node under `root` correctly marks `root` itself
  `Changed` too, since its `Children` list changed -- the diff catching more than the test's
  first draft expected, not a bug); an invalid operation inside an otherwise-valid multi-op
  patch leaves the document reference byte-identical with real diagnostics; revision mismatch
  rejected before any operation runs (proven via a side-effecting operation that never fires);
  repeat-call dry-run-is-free proof.
- `LayoutPatchTransactionTests.cs`: valid patch + diff correctness; the real
  `AddOrUpdateGroup` conflict exception rejected with the document unchanged; hash mismatch
  rejected before any operation runs; repeat-call dry-run-is-free proof.

Building the parameterized-node fixture for the semantic diff test surfaced a real gap in the
first draft: `aibt.test.success`/`failure`/`running` (the fixtures `P6-002`'s spike used)
declare zero parameters, so `SetParameter` against them fails validation (`AIBT2025`). Fixed
by mirroring `SemanticEditTransactionTests.cs`'s own pattern -- a custom
`aibt.core.test-leaf` manifest registered via the internal `AddBuiltInForTest`, the same
test-only registration path that file already uses, not a new one invented here.

## Verification

```text
Unity MCP run_tests (EditMode): AIBT.Tests.Editor.Patching.* -- 8/8 passed
Unity MCP run_tests (EditMode): LayoutSemanticIsolationTests (P3-007) + SemanticEditOperationsTests +
  SemanticEditTransactionTests (regression) -- 9/9 passed, no regressions
Tools~/Verification/Verify-Static.ps1 -- passed, 95 work items
git diff --check -- clean
```

## Scope and limitations

- No MCP wiring exists yet (`P6-006`/`P6-007`'s job).
- Group/note/reroute diffing compares fields directly (none of `LayoutGroup`/`LayoutNote`/
  `LayoutReroute` implement `IEquatable`); this is proportionate to what `P6-002`'s spike
  actually proved (node pin/position only) plus a reasonable, tested extension -- not a claim
  that every possible layout field combination was exhaustively covered.
- Diagnostic codes `AIBT9009`-`9011` are allocated here as this card's own production choice,
  per `ADR-P6-002`'s explicit deferral of exact codes to this card.
- Concurrent callers, very large patches, and cross-tree patches remain unexercised, as
  `ADR-P6-002` already disclosed.
