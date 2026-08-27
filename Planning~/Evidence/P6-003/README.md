# P6-003 node catalog and project manifest query layer evidence

## Result

Done. `Authoring/Discovery/` implements a read-only query layer wrapping the already-built
`P1-004` node registry and a newly-added `.aibt/policy.json` reader, formatted per
`Documentation~/ai-and-mcp.md`'s "Information available to an agent" section. No MCP wiring
(`P6-005`'s job).

## Provenance: research found this card thinner than its own prose implied

Before writing any code, the real codebase was checked directly (not assumed from the card's
own description):

- `Authoring/Registry/NodeRegistry.cs` (public) is already a full read-only, indexable,
  hashable catalog. `Authoring/Registry/NodeManifestCanonicalJson.cs` (`internal`, same
  `AIBT.Authoring` assembly) already serializes a `NodeManifest` to exactly the JSON shape
  `ai-and-mcp.md`'s "Node manifest" section requires, field for field. Both are reused
  directly by `NodeCatalogQuery`; neither is reimplemented.
- No ambient "the project's registry" exists anywhere in the codebase -- every registry is
  built explicitly by a caller. `NodeCatalogQuery`/`ProjectManifestQuery` accept a
  `NodeRegistry` instance from their own caller, consistent with this.
- `.aibt/policy.json` had **no reader anywhere in production code**, confirmed by grep --
  only `TreeValidator` consumes an in-memory `TreeValidationPolicy` object, never populated
  from the file. `ProjectPolicySnapshot`/`PolicyDocumentJson` is the first reader for this
  file; it is a plain reporting DTO, not a `TreeValidationPolicy` instance (Discovery reports
  facts, it does not run validation).
- `Runtime/Core/Identity/Revision.cs` already exists (`readonly struct Revision(ulong)`), and
  `Editor/Editing/SemanticEditOperations.cs` already increments it by 1 on every semantic
  edit. The task card's original hedge ("if `P6-002`'s revision model is not yet accepted, use
  a placeholder") was withdrawn before implementation -- `ProjectManifestQuery`'s tree listing
  reads the real `TreeDocument.Revision.Value` directly, proven correct by
  `TreeListingReflectsRealRevisionIncludingAfterASemanticEdit` (also caught a wrong assumption
  of mine during test-writing: a freshly built `TreeDocument`'s revision is `1`, not `0` --
  `TreeDocument`'s constructor normalizes an unset/default `Revision` to `1`
  (`revision.IsValid ? revision : new Revision(1)`), confirmed by reading the real constructor
  after the test failed on the first run).
- **New finding, not anticipated by the card**: `Editor/Editing/SemanticEditTransaction.cs`
  already exists and implements almost the entire "safe mutation protocol" shape
  `ai-and-mcp.md` describes -- `Apply(before, edit, registry, options)` applies a
  `Func<TreeDocument, TreeDocument>` speculatively, compiles/validates the candidate through
  the real `ReferenceCompiler`/`TreeValidator`, and returns either the accepted candidate or
  the original document unchanged with real diagnostics attached. This means `P6-002`'s actual
  open scope is narrower than its own card states: not "invent a transaction mechanism," but
  "add an expected-revision precondition and a semantic/layout diff format around an
  already-working accept-or-reject-unchanged primitive." `P6-002`'s task card is corrected to
  reflect this in the same commit as this card's evidence (see
  `Planning~/Tasks/P6/P6-002-*.md`'s own updated framing).

## Implementation

`Authoring/Discovery/`:
- `NodeCatalogQuery.cs` -- `Search`/`Page`/`TryGetContract`/`SerializeCatalog`, all delegating
  formatting to `NodeManifestCanonicalJson`.
- `DiscoveryDiagnosticCodes.cs` -- `AIBT9008` (`ToolingAndTestInput` range 9000-9999; 9001-9007
  were already taken by `BehaviorCaseJsonDiagnostics`, confirmed by grep before picking a code).
- `ProjectPolicySnapshot.cs` -- the `.aibt/policy.json` reader/DTO, every field from
  `Schemas~/policy.schema.json`; malformed input or a missing file returns a structured
  `Diagnostic` (`AIBT9008`), never an exception escaping to the caller.
- `ProjectManifestQuery.cs` -- assembles capabilities, node-registry hash/count, the policy
  snapshot, and a tree listing (`treeId`, `name`, real `revision`) from caller-supplied
  `TreeDocument`s, deterministically ordered by `TreeId`.

`Tests/Editor/Discovery/` (13 tests, all real, none trivial-pass placeholders):
- `NodeCatalogQueryTests.cs`: search case-insensitivity/determinism, category/summary
  matching, pagination slicing and past-the-end behavior, `TryGetContract` byte-for-byte parity
  against a direct `NodeManifestCanonicalJson.ToJson` call, and the card's own acceptance
  criterion (a newly registered fixture node appears with zero code changes here).
- `ProjectPolicySnapshotTests.cs`: full-field parse of a valid document, malformed-JSON and
  missing-required-field and wrong-`format`-value cases each producing `AIBT9008` rather than
  throwing, and a missing-file case exercising `TryReadFile` specifically.
- `ProjectManifestQueryTests.cs`: real revision reflected before/after a `SemanticEditOperations`
  call, policy summary field parity, deterministic tree ordering.

## Verification

```text
Unity MCP run_tests (EditMode): AIBT.Tests.Editor.Discovery.* -- 13/13 passed
Unity MCP run_tests (EditMode): existing NodeRegistry + Editing suites -- 39/39 passed, no
  regressions
Tools~/Verification/Verify-Static.ps1 -- passed, 95 work items
git diff --check -- clean
```

## Scope and limitations

- No MCP tool/resource wiring exists yet (`P6-005`'s job); this is a plain C# query API.
- `ProjectManifestQuery`'s tree-listing input is caller-supplied; this card does not invent a
  project-wide tree-discovery mechanism, since none exists in AIBT's model yet.
- The output is a `JObject` (Newtonsoft, insertion-ordered), not the byte-level canonical
  writer `NodeManifestCanonicalJson`/`CanonicalTreeJson` use internally for content-hashed
  documents -- proportionate for a read-only reporting endpoint; if a future card needs a
  content hash over the manifest response itself, that canonical byte format still needs to be
  built, not assumed to already exist here.
- `P6-002`'s own card was corrected as a direct consequence of this card's research (see above)
  -- recorded here rather than silently applied.
