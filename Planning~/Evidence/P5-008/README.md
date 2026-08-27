# P5-008 editor hot-reload workflow evidence

## Result

- `Authoring/HotReload/HotReloadPreviewDriver.cs`, `HotReloadPreviewOutcome.cs` (new, public
  `AIBT.Authoring`): a public facade crossing the `AIBT.Editor`/`AIBT.Runtime` internals-visibility
  boundary, mirroring `P3-009`'s `ReferencePreviewDriver` pattern exactly. Owns no reload semantics
  of its own -- `TryReload` is a direct call into `HotReloadCompatibilityClassifier.Classify`
  (`P5-003`) and `HotReloadStateMigration.Migrate` (`P5-005`/`P5-006`), translated to a public
  shape (`HotReloadPreviewOutcome`) `AIBT.Editor` can read without internals access.
- `Editor/HotReload/HotReloadWorkflowWindow.cs` (new, public `AIBT.Editor`): a real `EditorWindow`
  (`AIBT/Hot Reload Workflow` menu item) with a single explicit trigger -- a "Reload From..." button
  that opens a file picker for the new tree and immediately shows the classification, chosen
  strategy, and migrated/reset/dropped counts. No automatic file-watching or background reload
  exists; this matches this card's own "single, explicit, user-visible trigger" scope requirement.
  Own private view instance, not wired into `Editor/Graph/`'s live window -- the same disclosed
  boundary every `P3-004` through `P3-011` card already lived with.
- `Tests/Editor/HotReload/Preview/HotReloadPreviewDriverTests.cs` (new, 5 tests, all passing):
  null/invalid-argument rejection, a compatible parameter edit migrating every node, an
  incompatible type change reporting `IncompatibleRestart` and forcing `RequiredFullRestart` when
  it hits the root, falling back to full restart when the old instance is active, and rejecting an
  uncompilable reload target cleanly (no outcome, diagnostics surfaced).

## Live interactive proof (all three reload strategies, in the real Editor)

Per this card's own acceptance criterion ("verified by a live interactive Editor session via Unity
MCP... not only headless assertions"), the actual `HotReloadWorkflowWindow` was opened in the
user's live Unity Editor via Unity MCP's `execute_code`, driven through its own public
`LoadFromPath`/`ReloadFromPath` methods (the same methods its own toolbar buttons call), and its
displayed UI labels were read back directly -- not simulated, not asserted against internal state.
Three real fixture files
(`Tests/Editor/HotReload/Preview/Fixtures/{before,after-incompatible-child,after-compatible-reorder}.aibt.json`)
exercise all three strategies in one session:

| Scenario | Real UI output |
| --- | --- |
| Child `b` changes type (`running` -> `failure`) | `Strategy: Subtree restart (b)` -- `Migrated: 2  Reset: 1  Dropped: 0`; per-node verdicts `a: Migrate`, `b: IncompatibleRestart (restart root)`, `root: Migrate` |
| Children reordered, no type/param change | `Strategy: Compatible migration` -- `Migrated: 3  Reset: 0  Dropped: 0` |
| Reload while the old instance is still active (ticked once, 2 active nodes) | `Strategy: Full restart (old instance was still active)` |

A reload the classifier marks incompatible is never presented as a silent success: the subtree-restart
scenario's own outcome text names the restarting node and its category explicitly
(`IncompatibleRestart`), never folded into a blanket "reloaded successfully" message.

## Verification

Live Unity MCP test run: 5/5 passed (`HotReloadPreviewDriverTests`). Live interactive Editor
session: all three strategies, real UI output as tabulated above. Full suite: 1446 tests, same 3
pre-existing unrelated failures as every prior evidence file. `Verify-Static.ps1`: 83 work items,
unchanged. Full detail in `verification-results.json`.

## Scope and limitations

- Reference-executor backend only, matching every hot-reload card so far this phase (per the
  user's own decision after `P5-007`'s native-backend gap was found).
- No stepping/trace/highlighting UI exists in this window (unlike `BehaviorTreePreviewWindow`) --
  this card's own focus is the reload workflow and its explainability, not rebuilding tree
  visualization; `RunOneTick`/`ActiveNodeCount` are the minimum needed to demonstrate an active
  instance falling back to full restart.
- Same fixed Phase 1 fixture/built-in node-behavior set as every other preview/debugger/trace-view
  card (`ReferencePreviewFixtureEnvironment`) -- no production per-project leaf-registration
  mechanism exists yet.
- `Editor/Graph/`'s live window is not wired to this workflow, per this card's own explicit scope
  boundary.
