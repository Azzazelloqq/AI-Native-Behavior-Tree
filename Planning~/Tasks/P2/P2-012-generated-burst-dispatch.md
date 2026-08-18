# P2-012 — Generated closed Burst dispatch

Status: `Done`

## Objective

Generate and integrate closed `(typeId, version)` dispatch that calls public Burst nodes with bounded native contexts and no managed fallback.

## Depends on

- `P2-005`.
- `P2-006`.
- `P2-007`.
- `P2-008`.
- `P2-009`.
- `P2-010`.
- `P2-011`.

## Required reading

- `Documentation~/decisions/ADR-P2-012-burst-node-abi-v2.md`
- `Documentation~/specifications/burst-node-abi-v1.md`
- `Documentation~/specifications/burst-node-abi-v2.md`
- `Documentation~/specifications/node-contract-v1.md`
- `Documentation~/specifications/execution-semantics-v1.md`

## Allowed changes

- Relevant dispatch emitters under `CodeGen~/`
- ABI v2 carrier backing and bridge implementation under `Runtime/Nodes/Contracts/`
- `Runtime/Execution/Burst/Dispatch/`
- Narrow ABI v2 built-in metadata and prebinding adapters under `Authoring/Registry/Generated/` and `Authoring/Compilation/Generated/`
- ABI v2 contract/generation coverage under `Tests/Editor/CodeGen/Contracts/` and `Tests/Editor/CodeGen/Generation/`
- `Tests/Runtime/NativeExecution/Dispatch/`
- `Benchmarks~/Phase2/Dispatch/`

## Forbidden changes

- Reflection, delegates, interfaces/virtual calls, raw-pointer tokens, global or `SharedStatic` context registries, function-pointer registry, managed fallback, scheduler policy, or custom composite/decorator ABI.
- ABI v1 execution/compatibility shims, new public ABI v2 names or signatures, or consumer-visible native-container ownership.

## Deliverables

- Deterministic ABI v2 closed dispatch, bounded job-safe node context adapters, and fail-closed ABI v1 migration behavior.

## Acceptance criteria

- Exact ABI v2 type/version invokes the correct callback; ABI v1, unknown type, and version mismatch fault before invocation.
- Shard markers, handshake values, and the final catalog transport stream are v2 and reject v1 transport byte vectors with `AIBT5012`; unchanged configuration, memory, access, value-schema, and catalog-layout streams retain their v1 domains and `U32 1` format version.
- Public names and signatures remain source-compatible with v1; Enter/Tick retain sequential pack `8` and private prefix offsets `0`/`8`/`16` without an exact total-size pin, while every other carrier/context private layout is Runtime-owned, unmanaged, job-safe, and unpinned by `Marshal`/`UnsafeUtility` ABI fixtures.
- Carriers resolve bounded Runtime-owned views directly without raw pointers, global registries, `SharedStatic` current-context state, or ownership transfer.
- The implicit Runtime built-in metadata authority participates in the complete node-registry hash and collision checks but receives no catalog case index or generated dispatch case.
- The narrow Authoring adapter proves exact built-in-authority parity and supplies deterministic prebinding metadata for every declared blackboard, snapshot, command, async-operation, and completion handle; the initialized hot path uses validated ordinals with no string/hash lookup.
- Config is immutable, memory lifetime is correct, and all blackboard/snapshot/command bounds are enforced.
- No callback can reenter execution or throw across the Burst boundary.
- Generated dispatch is deterministic under declaration reorder and contains no forbidden managed dispatch mechanism.
- Burst/AOT evidence proves compiled execution and warm dispatch microcase has zero managed allocation.

## Required verification

```text
callback lifecycle/version/error matrix
ABI v1-to-v2 transport migration plus frozen v1 layout/schema byte-vector matrix
public source-surface, Enter/Tick prefix, and private-carrier contract checks
implicit Runtime built-in registry/no-dispatch-case proof
generated-source SHA comparison
Burst Inspector/AOT compile evidence
1/16/128-type dispatch microbenchmark with raw samples
allocation probe
```

## Handoff notes

- If flat-switch code size is materially problematic, return evidence for a deterministic sharding decision; do not invent dynamic dispatch.
