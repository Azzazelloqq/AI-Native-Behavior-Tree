# P6-006 — MCP authoring tools

Status: `Done`

## Objective

Expose `Documentation~/ai-and-mcp.md`'s "Authoring" tool group over MCP:
create tree; add/remove/move/replace/configure nodes; declare/change
blackboard keys; extract/inline subtrees; apply a domain-patch transaction;
request layout of the affected region. Every mutating call goes through
`P6-004`'s transaction engine — no tool in this card invents its own
validation or persistence path.

## Depends on

- `P6-004` (domain-patch transaction engine).
- `P6-005` (MCP server host and permission enforcement).

## Required reading

- `Documentation~/ai-and-mcp.md`'s "Core MCP surface > Authoring" section.
- `Editor/Patching/` (`P6-004`'s transaction engine — the only mutation
  path this card may call; this line originally said `Authoring/Patching/`,
  corrected per `P6-004`'s own evidence, which found the engine actually
  lives in `Editor/Patching/` since its dependencies — `SemanticEditOperations`/
  `SemanticEditTransaction`/`LayoutOrganizationOperations` — are `AIBT.Editor`
  types).
- `Editor/Organization/` and `Editor/Layout/` (`P3-004`/`P3-005`'s
  deterministic auto-layout and manual-organization services) for
  "request layout of the affected region" — reuse, do not reimplement.
- `Planning~/Evidence/P3-007/` (layout/semantic isolation invariant this
  card's layout-request tool must not cross into semantic mutation).

## Allowed changes

- The MCP assembly's authoring-tool module (location per `P6-001`'s ADR).
- `Tests/Editor/Mcp/Authoring/` (new) or the equivalent test location.
- `Planning~/Evidence/P6-006/`.

## Forbidden changes

- Any direct `TreeDocument` mutation outside `P6-004`'s transaction engine.
- Any node-development (generate/analyze/compile-new-node-type) tool —
  `P6-009`'s job.
- Weakening the domain-patch atomicity/dry-run/diff guarantees `P6-004`
  already proved.

## Deliverables

- One MCP tool per authoring operation listed in `ai-and-mcp.md`, each
  accepting an expected revision and supporting dry-run, each returning
  the semantic/layout diff and structured diagnostics `P6-004` already
  produces, permission-tagged per `P6-001`'s taxonomy (semantic edit vs.
  layout edit, distinctly).
- Every tool call is atomic end-to-end through the actual MCP transport,
  not merely at the in-process transaction-engine layer already proven by
  `P6-004`.

## Acceptance criteria

- A real MCP client can create a tree, add a node, connect it, set a
  parameter, and read back a correct semantic diff and new revision, in
  one session.
- A dry-run authoring call over MCP produces the same result as `P6-004`'s
  own dry-run and persists nothing, proven by a follow-up discovery call
  showing the document unchanged.
- A semantic-edit tool call is rejected for a session holding only
  read/layout-edit permission, and a layout-edit tool call is rejected for
  a session holding only read permission — both via `P6-005`'s real
  enforcement path.
- Extract/inline-subtree round-trips (extract then inline) to a
  semantically equivalent tree, verified by compiled-content-hash
  comparison.

## Required verification

```text
real MCP client: full authoring session (create/add/connect/configure/read-back)
dry-run parity against P6-004
permission-negative matrix (semantic edit, layout edit)
extract/inline round-trip content-hash proof
Verify-Static.ps1
```

## Handoff notes

- `P6-011` (generated agent documentation) uses this card's tools as the
  source for authoring recipes; keep tool descriptions accurate, they will
  be quoted verbatim.

## Outcome

Done — see `Planning~/Evidence/P6-006/` for the full account. Summary:

- All 11 Authoring tools implemented in `MCP/Authoring/`, wired through
  `McpToolDispatcher.cs` (11 new permission-tagged cases) and relayed by 11
  new thin server methods in `MCP~/Server/AuthoringTools.cs`. Every mutation
  routes through `P6-004`'s `SemanticPatchTransaction`/`LayoutPatchTransaction`.
- Real gaps found and resolved before/while implementing, all disclosed in
  the evidence: no pure `Move`/`Replace`/blackboard/extract-inline operation
  existed (added as new pure functions inside this card's own module, never
  touching `Editor/Editing/`); a real atomicity trap in `TreeDocument`'s
  legacy mutating instance methods (avoided, never called); no treeId→path
  resolution existed (added to `AibtTreeDiscovery`); no semantic-tree
  persistence helper existed (added, mirroring `LayoutPersistenceController`).
- **A pre-existing, load-bearing bug found and escalated**: `TreeDocument.Revision`
  is never persisted to `*.aibt.json`, so it always resets to 1 across the
  reload-per-call boundary every MCP call crosses — `SemanticPatchTransaction.Apply`'s
  own revision precondition could never detect a real concurrent edit between
  two separate MCP calls. Escalated to the owner rather than silently picked;
  **decided: a content-hash precondition** (`expectedHash`/`contentHash`),
  the same fix `ADR-P6-002` already made for `LayoutDocument`. `ai-and-mcp.md`'s
  "checked against its Revision" line is now inaccurate for the MCP surface
  specifically — a documentation correction is recommended follow-up work,
  out of this card's own scope.
- Two interpretive judgment calls, both disclosed: "replace" keeps
  `NodeId`/`Children`, swapping only type/parameters; "extract/inline" is
  payload-based (no live subtree-reference concept exists anywhere in AIBT).
- Two further real bugs found live: `ReferenceCompilerOptions.SourceId`
  needed a relative logical path, not the absolute file path (`AIBT3010`,
  caught by the first EditMode run); the Inspector CLI's `--tool-arg`
  key=value parser mishandles JSON-text argument values (worked around with
  `--tool-args-json`, live-only finding).
- Verified: 17 new EditMode tests (real dispatcher entry point, including
  the extract/inline compiled-content-hash round-trip), 45/45 regression
  (Discovery+Patching+Editing), 62/62 full re-run after live verification's
  domain reload, and a full live session (create/add/dry-run-remove/extract/
  inline/request_layout, plus the complete permission-negative matrix)
  against the real permanent server via the official Inspector CLI and the
  real Unity bridge. `Verify-Static.ps1` and `git diff --check` both pass.

**Addendum (2026-08-28):** `McpAuthoringJson.WriteNode`/`ReadNode` silently dropped `Observer` and
generated `Bindings` on every extract/inline subtree round trip — fixed, both fields now round-trip
byte-for-byte. Found while fixing: the production built-in registry these tools use has no
Condition-kind node type at all, so no tree carrying a legitimate `Observer` can currently be
validated/accepted through this card's own tools regardless — see
`Planning~/Evidence/P6-006/README.md`'s 2026-08-28 addendum for the full account.

**Addendum (2026-08-28):** this card's hand-rolled diagnostic JSON writer (dropped
`treeInstanceId`/`documentId`/`line`/`column`/`relatedLocations`/`suggestedOperation`) was replaced
with the shared, canonical `MCP/McpDiagnosticJson.cs` — the same real `DiagnosticJson.Serialize`
entry point `P6-007`'s tools already used, extracted to a neutral location so neither card's folder
owns "the real one." See `Planning~/Evidence/P6-006/README.md`'s second 2026-08-28 addendum.
