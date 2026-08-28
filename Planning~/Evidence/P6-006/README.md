# P6-006 MCP authoring tools evidence

## Result

Done. `MCP/Authoring/` implements all 11 tools `ai-and-mcp.md`'s Authoring section lists (create
tree; add/remove/move/replace/configure nodes; declare/change blackboard keys; extract/inline
subtrees; apply a domain-patch transaction; request layout of the affected region), every
mutation routed through `P6-004`'s `SemanticPatchTransaction`/`LayoutPatchTransaction` -- no tool
invents its own validation or persistence path. `McpToolDispatcher.cs` (`P6-005`) gained 11 new
`case`s, each permission-tagged (`SemanticEdit` for the 10 semantic-tree tools, `LayoutEdit` for
`request_layout`), delegating to `McpAuthoringToolDispatcher`. `MCP~/Server/AuthoringTools.cs`
adds the 11 matching thin server-side relays, mirroring `DiscoveryTools.cs`'s exact shape.

## Real gaps found between the card/ADR text and the actual code (found before writing code)

1. **No pure operation existed for move/replace/blackboard-declare/extract-inline.**
   `Editor/Editing/SemanticEditOperations.cs` (P3-006, outside this card's Allowed changes) only
   has `AddNode`/`RemoveNode`/`Connect`/`Disconnect`/`SetParameter`. Resolved without escalation:
   `SemanticEditTransaction.Apply`/`SemanticPatchTransaction.Apply` accept *any*
   `Func<TreeDocument,TreeDocument>`, not only ones from `SemanticEditOperations` -- the missing
   operations are new pure functions in `MCP/Authoring/McpAuthoringOperations.cs`, built only from
   already-public APIs. `Move` needed no new logic at all (composes `Disconnect`+`Connect`).
2. **A real atomicity trap avoided.** `TreeDocument` also exposes legacy *mutating* instance
   methods (`SetBlackboard`, instance `AddNode`, `ReplaceNodeAt`, `RemoveNodeAt`). Using any of
   them inside a `SemanticEditTransaction.Apply` edit function would silently corrupt the
   caller's "unchanged" document on rejection, since `Apply` returns `before` by reference on
   reject. `McpAuthoringOperations.cs` never calls them -- every function builds a full new
   `TreeDocument` via the public constructor, the same pure-copy pattern
   `SemanticEditOperations.Rebuild` already uses.
3. **No treeId -> file path resolution existed anywhere.** `AibtTreeDiscovery.Scan` (P6-005) read
   file paths internally but discarded them before returning. Extended `AibtTreeDiscovery.ScanResult`
   with a parallel `TreePaths` list and a `TryFindPath(treeId, out path)` helper -- squarely
   inside this card's own `MCP/` module.
4. **No semantic-tree persistence helper existed** (only `LayoutPersistenceController` did, for
   layout). `MCP/Authoring/TreeDocumentPersistence.cs` adds `Load`/`Save`, reusing the existing
   public `CanonicalTreeJson.Parse`/`.Serialize` -- not reimplementing them.
5. **A load-bearing, pre-existing bug, not caused by this card but only observable through it:
   `TreeDocument.Revision` is never persisted to `*.aibt.json`.** `CanonicalTreeJsonWriter` never
   writes it; `CanonicalTreeJson.ReadDocument` hard-codes `default` (-> `Revision(1)`) on every
   parse -- confirmed by reading both directly, then proving it live (a real
   `apply_domain_patch` call reported `accepted:true` with a bumped in-memory revision, but the
   very next call's fresh reload from disk saw revision 1 again, rejecting a correct
   `expectedRevision`). `P6-003`'s `get_project_manifest` already exposed this same always-1
   "revision" field, harmlessly, since it was never checked there. For this card it is not
   harmless: every MCP call reloads the tree fresh from disk with no live session, so
   `SemanticPatchTransaction.Apply`'s own revision precondition (`Editor/Patching/`, outside this
   card's Allowed changes) would always trivially pass, unable to detect a real concurrent edit
   between two separate calls. **Escalated to the owner** (this is a normative safety-contract
   question, not a private implementation detail) rather than silently picking a fix. **Decision:
   content-hash precondition**, the same fix `ADR-P6-002` already made for `LayoutDocument` (which
   also has no persisted revision field). Every mutating semantic tool's wire contract is
   `expectedHash`/`contentHash` (a hex SHA-256 string, reusing `CanonicalTreeJson.Serialize`'s
   already-computed `SemanticHash`), checked by the dispatcher *before* ever calling
   `SemanticPatchTransaction.Apply` (which is then given the just-loaded document's own trivially
   -matching actual revision, so its own precondition never independently rejects a
   hash-verified call -- its guarantee is unweakened, just no longer the only check). `ai-and-mcp.md`'s "checked against its Revision" line for semantic patches is now inaccurate for the MCP surface specifically and should be corrected in a documentation follow-up; the in-process `Editor/Editing` human-editor path (a single live session, no reload) is unaffected and still genuinely uses `Revision`.

## Two interpretive judgment calls (no existing precedent decided them)

- **"Replace a node"** = swap `TypeId`/`TypeVersion`/`Parameters` in place, keeping `NodeId` and
  `Children`. A full subtree swap composes `remove`+`add` in one `apply_domain_patch` call.
- **"Extract/inline subtree"** = a payload-based operation (removed nodes + attachment point
  returned as inline JSON), not a live subtree reference -- no subtree-reference node type or
  schema field exists anywhere in AIBT (checked `tree.schema.json`, `data-formats.md`, the node
  registry: "subtree references" is prose-only, never implemented), and the acceptance
  criterion's **content-hash** round-trip proof could not be satisfied by a live-reference design
  anyway (it would compile differently). A caller wanting a persisted, separate reusable tree
  asset feeds the same payload into `create_tree`.

## Implementation

`MCP/Authoring/`: `McpAuthoringOperations.cs` (new pure ops), `McpAuthoringJson.cs` (JSON <->
`NodeDocument`/`SemanticValue`/`BlackboardKeyDefinition` mapping, scoped to tool payloads --
tree-scoped built-in scalar blackboard types only, Enum32/Registered/Agent/Shared explicitly
rejected as a disclosed limitation), `TreeDocumentPersistence.cs`, `McpAuthoringDiagnostics.cs`
(`AIBT9015`-`9021`), `McpToolException.cs`, `McpAuthoringToolDispatcher.cs` (the 11 tool handlers
plus `apply_domain_patch`'s shared `add`/`remove`/`move`/`replace`/`configure`/`setBlackboard`
operation-vocabulary builder, reused by both the single-op tools and the composer).
`McpToolDispatcher.cs` gained 11 permission-tagged `case`s and now catches `McpToolException` in
`WithPermission`. `AibtTreeDiscovery.cs` gained `TreePaths`/`TryFindPath`. `request_layout`
bootstraps a missing `*.aibt.layout.json` without a hash precondition (same "no precondition for
a brand-new resource" reasoning as `create_tree`), otherwise reuses
`LayoutPersistenceController`/`DeterministicAutoLayoutService` directly, unmodified.

`MCP~/Server/AuthoringTools.cs`: 11 thin relays. Nested payloads (a node definition, an
operations list, an arbitrary parameter value) are accepted as raw JSON *text* parameters, not
POCO-schema-generated objects -- simpler and more directly inspectable via a real MCP client for
this shape of deeply nested, tool-specific payload; `insertIndex` uses `-1` to mean "append."

## A second real bug found and fixed during implementation (before live verification)

`ReferenceCompilerOptions.SourceId` must be a relative, forward-slash, `..`-free logical path
(`AIBT3010`) -- the first `BuildRegistryAndOptions` draft passed the absolute, backslash,
drive-lettered file path `TreeDocumentPersistence`/`AibtTreeDiscovery` use for real file I/O.
Found immediately by the first EditMode test run (every mutating call rejected with `AIBT3010`),
fixed with a `ToLogicalSourceId` helper (`Path.GetRelativePath` + slash normalization).

## Unity EditMode tests (17, all real, run live against `6000.5.8f1`)

`Tests/Editor/Mcp/Authoring/McpAuthoringToolDispatcherTests.cs`, calling the real
`McpToolDispatcher.Dispatch` entry point (the same one `McpBridgeListener` calls for every real
request) -- not a mock: full authoring session (create -> atomic add-decorator-with-its-own-child
patch -> configure -> diff/hash read-back); dry-run parity (same accept/reject outcome as the
real call, file untouched, then the real call does persist); the full 9-tool `SemanticEdit`
permission-negative matrix plus the `LayoutEdit` one for `request_layout`; content-hash-mismatch
rejected before any operation runs; move/replace/setBlackboard each get a real accept+ondisk-state
proof; extract-then-inline round-trips to the **same compiled content hash** (`CompiledContentHasher`,
the same mechanism `P3-007`'s isolation proof relies on) as before extraction.

## Live end-to-end verification (real MCP client, real permanent server, real Unity bridge)

Bridge started live in the actually-open Unity `6000.5.8f1` Editor via Unity MCP `execute_code`
(mirroring `P6-005`'s own methodology). The official `@modelcontextprotocol/inspector` CLI,
configured via `.mcp.json`-shaped `--config`/`--server` files (P6-005's own documented Windows
env-passthrough workaround), against the real, permanent `MCP~/Server/`
(`dotnet run --project Assets/AIBT/MCP~/Server`), all 11 authoring tools plus discovery listed
with real schemas via `tools/list`. A real fixture tree (`tree.mcp-live-test`) was created under
the actual `Modules` project's `Assets/` and fully exercised: `aibt_create_tree` ->
`aibt_add_node` (one rejected for an invalid decorator child-count, one accepted) ->
`aibt_remove_node` dry-run (file verified byte-unchanged on disk afterward) ->
`aibt_extract_subtree` -> `aibt_inline_subtree` (returned to the **exact same `contentHash`** as
before extraction) -> `aibt_request_layout` (bootstrapped a fresh `*.aibt.layout.json`). The full
permission-negative matrix was proven live too: a `SemanticEdit` tool call with only `LayoutEdit`
granted, and a `LayoutEdit` tool call with only `Read` granted, both rejected with `AIBT9012`
through the real enforcement path. All live-created fixture files were deleted afterward and the
Editor console showed zero errors on the following refresh.

**A third real bug found, live-only** (the two EditMode-covered `AIBT3010`/`AIBT1007` bugs above
were caught before live verification): the Inspector CLI's `--tool-arg key=value` pair parser
mishandles a value that is itself JSON text (embedded `{`/`"`/`:` characters) -- every JSON-string
argument call failed with a generic `isError:true` and no useful message. Fixed by using the CLI's
own `--tool-args-json '{...}'` flag (a single verbatim JSON object, no key=value coercion)
instead, which worked reliably for every subsequent call. Recorded here for future sessions in
this workspace, alongside `P6-005`'s own two Inspector CLI findings.

## Verification

```text
Unity MCP run_tests (EditMode): AIBT.Tests.Editor.Mcp.Authoring.* -- 17/17 passed
Unity MCP run_tests (EditMode): Mcp.Discovery + Patching + Editing regression -- 45/45 passed, no regressions
Unity MCP run_tests (EditMode): full re-run after live-verification domain reload -- 62/62 passed
dotnet build MCP~/Server -- 0 warnings, 0 errors
Live: real bridge + real permanent MCP~/Server/ + official Inspector CLI --
  tools/list (11 authoring + 3 discovery, real schemas)
  full authoring session: create -> add (1 rejected, 1 accepted) -> dry-run remove (file
    verified unchanged) -> extract -> inline (contentHash round-trip proven) -> request_layout
  permission-negative matrix: SemanticEdit-only-granted rejection, LayoutEdit-only-granted
    rejection -- both AIBT9012 via the real McpPermissionEnforcer path
  live fixture cleaned up; Editor console clean on the following refresh
Tools~/Verification/Verify-Static.ps1 -- passed, 95 work items
git diff --check -- clean
```

## Scope and limitations

- Blackboard tool (`set_blackboard_keys`/`create_tree`'s initial `blackboard`) supports only
  tree-scoped, built-in scalar value types with no default. Enum32, Registered types, and
  Agent/Shared scope (which require a scope contract plus a canonical default this MCP surface
  has no way to accept yet) are rejected explicitly with a clear message, not silently dropped.
- `ai-and-mcp.md`'s domain-patches section text ("a semantic patch... checked against its
  Revision") is now inaccurate for the MCP surface specifically, per finding 5 above -- a
  documentation correction is recommended follow-up, out of this implementation card's own scope.
- Extract/inline preserves `TypeId`/`TypeVersion`/`Children`/`Parameters`/`DisplayName`/
  `Description`/`Tags` exactly; `Observer` and generated `Bindings` are not carried through
  (neither is exercised by any Phase 1 built-in node type, so nothing in the accepted round-trip
  proof exercises them either) -- disclosed, not silently dropped.
- Single client, single Unity instance at a time, same as `P6-005`'s own disclosed scope,
  unchanged here.
