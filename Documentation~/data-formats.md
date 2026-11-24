# Data formats

## Goals

- Stable, versioned, deterministic, and diff-friendly text.
- Strict validation before import or compilation.
- No dependence on Unity YAML or editor implementation details.
- Exact typed contracts for humans, tools, tests, and AI agents.
- Forward evolution through explicit migrations.

Normative Phase 1 JSON Schemas live in `Schemas~/` and are governed by `specifications/canonical-json-v1.md` and `specifications/behavior-case-v1.md`. Persisted changes require an accepted decision and migration fixture.

## Semantic tree: `.aibt.json`

Contains format version, stable tree identity, human name and description, blackboard declarations, root node, node map, node type versions, typed parameters, ordered children, tags, and semantic metadata. Subtree references are defined by `decisions/ADR-P11-001-subtrees.md` (format version 3).

It does not contain canvas positions, colors, selection, generated runtime indices, compiled buffers, or model-specific prompts.

Node IDs are stable opaque identifiers assigned by authoring tools. Display names are independently editable. Renaming a node does not change hot-reload identity.

## Shared layout: `.aibt.layout.json`

Contains tree identity, layout version, node positions, pinned state, group bounds and membership, comments, sticky notes, edge routing, and layout preferences. Semantic compilation ignores it.

Selection, pan, zoom, open windows, navigation history, and temporary filters are local view state and are not committed.

## Node manifest

Describes a node type independently of its C# implementation. The manifest powers code generation, validation, editor UI, documentation, and MCP discovery.

## Project policy: `.aibt/policy.json`

Defines project-specific validation and style rules. Unknown rules are reported instead of silently ignored. Policy versions are recorded in diagnostics and compiled metadata.

The file is optional. It is read by MCP and the CLI (`validate`, `compile`, `prepare_runtime`, `get_project_manifest`, the authoring tools); the Graph window does not read it. Without it, `validate`, `compile` and `prepare_runtime` check a tree as the Graph window's tree list does: a tree with a managed node with the managed layer's policy (what a `BehaviorTreeAgent` accepts), any other tree with the native default, which allows Agent and Shared scope; `get_project_manifest` returns `"policy": null` (ADR-P9-001, amendment 2026-11-24). The MCP authoring tools choose the policy the same way and report an unreadable file as warning AIBT9008; the Graph window's edit session uses the default its tree list uses (ADR-P9-001, second amendment 2026-11-24).

Add the file to make the rules stricter or different for the whole project: forbid managed or main-thread nodes, require descriptions or a naming style, forbid node types, set the unreachable-node level, or opt Agent and Shared scope in or out. With the file, every tree is checked against it, whatever its host. A file that exists but cannot be read (for example a missing required field) is not applied: the tools use the defaults above and report a warning AIBT9008 that names the problem. Every field below is required (`Schemas~/policy.schema.json`); `maxTreeDepth`, `maxNodesPerTree` and `performance.maxEstimatedCost` are optional. A minimal file that accepts the package's samples:

```json
{
  "format": "aibt.policy",
  "formatVersion": 1,
  "allowManagedNodes": true,
  "allowMainThreadNodes": true,
  "requireTreeDescription": false,
  "requireNodeDescriptions": false,
  "blackboardNaming": "any",
  "forbiddenNodeTypes": [],
  "requireDeterministicNodes": false,
  "allowSideEffects": true,
  "unreachableNodes": "error",
  "supportsAgentScope": true,
  "supportsSharedScope": true,
  "warningsAsErrors": [],
  "performance": { "forbidUnboundedRepeaters": false, "requireEventDrivenServices": false }
}
```

## Behavior case: `.aibtcase.json`

Defines initial blackboard and world inputs, events and ticks, expected statuses, commands, blackboard effects, and optional invariants. Cases test observable behavior rather than implementation order.

## Deterministic serialization

- UTF-8 and LF endings.
- Stable property and collection ordering defined by the serializer.
- Normalized floating-point representation.
- No timestamps or machine paths in canonical output.
- Stable IDs are preserved; tools assign IDs for new elements.
- Formatting is automatic and does not carry semantic meaning.

## References

Project assets use stable Unity GUID and optional local file ID. Scene objects are not embedded in tree definitions; they are supplied through bindings, blackboards, or world snapshots. External subtree and node references include expected contract versions.

## Migrations

Each persisted format and node type is independently versioned. Migrations are deterministic, testable, ordered, and produce a previewable diff. Unsupported future versions fail with structured diagnostics; they are never loaded on a best-effort basis.
