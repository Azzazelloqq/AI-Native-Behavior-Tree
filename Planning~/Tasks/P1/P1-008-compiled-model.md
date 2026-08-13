# P1-008 — Logical compiled-program model

Status: `Draft`

## Objective

Define concrete immutable C# records for Compiled Program v1 without native allocation or execution.

## Depends on

- `P1-001`
- `P1-004`
- `P1-005`

## Required reading

- `specifications/compiled-program-v1.md`

## Allowed changes

- `Runtime/Compiled/Model/`
- `Tests/Runtime/CompiledModel/`

## Forbidden changes

- Compiler, executor, native containers, binary persistence, or changing normative fields.

## Deliverables

- Header, node records, child indices, config blob, blackboard slots/defaults, debug map, and named invalid index.
- Constructor/builder boundary that validates bounds, offsets, alignment, and immutability.

## Acceptance criteria

- All indices/counts/offsets are 32-bit and overflow is rejected.
- Records expose no authoring object references or platform pointers.
- Invalid ranges, overlaps, root indices, and alignments have tests.

## Required verification

- Focused model invariant tests.
