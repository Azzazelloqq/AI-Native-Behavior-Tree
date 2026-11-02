# P8-005 — Execute built-in and project reference leaves in preview

Status: `In Progress`

## Objective

Make the reference preview execute the built-in leaf behaviors it already accepts and let it
execute registered project reference leaves. Instances of the same node type must keep separate
state within a tree.

## Dependencies

- P3-009, P7-008 and P7-028 are complete.

## Scope

- Preserve existing fixture preview behavior and compiler validation.
- Bind built-in reference leaves and explicitly supplied project providers to the reference
  executor. Create separate behavior instances per runtime node in each preview.
- Use node memory for the built-in Wait counter so its state is isolated under any correct
  reference-executor registration, including parallel branches.
- Wire the existing Preview window to discovered reference providers. Report unsupported
  generated-only nodes as a reference-preview limitation, not as a claim about native execution.
- Bind built-in leaves in the existing hot-reload preview too; project-provider migration remains
  a separate problem because managed behavior state has no migration contract.
- Do not alter native execution or add a managed fallback to Burst execution.

## Acceptance

- Two parallel Wait nodes with different durations finish on their own configured ticks.
- Two instances of one stateful project reference leaf do not share behavior instance state.
- The hot-reload preview can tick a built-in Wait leaf.
- Existing reference-preview parity and hot-reload tests continue to pass. Focused Unity tests,
  static verification and API-reference regeneration cover the new public overload.

## Current verification

- Isolated Unity 6000.7.0b1 EditMode run: 1481/1481 passed on the integrated snapshot.
- Static/schema verification passed; targeted graph, preview and project-catalog checks passed.
- Swarm Arena's project-generated tree compiled in a separate consumer project; changing a
  blackboard binding in the actual EditorWindow and saving it passed an automated check.
- Unity 6000.5.8f1 and a manual interactive editor acceptance remain unverified in this environment.
  This card remains in progress until its applicable acceptance gates are completed.
