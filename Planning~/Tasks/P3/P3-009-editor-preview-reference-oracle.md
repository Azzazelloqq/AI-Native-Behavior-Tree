# P3-009 — Editor preview via reference oracle

Status: `Done`

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

## Outcome

- Crossing the `AIBT.Editor` / `AIBT.Runtime` internals-visibility boundary this card's objective
  requires was escalated to the owner (`AskUserQuestion`, 2026-08-19) rather than decided silently,
  per `DECISION_BOUNDARIES.md`. Accepted answer: a new public facade,
  `Authoring/Execution/ReferencePreviewDriver.cs` (+ `ReferencePreviewContracts.cs` and the
  internal `ReferencePreviewFixtureEnvironment.cs`), added to `AIBT.Authoring` (which already has
  `InternalsVisibleTo` from `AIBT.Runtime`, and which `AIBT.Editor` already references) — mirroring
  the existing `ReferenceCompiler` pattern, zero new assembly references or `InternalsVisibleTo`
  grants. This is a deliberate deviation from this card's `Allowed changes` list, made under
  explicit current-user-instruction priority; `work-items.json`'s `P3-009.owns` now also lists
  `Authoring/Execution/`.
- `Editor/Preview/BehaviorTreePreviewWindow.cs`: Load/Step/Run Tick/Play/Pause/Restart controls,
  right-click breakpoints, live active-node highlighting via a private `BehaviorTreeGraphView`
  instance (P3-003's adapter, not modified), and a blackboard panel — driving the facade, which
  drives the real `ReferenceExecutionMachine` via its own public `BeginUpdate`/`AdvanceOneStep`/
  `Restart` API only (no second stepping implementation).
- 3/3 automated tests passing, including a step-sequence-and-terminal-status parity proof against a
  raw `ReferenceExecutionMachine` built the same way the headless behavior-case runner is, plus a
  same-process edit-without-restart proof and a never-mutates-the-source-file proof. Also
  live-verified interactively in the running `6000.5.8f1` Editor via Unity MCP (`execute_code`):
  opened the window, loaded a fixture tree, stepped/ran it, and observed correct multi-tick active-
  node persistence with no console errors.
- **Fixed, fixture-only executable node set** (`aibt.test.success`/`.failure`/`.running` plus
  built-ins) — the only executable leaf-behavior set that exists anywhere in the repository today;
  not an invented weakening. Extending preview to arbitrary project-authored leaves needs its own
  accepted decision.
- **No `Editor/Graph/` UI wiring** — same disclosed pattern as `P3-004` through `P3-008`; this
  window hosts its own graph view rather than attaching to an already-open
  `BehaviorTreeGraphWindow`.
- Full evidence: `Planning~/Evidence/P3-009/README.md`, `verification-results.json`.
