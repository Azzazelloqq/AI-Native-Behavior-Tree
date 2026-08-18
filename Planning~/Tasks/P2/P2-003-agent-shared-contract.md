# P2-003 — Agent and Shared blackboard contract

Status: `Done`

## Objective

Complete the persisted and runtime-neutral contract for Agent scope, Shared scope, and deterministic shared reductions.

## Depends on

- `P2-001`.

## Required reading

- `Documentation~/specifications/blackboard-v1.md`
- `Documentation~/specifications/determinism-v1.md`
- `Documentation~/specifications/update-phases-v1.md`
- `Documentation~/specifications/identity-and-hashing-v1.md`

## Allowed changes

- `Documentation~/specifications/blackboard-v1.md`
- `Documentation~/specifications/agent-shared-blackboard-v1.md`
- Required versioned schema/model/compiled-format decision documents and fixtures under `Spikes~/BlackboardScopes/`
- `Planning~/Evidence/P2-003/`

## Forbidden changes

- Production storage or reducers.
- Unordered shared writes, worker-completion-order semantics, implicit schema coercion, or unversioned persisted-format changes.

## Deliverables

- Agent context identity, compatibility, ownership, initialization/reset, versioning, and multi-tree binding rules.
- Shared key reduction metadata and exact Min/Max/Sum/Any/All/First/Last ordering, overflow, float, and custom reducer rules.

## Acceptance criteria

- Canonical authoring and compiled representations are versioned and hash-covered.
- Unconfigured Shared writes and incompatible Agent contexts are stable compilation errors.
- Reduction results are defined independently of worker count, batch partition, and completion order.
- First/Last use an explicit stable semantic key; float/NaN and overflow behavior are unambiguous.
- Independent review accepts the persisted-contract changes before runtime implementation.

## Required verification

```text
schema and canonical-byte fixtures
compiled-content hash sensitivity tests
permuted contribution-order model tests
Verify-Static.ps1
Verify-Schemas.ps1
```

## Handoff notes

- A custom reducer ABI must reuse the accepted unmanaged ABI rules or be explicitly deferred.
