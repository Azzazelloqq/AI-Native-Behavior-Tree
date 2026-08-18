# P3-009 — Editor preview via reference oracle

Status: `Draft`

## Objective

Let the editor step/preview a tree using the Phase 1 managed reference executor, so in-editor stepping semantics cannot drift from the accepted oracle (`phase3-inputs.md` required item 5).

## Depends on

- `P3-006`.

## Required reading

- `Documentation~/specifications/execution-semantics-v1.md`.
- `Documentation~/specifications/update-phases-v1.md`.
- `Planning~/Evidence/P1-GATE/README.md` (what the managed reference executor is accepted to do).

## Allowed changes

- `Assets/AIBT/Editor/Preview/` (new).
- `Tests/Editor/Preview/` fixtures.
- `Planning~/Evidence/P3-009/`.

## Forbidden changes

- A second, editor-local execution semantics implementation; this card only drives and observes the existing managed reference executor.
- Any change to the reference executor's own behavior; it is consumed as-is.

## Deliverables

- Step/play/pause/breakpoint controls in the editor driving the reference executor against the currently-edited tree.
- Live highlighting of the active node and blackboard/command state per step, sourced from the executor's existing observable state, not a parallel model.

## Acceptance criteria

- Stepping the same tree through the in-editor preview and through the existing behavior-case runner (headless) produces the identical step sequence and terminal status.
- Edits made via `P3-006` are reflected in the next preview run without requiring an editor restart.
- Preview never mutates `.aibt.json` or `.aibt.layout.json`.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <preview parity fixture>
step-sequence comparison against the headless behavior-case runner
```

## Handoff notes

- None beyond the dependency on `P3-006`'s stable edit surface.
