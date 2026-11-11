# P9-002 — Ready-made node library

Status: `Draft`

## Authorization and dependencies

Owner approved in chat on 2026-11-11. Depends on P9-001.

## Objective

Ship enough leaves that a common agent needs no custom node: waits, blackboard set/compare,
movement, Animator, GameObject, events and checks under `aibt.unity.*`, plus a package-provided
Burst shard of standard native conditions/setters a project catalog can include.

## Plan and owned paths

1. Managed `aibt.unity.*` leaves in `Agents/Nodes/`, each with a manifest summary, usage notes
   and a behavior test.
2. Native standard shard. First verify empirically the P7-028 constraints (frozen `aibt.core.*`,
   byte-identical `aibt.stdlib.*` manifest parity, `GeneratedHandle` support on the reference
   side). If parity is unreachable the nodes ship as a native-only shard, disclosed as such.
3. Regenerate `Documentation~/generated/`.

Owned: `Agents/Nodes/`, `Authoring/Registry/Generated/`, `Authoring/Model/Nodes/`, `Tests/`,
`Documentation~/generated/`, `CHANGELOG.md`, `Planning~/Evidence/P9-002/`.

## Acceptance

Every shipped node is discoverable in the editor search and MCP catalog, has a behavior test and
documented Running/abort semantics. No node is presented as Burst-compatible unless it is.
