# Data formats

## Goals

- Stable, versioned, deterministic, and diff-friendly text.
- Strict validation before import or compilation.
- No dependence on Unity YAML or editor implementation details.
- Exact typed contracts for humans, tools, tests, and AI agents.
- Forward evolution through explicit migrations.

Draft JSON Schemas live in `Schemas~/`. They establish direction and may evolve before the first runtime vertical slice.

## Semantic tree: `.aibt.json`

Contains format version, stable tree identity, human name and description, blackboard declarations, root node, node map, node type versions, typed parameters, ordered children, subtree references, tags, and semantic metadata.

It does not contain canvas positions, colors, selection, generated runtime indices, compiled buffers, or model-specific prompts.

Node IDs are stable opaque identifiers assigned by authoring tools. Display names are independently editable. Renaming a node does not change hot-reload identity.

## Shared layout: `.aibt.layout.json`

Contains tree identity, layout version, node positions, pinned state, group bounds and membership, comments, sticky notes, edge routing, and layout preferences. Semantic compilation ignores it.

Selection, pan, zoom, open windows, navigation history, and temporary filters are local view state and are not committed.

## Node manifest

Describes a node type independently of its C# implementation. The manifest powers code generation, validation, editor UI, documentation, and MCP discovery.

## Project policy: `.aibt/policy.json`

Defines project-specific validation and style rules. Unknown rules are reported instead of silently ignored. Policy versions are recorded in diagnostics and compiled metadata.

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

