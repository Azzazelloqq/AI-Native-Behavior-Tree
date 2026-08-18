# P2-007 — Native Tree and Agent blackboard storage

Status: `Done`

## Objective

Implement fixed-layout native Tree and Agent blackboards with declared typed access, canonical equality, versions, defaults, and deterministic binding.

## Depends on

- `P2-003`.
- `P2-005`.
- `P2-006`.

## Required reading

- `Documentation~/specifications/blackboard-v1.md`
- `Documentation~/specifications/agent-shared-blackboard-v1.md`
- `Documentation~/specifications/determinism-v1.md`

## Allowed changes

- `Runtime/Blackboard/Native/Tree/`
- `Runtime/Blackboard/Native/Agent/`
- `Tests/Runtime/NativeExecution/Blackboard/TreeAndAgent/`

## Forbidden changes

- Runtime string lookup, implicit conversion, shared reduction, managed storage fallback, or undeclared access.

## Deliverables

- Native value arenas, defaults, slot versions/revisions, typed ordinal accessors, and host-created Agent contexts.

## Acceptance criteria

- All built-ins and an explicitly registered unmanaged fixture match P1 canonical value/equality behavior.
- Equal writes are no-ops; changed writes and reset update versions exactly as specified.
- Multiple trees bound to one compatible Agent context observe shared Agent state; different agents remain isolated.
- Incompatible type/layout/schema bindings and concurrent ownership conflicts reject before execution.
- No managed allocation, lookup, or resize occurs in initialized access paths.

## Required verification

```text
built-in/registered value matrix
-0/NaN/fixed-string/Enum32/AssetId negatives
multi-tree same-agent and different-agent tests
reset/version/overflow tests
warm initialized access allocation probe
```

## Handoff notes

- Shared scope is owned by P2-008 and remains inaccessible here.
