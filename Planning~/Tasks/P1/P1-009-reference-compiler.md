# P1-009 — Deterministic reference compiler

Status: `Draft`

## Objective

Compile a validated authoring document and explicit node registry into an immutable logical compiled program.

## Depends on

- `P1-006`
- `P1-007`
- `P1-008`

## Allowed changes

- `Authoring/Compilation/`
- `Tests/Editor/Compilation/`
- `Tests/Fixtures/Trees/Compilation/`

## Forbidden changes

- Runtime execution, native optimization, source generation, binary loading, or compiling invalid input by repair.

## Deliverables

- Deterministic node indexing, ordered child table, config packing, memory layout, blackboard layout, hashes, capabilities, and debug map.
- Compilation result containing program or structured diagnostics, never both success and errors.

## Acceptance criteria

- Equivalent canonical input produces byte-equivalent logical content and hashes.
- Registry/version/policy changes affect recorded hashes as specified.
- Overflow, alignment, collision, and unsupported-capability cases fail deterministically.

## Required verification

- Focused compiler tests and golden compiled-program fixtures.
