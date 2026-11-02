# P8-004 — Discover project nodes in human authoring

Status: `In Progress`

## Objective

Make the graph browser and node palette discover the project's existing registered reference
leaves and generated Burst-node metadata, rather than silently using only built-in manifests.

## Dependencies

- P7-008 and P7-009 are complete. Keep Runtime independent of Editor and MCP.

## Scope

- Reuse generated-shard metadata materialization and reference-leaf provider discovery; keep
  authoring and MCP on a shared Editor-side discovery boundary rather than duplicate registries.
- Include player-project generated shards, exclude test-only assemblies, retain built-ins, and
  report conflicting or unreadable registrations instead of silently declaring a tree valid.
- Use the same registry for the browser, graph rendering and node palette. Keep reference preview
  and generated runtime execution distinct; a manifest alone is not an executable handler.
- Preserve existing public node and persisted-format contracts.

## Acceptance

- Existing project reference-leaf discovery tests continue to pass. A generated project shard is
  available in the graph registry; test-only shards are not included in automatic project discovery.
- Browser diagnostics distinguish unknown contracts and actual validation errors. Conflicting or
  broken registrations are visible and do not corrupt the built-in catalog.
- Existing MCP discovery, graph tests and generated metadata tests pass in an isolated consumer.

## Current verification

- Isolated Unity 6000.7.0b1 EditMode run: 1481/1481 passed on the integrated snapshot.
- Static/schema verification passed; targeted graph, preview and project-catalog checks passed.
- Swarm Arena's project-generated tree compiled in a separate consumer project; changing a
  blackboard binding in the actual EditorWindow and saving it passed an automated check.
- Unity 6000.5.8f1 and a manual interactive editor acceptance remain unverified in this environment.
  This card remains in progress until its applicable acceptance gates are completed.
