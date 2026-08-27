# P6-005 — MCP server host, discovery tools, and permission enforcement

Status: `Draft`

## Objective

Build the actual MCP server assembly per `P6-001`'s accepted transport/
hosting decision, wire the "Discovery" tool/resource group from
`Documentation~/ai-and-mcp.md` (project and capability description, node
search and contract lookup, tree and policy listing, schema and
documentation resources) over `P6-003`'s query layer, and implement real
enforcement of `P6-001`'s permission-model taxonomy so every later tool
card has an enforcement point to declare against.

## Depends on

- `P6-001` (accepted ADR; this card implements the transport/hosting/
  permission-model decision).
- `P6-003` (node catalog and project manifest query layer).

## Required reading

- `Documentation~/decisions/ADR-P6-001-*.md` (the accepted decision this
  card implements).
- `Documentation~/ai-and-mcp.md`'s "Core MCP surface > Discovery" and
  "Safe mutation protocol"'s permission sentence.
- `Documentation~/architecture.md`'s "Dependency direction" (confirmed/
  corrected by `P6-001`'s ADR) — the new assembly must satisfy it exactly.
- `Planning~/Evidence/P6-001/` (the disposable spike's real-client proof —
  this card's server must pass the same kind of check, not merely unit
  tests against in-process objects).

## Allowed changes

- The new MCP assembly at the location `P6-001`'s ADR names (expected
  `MCP/`, sibling to `Authoring/`/`Editor/`/`CodeGen~/`).
- `Tests/Editor/Mcp/Discovery/` (new) or the test location `P6-001`'s ADR
  names for the chosen hosting model.
- `Planning~/Evidence/P6-005/`.

## Forbidden changes

- Any player-facing assembly reference to the new MCP assembly.
- Any mutation tool (authoring/node-development) — this card is
  discovery/read-only plus the permission-enforcement mechanism itself.
- Silently widening the new assembly's references beyond what `P6-001`'s
  ADR named.

## Deliverables

- A running MCP server following `P6-001`'s accepted hosting model.
- Discovery tools/resources: project+capability description, node search/
  contract lookup, tree+policy listing, schema/documentation resources —
  all backed by `P6-003`, none reimplementing its formatting.
- A permission-enforcement mechanism: every tool/resource declares its
  category from `P6-001`'s taxonomy, and a call outside the categories
  granted to the current session is rejected with a structured diagnostic,
  never silently downgraded or silently allowed.
- Real protocol-conformance evidence: a real MCP client connects, lists
  resources, calls each discovery tool, and receives spec-shaped responses
  (`WORK_PACKAGES.md`'s "protocol conformance tests" output for Phase 6).

## Acceptance criteria

- A permission check has at least one positive (allowed) and one negative
  (rejected) test per declared category, using the actual enforcement
  path, not a mocked one.
- A discovery call for a project with zero registered custom nodes returns
  exactly the Phase 1 fixture/built-in catalog, honestly, matching
  `P6-003`'s own disclosure.
- The server starts and stops cleanly without leaving the Editor (or
  external process, per the accepted hosting model) in a degraded state,
  proven by a repeated start/stop cycle test.

## Required verification

```text
real MCP client: connect, list resources, call every discovery tool
permission enforcement positive/negative matrix
repeated start/stop cycle
Verify-Static.ps1
```

## Handoff notes

- `P6-006`, `P6-007`, `P6-008`, `P6-009`, `P6-010` all register their tools
  through this card's server/permission scaffolding; none should stand up
  a second server or a second enforcement path.
