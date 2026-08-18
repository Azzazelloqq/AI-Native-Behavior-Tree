# P2-011 — Bounded native diagnostics and trace channels

Status: `Done`

## Objective

Record structured diagnostics and trace semantics from jobs without managed messages, allocation, or worker-order nondeterminism.

## Depends on

- `P2-002`.
- `P2-006`.

## Required reading

- `Documentation~/specifications/diagnostics-v1.md`
- `Documentation~/specifications/trace-v1.md`
- `Documentation~/specifications/update-phases-v1.md`

## Allowed changes

- `Runtime/Diagnostics/Native/`
- `Runtime/Trace/Native/`
- `Tests/Runtime/NativeExecution/DiagnosticsAndTrace/`

## Forbidden changes

- Managed strings or collections in jobs, trace-driven semantics, unbounded recording, or completion-order merge.

## Deliverables

- Unmanaged diagnostic/location/event records, bounded per-instance channels, deterministic merge, and host-side message projection.

## Acceptance criteria

- Lifecycle/common fields and semantic ordering match P1 reference traces.
- Per-instance sequences and merged order are independent of workers and batches.
- Capacity exhaustion emits the exact accepted dropped-record summary without changing execution semantics.
- Trace Off performs no record writes; safety/development modes do not change semantics.
- Host projection preserves stable codes/locations and cannot throw across a job boundary.

## Required verification

```text
reference/native trace projection comparison
permuted merge tests
capacity and disabled-channel tests
Burst compile and allocation probe
```

## Handoff notes

- Do not make diagnostic text part of job data; only stable structured fields cross the boundary.

## Acceptance record

- Independently reviewed and accepted on 2026-08-14 after 35/35 focused Unity tests, 427/427 broader Runtime tests, static/schema/diff gates, and a source-level merge audit.
- The accepted implementation copies and rebases trace payloads and diagnostic related-location arenas directly from independent snapshots, rejects cross-partition duplicates or capacity failures before publication, preserves deterministic ordering, and validates initialized append/merge allocation measurement with a positive canary.
