# P2-006 — Native program image and packed instance arenas

Status: `Done`

## Objective

Bind the logical compiled program into immutable job-safe native tables and allocate deterministic fixed-capacity per-instance execution state.

## Depends on

- `P2-002`.

## Required reading

- `Documentation~/specifications/compiled-program-v1.md`
- `Documentation~/specifications/native-runtime-v1.md`
- `Planning~/Evidence/P1-GATE/phase2-inputs.md`

## Allowed changes

- `Runtime/Compiled/Native/`
- `Runtime/State/Native/`
- `Tests/Runtime/NativeExecution/ProgramAndState/`

## Forbidden changes

- Node dispatch, execution semantics, scheduler policy, managed object references, debug strings, or authoring objects in job data.

## Deliverables

- Validating managed-to-native binder and immutable native program image.
- Packed frame, node-memory, generation, parallel-branch, observer, update, and budget state arenas.

## Acceptance criteria

- Every logical semantic field/range is preserved; native bind rejects invalid bounds, alignment, counts, and hashes before scheduling.
- Offsets and capacity calculations use checked arithmetic and deterministic alignment.
- No arena resizes during execution.
- Activation memory clears on every terminal/aborted Exit; Instance memory clears only on create/restart/reset.
- Bind, failure, and dispose paths are leak-free and reject use-after-dispose/job ownership violations.

## Required verification

```text
logical-to-native byte/table projection tests
capacity/overflow/alignment negative matrix
Jobs safety-check tests
native leak detection
P1 compiled-model regression suite
```

## Handoff notes

- Debug/source metadata remains host-side unless a later tracing card proves a bounded native representation is required.

## Acceptance record

- Independently reviewed and accepted on 2026-08-14 after 29/29 focused tests, 392/392 full Runtime tests, clean Unity compilation, zero native-leak warnings, and static/schema/diff gates.
- Native job views retain numeric debug ordinals only; exact debug strings and authoring identities remain host-side.
