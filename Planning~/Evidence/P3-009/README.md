# P3-009 editor preview via reference oracle evidence

## Result

- `Authoring/Execution/ReferencePreviewDriver.cs` (new, `AIBT.Authoring`, public): drives the
  accepted Phase 1 `ReferenceExecutionMachine` (internal to `AIBT.Runtime`) as-is. Every state
  transition is a direct call into the machine's existing `BeginUpdate`/`AdvanceOneStep`/`Restart`
  API (`ReferenceExecutionMachine.cs`); this type adds no execution semantics of its own, only a
  public-shaped translation (`ReferencePreviewContracts.cs`) of the internal
  envelope/trace/inspection contracts so `AIBT.Editor` (no internals visibility into
  `AIBT.Runtime`) can consume them. `RunTick` opens a tick and loops the machine's own
  `AdvanceOneStep` primitive to a boundary or a caller-supplied breakpoint node, leaving the tick
  open for the next call to resume exactly where it stopped.
- `Authoring/Execution/ReferencePreviewFixtureEnvironment.cs` (new, `AIBT.Authoring`, internal):
  the fixed node-behavior set the driver compiles/executes against --
  `NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures()` plus
  `ReferenceLeafRegistry.CreatePhase1Fixtures()` and the sibling `CreatePhase1BuiltIns()`
  composite/decorator/parallel registries, i.e. the *same, already-shipped* Phase 1 fixture set the
  headless behavior-case runner already exercises (see "Scope and limitations" below).
- `Editor/Preview/BehaviorTreePreviewWindow.cs` (new, `AIBT.Editor`): an `EditorWindow` (menu
  `AIBT/Behavior Tree Preview`) with Load/Step/Run Tick/Play/Pause/Restart controls and a
  right-click-to-toggle breakpoint on each node, driving `ReferencePreviewDriver` and rendering
  through a private `BehaviorTreeGraphView` instance (P3-003's read-only adapter, consumed as a
  black box, not modified) with active nodes highlighted via style borders and a blackboard panel.
  `LoadDocument(TreeDocument, sourceId)` lets an already-open window preview a different in-memory
  document (e.g. the result of a `SemanticEditTransaction`) without touching disk or restarting the
  editor.
- 3 automated tests, all passing:
  - `ReferencePreviewParityTests.PreviewAndHeadlessOracleProduceIdenticalStepSequenceAcrossTicks` --
    constructs a raw `ReferenceExecutionMachine` the same way
    `Tests/Integration/SemanticSlice/ReferenceBehaviorCaseAdapter.cs` does (the "headless oracle"),
    and separately drives the same tree through `ReferencePreviewDriver`. Across 2 ticks, both
    sides' translated trace-event sequences (kind/node/status/source-node) and `RootResult` are
    asserted identical; `TerminalResult` is asserted identical at the end.
  - `ReferencePreviewParityTests.EditViaSemanticEditOperationsIsReflectedInNextPreviewRunWithoutEditorRestart` --
    applies `SemanticEditOperations.RemoveNode` (P3-006's own edit surface) to the fixture tree and
    creates a second driver over the result in the same test/process; the edit (removing the
    always-`Running` leaf) is reflected immediately (root result becomes `Success`).
  - `BehaviorTreePreviewWindowTests.LoadingAndSwitchingDocumentsNeverMutatesTheSourceFileOrCreatesALayoutFile` --
    loads the fixture, then reloads the same open window with an edited in-memory document; the
    source file's bytes are asserted unchanged and no `.layout.json` sibling file is created.
- Live interactive verification (this environment's headless batchmode is unusable while the
  interactive Editor holds `Temp/UnityLockfile` for the MCP bridge, per this session's operating
  constraint -- see "Verification commands" below): opened `BehaviorTreePreviewWindow` in the
  running `6000.5.8f1` Editor via `unityMCP.execute_code`, loaded the fixture tree
  (`success-then-running.aibt.json`), and stepped/ran it live:
  - After `Load` the `BehaviorTreeGraphView` rendered exactly 3 nodes (matches the fixture).
  - `Step` once opened a tick; `Step` again entered/ticked the root, active-node set became
    `[root]`.
  - `Run Tick` (drains to the tick boundary) produced active set `[root, b]` (node `a` entered,
    ticked Success, exited; node `b` entered, ticked Running, stayed active) and `HasOpenTick =
    False`, matching `ReferenceExecutionMachine.Tick`'s Running branch (`Progress = Waiting`, no
    root result yet).
  - A second `Run Tick` kept the same active set `[root, b]` unchanged -- the memory-sequence
    resumed directly at node `b` without re-entering `a`, proving state persists correctly across
    ticks through the live window, not just in an isolated unit test.
  - No console errors or exceptions were produced by this session.

