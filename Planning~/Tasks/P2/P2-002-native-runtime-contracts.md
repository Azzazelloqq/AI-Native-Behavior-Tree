# P2-002 — Native ownership and bounded-capacity contracts

Status: `Done`

## Objective

Define ownership, allocator, lifetime, capacity, overflow, safety, and disposal rules for native program images, instance arenas, snapshots, commands, completions, diagnostics, trace, and jobs.

## Depends on

- `P2-001`.

## Required reading

- `Documentation~/architecture.md`
- `Documentation~/specifications/compiled-program-v1.md`
- `Documentation~/specifications/update-phases-v1.md`
- `Documentation~/specifications/trace-v1.md`

## Allowed changes

- `Documentation~/specifications/native-runtime-v1.md`
- A focused P2-002 ADR linked from `Documentation~/decisions.md`
- `Spikes~/NativeOwnership/`
- `Planning~/Evidence/P2-002/`

## Forbidden changes

- Production native containers or executor code.
- Hidden resize/allocation/fallback behavior in an execution pass.
- Disposal or mutation while a scheduled job owns a view.

## Deliverables

- Exact ownership graph, allocator policy, initialized/executing/disposed states, capacity inputs, overflow behavior, and safety-check contract.
- Stable structured diagnostic allocation for every capacity and lifetime failure.

## Acceptance criteria

- Every native allocation has one owner and deterministic disposal point.
- All hot-path buffers are bounded before scheduling; exhaustion rejects atomically without wrap, partial publication, or managed fallback.
- Job-visible memory cannot resize, move, mutate from the host, or be disposed until dependencies complete.
- A small native-container spike covers create, schedule, complete, abort/fault cleanup, and controlled capacity failure with leak detection.

## Required verification

```text
focused native ownership tests
Unity Jobs/Burst safety-check and release-mode compile
native leak detection after success and failure
git diff --check
```

## Handoff notes

- This is a contract/spike card; production storage starts only after acceptance.
