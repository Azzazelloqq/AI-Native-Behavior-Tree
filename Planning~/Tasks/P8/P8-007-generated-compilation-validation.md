# P8-007 — Validate generated native trees from the graph

Status: `In Progress`

## Objective

Give an author a direct, honest generated-program compilation check for a version 2 graph.

## Dependencies

- P7-008 and P7-009 are complete; existing generated-program and registry contracts apply.

## Scope

- Collect generated node and registered-type descriptors from discovered player shards.
- Report conflicting registered-type definitions and fail closed.
- Expose an explicit native compilation check with its diagnostics in the graph. It does not launch a Player or claim that a game integration exists.

## Acceptance

- Valid generated trees compile through the existing production compiler.
- Invalid or missing bindings produce actionable compiler diagnostics.
- Existing reference validation and editor behavior remain available.
- Run focused and broad EditMode tests and static verification.

## Current verification

- Isolated Unity 6000.7.0b1 EditMode run: 1481/1481 passed on the integrated snapshot.
- Static/schema verification passed; targeted graph, preview and project-catalog checks passed.
- Swarm Arena's project-generated tree compiled in a separate consumer project; changing a
  blackboard binding in the actual EditorWindow and saving it passed an automated check.
- Unity 6000.5.8f1 and a manual interactive editor acceptance remain unverified in this environment.
  This card remains in progress until its applicable acceptance gates are completed.