## Decision

No new decision beyond the owner's explicit 2026-08-19 choice (via `AskUserQuestion`) of how to
cross the `AIBT.Editor` / `AIBT.Runtime` internals-visibility boundary this card's objective
requires: a public facade type added to `AIBT.Authoring` (which already has `InternalsVisibleTo`
from `AIBT.Runtime`, and which `AIBT.Editor` already references), rather than widening
`Runtime/AssemblyInfo.cs`'s `InternalsVisibleTo` to include `AIBT.Editor` directly. This mirrors
the existing `ReferenceCompiler` pattern (also a public `AIBT.Authoring` facade bridging Runtime
internals) and required zero new assembly references or `InternalsVisibleTo` grants.

This is a deliberate deviation from the card's stated `Allowed changes` (`Editor/Preview/`,
`Tests/Editor/Preview/`, `Planning~/Evidence/P3-009/`): `Authoring/Execution/` was also touched.
Per `MASTER_PLAN.md`'s source-priority order, explicit current user instruction outranks the
assigned work-item card, and the user was asked and explicitly chose this option before any of
`Authoring/Execution/` was written. `Planning~/work-items.json`'s `P3-009.owns` was updated to
include `Authoring/Execution/` accordingly.

## Scope and limitations

- **Fixed, fixture-only node-behavior set.** `ReferencePreviewDriver` always compiles/executes
  against the same Phase 1 fixture/built-in set (`ReferencePreviewFixtureEnvironment.cs`): built-in
  composites/decorators/parallel plus the `aibt.test.success`/`aibt.test.failure`/
  `aibt.test.running` constant leaves. This is not an invented weakening -- it is the *only*
  executable leaf-behavior set that exists anywhere in the AIBT repository today.
  `ReferenceLeafRegistry.CreatePhase1Fixtures()` (Runtime) and `NodeRegistryBuilder.AddTestFixtures()`
  (Authoring) are themselves already-shipped Phase 1 infrastructure, not new production code, and
  they are exactly what the headless behavior-case runner already exercises. AIBT ships no
  production per-project leaf-behavior registration mechanism yet (see
  `Tests/Integration/SemanticSlice/SemanticSliceNodeContracts.cs` for how even the Phase 1/2
  integration tests build their own one-off test-fixture leaves). A tree using any node type
  outside this set fails compilation with a normal diagnostic surfaced in the preview window's
  status label, not a crash. Extending preview to arbitrary project-authored leaf behavior needs
  its own accepted decision (a production leaf-registration API), out of this card's scope.
- **No `Editor/Graph/` live wiring.** `BehaviorTreePreviewWindow` hosts its own private
  `BehaviorTreeGraphView` instance; it does not attach to (or read from) an already-open
  `BehaviorTreeGraphWindow` (P3-003), matching the same "not wired into the live graph editor"
  scope boundary already disclosed by every `P3-004`--`P3-008` evidence file. `LoadDocument` lets
  the *same preview window* re-preview an edited document without an editor restart, which is what
  the acceptance criterion actually requires; it does not mean the P3-006 editing UI and this
  preview window are the same live surface yet.
- **"Step" is the machine's one atomic action, not one visible tree-wide tick.** Per
  `Documentation~/specifications/reference-executor-machine-v1.md`, one discrete step is a single
  Enter/Tick/Abort-transition/Exit/observer-evaluation. The preview window's `Step` button maps
  directly to this (`ReferencePreviewDriver.StepAtomic`, after an implicit first `BeginTick`); `Run
  Tick`/`Play` drain to a full tick boundary. This was a deliberate choice under
  `DECISION_BOUNDARIES.md`'s "missing detail, every compliant choice observably equivalent, choose
  the simplest correct option" rule -- both granularities are exposed, and neither reimplements the
  machine's own stepping logic (this card's forbidden-changes clause).
- **Pre-existing, unrelated test failures observed in the same full-suite run**, not touched or
  caused by this card's change: `AIBT.Tests.CodeGen.Generation.GeneratedArtifactContractTests`
  (`EmittedMetadata_MaterializesConsumerDescriptorAndCompilesCanonicalV2Artifact`,
  `RegisteredCatalog_DrivesJsonValidationAndCompiledDefaultCodec` -- "the CodeGen test assembly
  must belong to the AIBT package") and `LocalSaveSystem.Tests.SaveStoreTests.SaveStore_AutoSave_WritesToDisk`.
  None reference `Editor/Preview/`, `Authoring/Execution/`, or any file this card touched.

See `verification-results.json` for exact commands and results.
