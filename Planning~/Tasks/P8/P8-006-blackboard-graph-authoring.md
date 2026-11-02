# P8-006 — Blackboard authoring in the graph

Status: `In Progress`

## Objective

Allow a human author to create tree-scoped blackboard variables and connect compatible generated-node blackboard inputs in the graph editor.

## Dependencies

- P7-008 and P7-028 are complete; existing canonical tree v2 and generated binding contracts apply.

## Scope

- Edit supported built-in tree-scoped variables through undoable semantic operations; preserve unsupported variables and existing contracts.
- Expose generated binding metadata from the shared project catalog and select only compatible keys.
- Create new trees in v2; leave existing v1 files unchanged and explain the binding limitation.
- Preserve dangling bindings visibly so authors can repair them.

## Acceptance

- Variable and binding changes survive save/reopen and undo/redo.
- Incompatible keys cannot be selected; removing a key does not silently rewrite bindings.
- Existing v1 documents remain readable and are not silently migrated.
- Run focused Unity editor tests, broader EditMode tests and static verification.

## Current verification

- Isolated Unity 6000.7.0b1 EditMode run: 1481/1481 passed on the integrated snapshot.
- Static/schema verification passed; targeted graph, preview and project-catalog checks passed.
- Swarm Arena's project-generated tree compiled in a separate consumer project; changing a
  blackboard binding in the actual EditorWindow and saving it passed an automated check.
- Unity 6000.5.8f1 and a manual interactive editor acceptance remain unverified in this environment.
  This card remains in progress until its applicable acceptance gates are completed.
