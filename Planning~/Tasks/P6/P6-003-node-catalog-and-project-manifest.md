# P6-003 — Node catalog and project manifest query layer

Status: `Draft`

## Objective

Implement a read-only Authoring-layer query surface that formats the
already-built node registry (`P1-004`) and project policy (`.aibt/policy.json`,
already enforced by `TreeValidator`) into the exact information shape
`Documentation~/ai-and-mcp.md` requires an agent to be able to discover:
node catalog with searchable metadata, exact parameter/blackboard schemas,
declared accesses/effects/lifecycle/domain/determinism/cost hints, and
project manifest (capabilities, policy summary, tree/revision listing).
This card builds the query layer only; it is not itself an MCP tool
(`P6-005` exposes it over MCP).

## Depends on

- `P5-010` (Phase 5 integration gate; Phase 6 entry per `MASTER_PLAN.md`).

## Required reading

- `Documentation~/ai-and-mcp.md`'s "Information available to an agent" and
  "Node manifest" sections — the exact field list this card must satisfy.
- `Authoring/Registry/` (`P1-004`'s registry builder — versioned manifest,
  child policy, parameter contracts, declared accesses/effects, execution
  domain, determinism, cost hint) — the source of truth this card formats,
  never duplicates.
- `Schemas~/node-manifest.schema.json`, `Schemas~/policy.schema.json` — the
  exact shapes already normative for manifest and policy data.
- `Planning~/Evidence/P5-GATE/known-limitations.md` — "No production
  per-project leaf-behavior registration mechanism exists; every executable
  leaf... is still a Phase 1 fixture or built-in composite/decorator." This
  card's catalog must honestly reflect whatever is actually registered in
  the current project, never imply a richer catalog exists.
- `Documentation~/burst-node-authoring.md` — the codegen path
  (`P2-004`/`P2-005`) a real custom node would use; the catalog must surface
  a codegen-registered node the same way as a Phase 1 fixture, with no
  special-casing.

## Allowed changes

- `Authoring/Discovery/` (new).
- `Tests/Editor/Discovery/` (new).
- `Planning~/Evidence/P6-003/`.

## Forbidden changes

- Any MCP transport, tool, or resource wiring (`P6-005`'s job) — this is a
  plain C# query API with no MCP dependency.
- A second, hand-maintained node catalog; every field must be sourced from
  `Authoring/Registry/` or `.aibt/policy.json`, never re-declared.
- Claiming pagination or a project-scale guarantee that was not actually
  measured — `ai-and-mcp.md` requires large projects not need every tree/
  node definition in context; this card must implement and test pagination/
  targeted lookup, not merely document the intent.

## Deliverables

- A node-catalog query API: search by canonical ID/keyword, exact single-node
  contract lookup, paginated enumeration.
- A project-manifest query API: registered backends/capabilities, a policy
  summary sourced from `.aibt/policy.json`, and a tree/revision listing
  (revision field sourced from whatever `P6-002`'s accepted ADR defines —
  if `P6-002` is not yet accepted when this card starts, use the tree's
  existing `CompiledContentHash`/document identity and flag the gap in
  Handoff notes rather than inventing a revision counter here).
- Tests proving catalog/manifest output is generated, not hand-authored:
  a newly registered fixture node must appear without touching this card's
  code.

## Acceptance criteria

- Every field `ai-and-mcp.md`'s "Node manifest" section lists (ID, version,
  summary, intended/discouraged use, parameters, ports, allowed children,
  blackboard access, side effects, lifecycle statuses, threading domain,
  determinism, cost category, examples, deprecation/migration) is present
  when the underlying registry entry has it, and honestly absent (never
  synthesized) when it does not.
- A search over a paginated result set returns stable, deterministic
  ordering across repeated calls.
- The project manifest's policy summary matches `.aibt/policy.json` byte
  for byte in meaning (a policy field change is reflected without code
  changes here).
- Enumeration and hash behavior mirror `P1-004`'s own insertion-order
  independence guarantee.

## Required verification

```text
focused Discovery tests (search, pagination, manifest-vs-policy.json parity)
Verify-Static.ps1
```

## Handoff notes

- `P6-005` (MCP discovery tools) wraps this query layer directly; it must
  not reimplement formatting logic.
- `P6-011` (generated agent documentation) consumes this same query layer
  as its data source, per `ai-and-mcp.md`'s "Duplicated hand-maintained
  catalogs are forbidden."
- If `P6-002`'s revision model is not yet accepted, the interim revision
  source used here is a documented placeholder, not a silent commitment —
  `P6-004`/`P6-006` must reconcile it once `P6-002` lands.
