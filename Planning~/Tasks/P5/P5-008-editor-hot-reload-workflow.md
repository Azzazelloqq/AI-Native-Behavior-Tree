# P5-008 — Editor hot-reload workflow

Status: `Draft`

## Objective

Surface hot reload as an explicit, explained Editor workflow: detect a
compiled-program change against a live instance, show the user what
`P5-003`'s classifier found and which of `P5-004`/`P5-005`/`P5-006`'s
strategies ran, per `Documentation~/architecture.md`'s assignment of
hot-reload orchestration to the Editor.

## Depends on

- `P5-004`, `P5-005`, `P5-006` (every strategy this workflow can trigger).
- `P5-007` (scheduler-interaction behavior this workflow must accurately
  describe to the user).

## Required reading

- `Documentation~/hot-reload.md`'s "Editor workflows" section.
- `Documentation~/architecture.md`'s Editor responsibility list.
- `Planning~/Evidence/P3-009/`, `Planning~/Evidence/P3-010/`,
  `Planning~/Evidence/P3-011/` (the existing preview/debugger/trace-view
  pattern this workflow's UI should follow, including their disclosed
  "own private view instance, not wired into `Editor/Graph/`'s live window"
  limitation -- this card inherits the same limitation unless it explicitly
  changes it, which is out of this card's own scope).

## Allowed changes

- `Editor/HotReload/` (new).
- `Tests/Editor/HotReload/` (new).

## Forbidden changes

- Wiring `Editor/Graph/`'s live window to this or any other Phase 3/4/5
  surface -- out of scope, same disclosed boundary every `P3-004` through
  `P3-011` card already lived with.
- Any change to `P5-001` through `P5-007`'s decided mechanisms -- this card
  presents their results, it does not alter them.

## Deliverables

- A workflow that detects a live instance's compiled program has changed
  (via `P3-007`'s `CompiledContentHash` signal), runs `P5-003`'s classifier,
  and presents the classification, chosen strategy, and outcome (including
  `P5-006`'s per-node migrated-vs-restarted split when relevant) to the
  user before or as the reload happens -- not silently in the background.
- A way for the user to see *why* a given strategy was chosen, mirroring
  `execution-and-scheduling.md`'s scheduler-explainability discipline.
- Graceful, explicit handling of a reload the user did not expect to
  succeed (e.g., an incompatible change reported as such, not silently
  downgraded to "worked fine").

## Acceptance criteria

- Every reload this workflow triggers shows the user the actual strategy
  used and the actual classification result, verified by a live interactive
  Editor session via Unity MCP (the same verification technique `P3-009`
  through `P3-012` used to close the "was this ever actually driven
  interactively" gap), not only headless assertions.
- A reload the classifier marked incompatible is never silently presented
  as a successful migration.
- The workflow does not itself become a second, competing hot-reload trigger
  path alongside a hypothetical future automatic file-watch trigger -- this
  card scopes to a single, explicit, user-visible trigger; anything more
  automatic is later work, not assumed here.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <editor hot-reload workflow fixture>
live interactive Editor session via Unity MCP driving at least one reload of each strategy
```

## Handoff notes

- `P5-010` (the Phase 5 gate) re-runs this card's live-interactive proof
  against the final committed snapshot, the same way `P3-013` re-ran
  `P3-007`'s isolation proof.
