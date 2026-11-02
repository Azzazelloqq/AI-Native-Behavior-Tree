# P8-003 — Refresh the generated-program fixture after built-in catalog expansion

Status: `In Progress`

## Objective

Update the canonical generated-program fixture to reflect the already-shipped built-in node
catalog, without changing compilation behavior or the test's independent serializer assertions.

## Evidence and scope

- The pinned fixture predates P7-028's production built-in leaves. Compilation includes the
  complete registry hash and registry capability bits in the program header.
- A fresh compilation and the independently implemented test serializer agree byte for byte.
  Compared with the old fixture, only the registry hash at bytes 54–85 and the capability byte
  at 154 differ. The latter changes `e1` to `e9` because the expanded catalog includes the
  non-deterministic random condition; the test independently checks capability bits against the
  current registry. No node, tree, blackboard, or executable instruction bytes changed.
- Update the fixture's exact byte stream and its SHA-256 content hash. Do not change assertions,
  serializer logic, runtime behavior, or the authored tree.

## Acceptance

- The existing generated-artifact contract test passes unchanged, including its independent
  field-stream comparison, capability checks and exact canonical byte/hash comparisons.
- Relevant integration tests and static verification pass in an isolated UPM consumer.

## Current verification

- Isolated Unity 6000.7.0b1 EditMode run: 1481/1481 passed on the integrated snapshot.
- Static/schema verification passed; targeted graph, preview and project-catalog checks passed.
- Swarm Arena's project-generated tree compiled in a separate consumer project; changing a
  blackboard binding in the actual EditorWindow and saving it passed an automated check.
- Unity 6000.5.8f1 and a manual interactive editor acceptance remain unverified in this environment.
  This card remains in progress until its applicable acceptance gates are completed.
