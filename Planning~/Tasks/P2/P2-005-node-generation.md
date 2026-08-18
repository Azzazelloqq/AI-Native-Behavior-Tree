# P2-005 — Deterministic node generation and scope compilation

Status: `Done`

## Objective

Generate canonical node manifests, typed blackboard access bindings, exact config packing, and deterministic registry entries for public Burst nodes, while implementing the accepted Agent/Shared authoring and compiled metadata.

## Depends on

- `P2-004`.
- `P2-003`.

## Required reading

- `Documentation~/specifications/burst-node-abi-v1.md`
- `Documentation~/specifications/node-contract-v1.md`
- `Documentation~/specifications/compiled-program-v1.md`
- `Documentation~/specifications/identity-and-hashing-v1.md`
- `Documentation~/specifications/agent-shared-blackboard-v1.md`

## Allowed changes

- Relevant emitters under `CodeGen~/`
- `Authoring/Registry/Generated/`
- `Authoring/Compilation/Generated/`
- Required focused changes under `Authoring/Model/Blackboard/` and `Authoring/Compilation/` for the accepted Agent/Shared contract
- Explicitly versioned schema and compiled-format changes authorized by P2-001/P2-003
- `Tests/Editor/CodeGen/Generation/`
- `Tests/Editor/BlackboardScopes/`
- `Tests/Fixtures/P2/CodeGen/`

## Forbidden changes

- Runtime dispatch or executor.
- Reflection/scanning, arbitrary serializer hooks, or reinterpretation of P1 literal access metadata.

## Deliverables

- Deterministic manifest/registry generation and unmanaged config packers.
- Typed key-parameter binding to compiled access ordinals.
- Versioned Agent context and Shared reducer metadata carried from canonical authoring through compiled records and content hash.

## Acceptance criteria

- Output and registry hash are independent of file, assembly, declaration enumeration, and culture order.
- FNV collision and duplicate type/version fail before compilation.
- Config offsets, padding, size, alignment, and little-endian bytes match the accepted ABI.
- Wrong key scope/type/direction fails before execution.
- Missing/incompatible Agent context metadata and unconfigured Shared writes fail compilation with stable diagnostics.
- Agent/Shared metadata is canonical, schema-valid, and covered by semantic/compiled-content hashes.
- P1 compiler and registry goldens remain unchanged unless an accepted format-version migration explicitly updates them.

## Required verification

```text
shuffled declaration and culture matrix
layout/padding/overflow tests
typed access positive and negative tests
Agent/Shared canonical/compiler/hash fixtures
P1 compiler/registry regression suite
git diff --check
```

## Handoff notes

- Registered unmanaged values require an accepted canonical serializer/equality binding; do not invent one locally.
