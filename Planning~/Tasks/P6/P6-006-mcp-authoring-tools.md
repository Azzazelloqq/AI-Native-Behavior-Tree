# P6-006 — MCP authoring tools

Status: `Draft`

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
- `Authoring/Patching/` (`P6-004`'s transaction engine — the only mutation
  path this card may call).
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
