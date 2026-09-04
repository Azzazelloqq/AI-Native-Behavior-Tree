# P7-027 production Play-mode host and live visual debugger — evidence

## Result

Done. Applies `ADR-P7-010` to production — the single most-repeated disclosed gap across the whole
project (`P3-009`, `P3-010`, `P3-011`, `P6-008`, `P6-012` all independently found "no production
component drives a compiled tree during real Play mode") — plus the owner's own explicit ask for
live, animated visual debugging.

## The real dispatch mechanism, resolved before implementation

`ADR-P7-010`'s own text hedged: "driving `SchedulingPolicyDriver`'s (**or a promoted equivalent's**)
`Immediate`/`Budgeted` methods directly." Reading `Runtime/Scheduling/SchedulingPolicyDriver.cs` in
full found why: every leaf's Tick status is supplied by the *caller*, pre-computed, via a plain
`NodeStatus[] leafStatusByRuntimeIndex` array (its own doc comment: "this driver never interprets a
node's type ID"). That is correct for controlled benchmark scenarios scripted in advance; it cannot
drive a tree whose leaves compute their own real outcome. Put to the owner rather than deferred: the
owner asked to resolve the real dispatch question now.

Resolution, read directly from the engine's own primitives, not invented:
`SchedulingPolicyDriver.TryRunImmediate`'s own loop is a thin wrapper over two already-`internal`
-accessible `NativeLifecycleMachineV1` primitives — `TryAdvance` (returns a step, e.g.
`DispatchRequired` naming which node needs a real Tick) and `TryCompleteDispatch` (hands the machine
that Tick's real result). `SchedulingPolicyDriver.TryHandleStep`'s own body is exactly `status =
leafStatusByRuntimeIndex[step.NodeIndex]; machine.TryCompleteDispatch(...)` — trivially reproducible
with a real, on-demand status instead. The real per-project leaf dispatch table
(`GenericNativeDispatchTranslatorV1`, generated code, `P7-009`) naturally lives outside `AIBT.Runtime`
— exactly matching the ADR's own reasoning that the host itself must stay `AIBT.Runtime`-only. Clean
boundary: the host accepts a real-dispatch delegate injected at construction time, resolved by
whatever project-level code builds the host — the host never references `AIBT.Authoring`.

## Implementation

- **`Runtime/Integration/ProductionTreeHost.cs`** (new, `AIBT.Runtime`, mirrors `Runtime/Integration/
  Snapshots/`'s own precedent as the sub-area home): a `MonoBehaviour` per `ADR-P7-010`'s shape (one
  per tree instance, `Immediate`/`Budgeted` scope only, `Update()` not `FixedUpdate()`, host-owned
  `NativeTraceChannelOwnerV1`).
  - `TryBootstrap(CompiledProgram, DispatchLeaf, NativeTraceChannelCapacityV1, out
    NativeRuntimeFailureV1)` builds one `SchedulingAgent` via `SchedulingPolicyDriver
    .TryCreateAgents(..., agentCount: 1, ...)` — reuses the exact, already-tested construction/
    disposal logic, does not duplicate it.
  - A real compile error (`CS0051: Inconsistent accessibility`) was found and fixed during
    implementation, not predicted at planning time: `NativeLifecycleNodeKindV1` is itself `internal`,
    so it cannot appear as a public method's parameter type. Resolved by deriving `nodeKinds`
    internally via `NativeHotReloadInstance.ClassifyKind` (an existing, already-`internal` helper in
    the same assembly, already used by `NativeHotReloadInstance.TryBuild` for the identical purpose)
    rather than requiring the caller to supply it — a cleaner public API as a side effect, not just a
    workaround.
  - `Update()` drives `TryAdvance`/`TryCompleteDispatch` directly in a loop mirroring
    `SchedulingPolicyDriver.TryHandleStep`'s own exact per-step handling: the injected dispatch
    delegate is called only for real `Tick`-phase dispatches; every other phase (`Enter`/`Exit`/
    `Abort`) is answered with `NodeStatus.Running`, matching `TryHandleStep`'s own existing behavior
    exactly, not a new invented rule. Additively drives `ADR-P6-015`'s own `NativeTraceRecorderV1` at
    the same call sites `SchedulingPolicyDriver`'s own recorder overload already uses (`P7-007`'s
    already-accepted "additive hook" rule) — the recorder itself was not touched or forked.
  - `OnDestroy()` disposes the trace channel and agent; proven idempotent under double invocation
    (both the direct-reflection test call and a real `GameObject` teardown can call it safely).
- **`Editor/Trace/TraceTimelineWindow.cs`**: `OnEnable`/`OnDisable` subscribe/unsubscribe an
  `EditorApplication.update` handler. While a session is attached and the Editor is actually in Play
  mode, it auto-`Refresh()`es at up to ~10Hz (throttled) — this is what makes highlighting live
  instead of requiring a manual "Refresh" click, satisfying the card's own acceptance criterion
  literally. The same tick also drives `AdvanceHighlightAnimation`, which fades each graphed node's
  border alpha toward its current highlight target (`Mathf.MoveTowards`, ~200ms full fade) instead of
  `ApplyHighlight`'s previous instant flat color/width swap — the owner's own explicit "красивые
  анимации" ask, scoped to a color/width fade as the card's own disclosed first-pass minimum.

## Live verification (Unity MCP, real Play mode — not a substitute)

1. Entered real Play mode (`manage_editor` `play`), confirmed via `EditorApplication.isPlaying`.
2. Built a real `CompiledProgram` mirroring `Tests/Runtime/Benchmarking/SchedulingPolicyDriverTests`'s
   own already-proven minimal single-generated-leaf fixture (one `aibt.core.memory-sequence` root,
   one `test.leaf` `GeneratedLeaf` child) — duplicated locally per this codebase's own established
   small-fixture-duplication precedent, not a new invented shape.
3. `ProductionTreeHost.TryBootstrap` on a real scene `GameObject`, with a real on-demand dispatch
   delegate (not a pre-supplied array) returning `Running` — succeeded (`bootstrap=True
   failure=None`).
4. Confirmed Unity Editor Play-mode's own known unfocused-throttling (already disclosed by
   `ADR-P7-010`'s own spike, finding 2 — `Update()` doesn't fire while the Editor window isn't
   focused/repainting): `TotalUpdates` stayed `0` until manually pumped via reflection, the same
   workaround the spike itself needed for a fast MCP round-trip proof. The mechanism ticked is real,
   live, unmodified production `Update()` code — only the pumping cadence was worked around.
5. Opened `TraceTimelineWindow`, attached a real `NativeExecutionDebuggerSession` to the host's own
   `TraceChannelOwner` (unmodified `Attach`, per the ADR's own already-proven shape), called
   `LoadGraphContext` with a real `TreeDocument`/registry/`CompiledProgram` matching the fixture —
   `session.IsAttached=True`, `model.steps=17` on first read (real trace records, not synthetic).
6. Ticked the host 10 more times via reflection, then re-read the window's `CurrentModel` **without
   calling `Refresh()` explicitly this round** — `model.steps=47`. This is the load-bearing proof:
   the step count grew on its own, meaning the `EditorApplication.update`-driven auto-refresh
   subscription genuinely fired and re-read the channel live, not merely that `Refresh()` itself
   works when clicked.
7. Read `_highlightAlphaByNode` via reflection: `root alpha=1`, `leaf alpha=1` — both continuously-
   active nodes correctly reached the animation's own steady-state target.
8. Exited Play mode (`manage_editor` `stop`); console cleared and re-checked immediately after —
   zero new leak diagnostics from this teardown, consistent with the EditMode test's own already-
   passing `OnDestroy_DisposesTheTraceChannelAndTheAgentLeakFree` proof.

No visual screenshot of the animated fade itself was captured this session — the proof above is
programmatic (reading real internal state through a live MCP round trip), not a rendered image. A
future session or the owner can capture one directly if a visual record is specifically wanted.

## Test evidence

- `Tests/Runtime/Integration/ProductionTreeHostTests.cs` (new, 4 tests, all passing):
  `TryBootstrap_RealProgram_SucceedsAndOwnsATraceChannel`, `Update_RealDispatchDelegate
  _DrivesTheLeafOnDemandNotFromAPreSuppliedArray` (confirms the dispatch delegate is called exactly
  once with the real leaf's node index, not from a pre-supplied array), `Update_RunningThenSuccess
  _MatchesSchedulingPolicyDriverTests_OwnAlreadyProvenTwoTickPattern` (cross-checks against the
  existing benchmark test's own already-proven two-tick Running-then-Success sequence),
  `OnDestroy_DisposesTheTraceChannelAndTheAgentLeakFree`.
- `Documentation~/generated/api-reference-runtime.md` regenerated via the real `AIBT/MCP/Regenerate
  Documentation` menu command (not hand-edited) — 4 new public members
  (`ProductionTreeHost.TryBootstrap`/`TraceChannelOwner`/`LastRootResult`/`TotalUpdates`) now covered;
  `McpDocumentationGeneratorsTests`'s own drift-check re-passed (12/12).

## Verification

- `Verify-Static.ps1` — passed (7 schemas, 134 work items).
- `AIBT.Editor.Tests` (live `run_tests`): **396/396 passed** (392 baseline + 4 new).
- Whole host-project regression (live `run_tests`, no assembly filter, 1649 tests): only the same 3
  pre-existing, unrelated failures already disclosed in `P7-018`'s own evidence (2 known
  `GeneratedArtifactContractTests` host-layout failures, 1 unrelated `LocalSaveSystem` package test)
  — zero regressions attributable to this card.

## Scope and limitations

- `Immediate`/`Budgeted` scheduling policies only, per `ADR-P7-010`'s own decision 3 — a
  population-level coordinator for `BatchedJobsSameFrame`/`PipelinedJobs` remains separate, explicit
  future work.
- A full Standalone-Player proof (as opposed to Editor Play mode) was not exercised, matching the
  ADR's own already-disclosed scope limitation.
- The animation's first-pass scope is a color/width border fade only, as the card's own text
  disclosed as the acceptable minimum — no additional visual effects were attempted.
