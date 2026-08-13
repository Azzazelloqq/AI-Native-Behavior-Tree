# P1-004 — Node manifest and registry model

Status: `Draft`

## Objective

Implement the model-level node catalog used by validation and the reference compiler, without source generation or runtime reflection discovery.

## Depends on

- `P1-001`
- `P1-002`

## Required reading

- `Schemas~/node-manifest.schema.json`
- `Documentation~/ai-and-mcp.md`
- `specifications/execution-semantics-v1.md`

## Allowed changes

- `Authoring/Model/Nodes/`
- `Authoring/Registry/`
- `Tests/Editor/NodeRegistry/`

## Forbidden changes

- Source generators, user-node runtime dispatch, editor palette UI, or MCP tools.

## Deliverables

- Versioned node manifest, child policy, parameter contracts, declared accesses/effects, execution domain, determinism, and cost hint.
- Explicit registry builder with stable numeric-ID collision detection and deterministic registry hash.
- Built-in Phase 1 manifest fixtures.

## Acceptance criteria

- Duplicate canonical IDs, incompatible versions, and numeric collisions are diagnostics.
- Registry enumeration and hash are insertion-order independent.
- No assembly scanning occurs in player/runtime code.

## Required verification

- Focused registry and collision tests.
