# P2-021 — Initialized Burst allocation and native lifetime gate

Status: `Done`

## Objective

Measure and enforce zero managed allocation after warmup plus leak-free bounded native lifetimes for the initialized P2 execution paths.

## Depends on

- `P2-020`.

## Required reading

- `Documentation~/decisions.md` (`AIBT-003`)
- `Documentation~/benchmarks.md`
- `Planning~/DEFINITION_OF_DONE.md`

## Allowed changes

- `Tests/Runtime/NativeExecution/Allocation/`
- `Tools~/Verification/P2/Allocation/`
- `Planning~/Evidence/P2-021/`

## Forbidden changes

- Runtime semantic fixes inside a verification card.
- Claims covering initialization, compilation, reference/managed nodes, host materialization, or unmeasured platforms.

## Deliverables

- Repository-owned warmup/measurement protocol, controlled allocation probe, capacity-exhaustion cases, and native lifetime/leak report.

## Acceptance criteria

- Native Immediate, Budgeted/Resume, and BatchedJobsSameFrame representative windows report zero managed GC bytes/events after all storage is preallocated.
- A controlled allocation fixture fails the gate, proving measurement sensitivity.
- No native container creation, resize, or disposal occurs inside the measured tick window.
- Success, abort, fault, restart, capacity failure, and final dispose are leak-free.
- Raw per-window samples and exact environment/toolchain are retained; claims are limited to measured paths.

## Required verification

```text
Player-style allocation harness after warmup
controlled allocation failure probe
native leak detection matrix
sanitized raw-sample validation
```

## Handoff notes

- Any failure returns to the owning implementation card; do not weaken the probe or move work outside the measured region artificially.
