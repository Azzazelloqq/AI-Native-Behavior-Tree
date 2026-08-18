# P3-007 — Layout/semantic isolation proof

Status: `Draft`

## Objective

Prove, as an automated test rather than a review convention, that a layout-only edit never changes the compiled program (`phase3-inputs.md` required item 3).

## Depends on

- `P3-005`.
- `P3-006`.

## Required reading

- `Documentation~/specifications/editor-layout-v1.md`.
- `Documentation~/specifications/compiled-program-v1.md`.
- `AGENTS.md` ("The visual editor must never make semantic behavior depend on node coordinates, colors, groups, or comments.").

## Allowed changes

- `Tests/Editor/Layout/` (isolation test suite).
- `Planning~/Evidence/P3-007/`.

## Forbidden changes

- `Editor`, `Runtime`, or `Authoring` implementation changes to make the test pass; if the invariant does not hold, the bug is reported against the offending card (`P3-005` or the compiler), not patched here by weakening the test.

## Deliverables

- A test that, for a representative fixture tree: compiles the program, applies every `P3-005` manual-organization action plus `P3-004` auto-layout, recompiles, and asserts the compiled program is byte-identical.
- A companion negative case: a genuine `P3-006` semantic edit is asserted to *change* the compiled program, proving the test can actually detect a difference.

## Acceptance criteria

- The positive case (layout-only) passes with zero compiled-program diff across every manual-organization action kind (pin, group, comment, sticky note, reroute) and auto-layout.
- The negative case (semantic edit) fails to be identical, confirming the comparison is not vacuously true.
- The test runs in the standard EditMode suite, not as a manual/opt-in check.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <isolation proof fixture>
```

## Handoff notes

- This card is a hard gate on `P3-013`: the Phase 3 integration gate must re-run this exact test, not merely confirm it exists.
