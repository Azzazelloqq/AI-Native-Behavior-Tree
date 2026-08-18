# P2-009 — Immutable native world snapshots

Status: `Done`

## Objective

Provide a versioned typed host-to-job snapshot boundary that freezes one logical revision for an execution pass.

## Depends on

- `P2-001`.
- `P2-002`.

## Required reading

- `Documentation~/specifications/update-phases-v1.md`
- `Documentation~/specifications/determinism-v1.md`
- `Documentation~/specifications/burst-node-abi-v1.md`

## Allowed changes

- `Runtime/Integration/Snapshots/`
- `Tests/Runtime/NativeExecution/Snapshots/`

## Forbidden changes

- Scene-object access from jobs, arbitrary pointers, mutable job-visible host storage, or untyped lookup.

## Deliverables

- Typed snapshot contracts, registry/views, host builder/freeze lifecycle, and declared-access validation.

## Acceptance criteria

- Every scheduled pass observes one immutable revision across all resumes and nodes.
- Mutation or disposal while jobs own the view is rejected safely.
- Undeclared, missing, version-mismatched, and type-mismatched reads are stable failures.
- Immediate and scheduled execution observe identical fixture data.
- Initialized reads allocate no managed memory.

## Required verification

```text
snapshot lifecycle and typed-access tests
frozen-revision resume tests
Jobs safety checks
allocation probe
```

## Handoff notes

- Snapshot schemas are explicit integration contracts; no `UnityEngine.Object` enters the native view.

## Acceptance record

- Independently reviewed and accepted on 2026-08-14 after 7/7 focused Unity tests, 430/430 broader Runtime and snapshot tests, a source-level ownership/type-safety audit, and static/schema/diff gates.
- The final implementation owns its identity domain locally, rejects same-size wrong generic types with a Burst-compatible same-process token, validates the managed-allocation probe with a positive canary, and remains independent of P2-006.
