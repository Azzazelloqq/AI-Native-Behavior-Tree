# P7-010 production Play-mode host decision evidence

## Result

Done, accepted. `Documentation~/decisions/ADR-P7-010-production-play-mode-host.md` (`AIBT-034`)
decides the shape, assembly placement, initial scheduling-policy scope, attach/trace shape, update
timing, and lifecycle of a future production Play-mode host -- the single most-repeated disclosed
gap in the project (independently found by `P3-009`, `P3-010`, `P3-011`, `P6-008`, and `P6-012`'s own
gate session). No production file under `Runtime/`, `Authoring/`, or `Editor/` was touched, per this
card's own Forbidden-changes clause -- this card decides on paper plus a disposable spike only.

## Decision, summarized

1. Shape: one `MonoBehaviour` per tree instance (a `GameObject` component), not a `ScriptableObject`
   singleton -- argued from Unity's own `Update()`/`OnDestroy()` lifecycle guarantees.
2. Location: `Runtime/Integration/`, inside `AIBT.Runtime` -- never `AIBT.Editor`, never a `*.Tests`
   assembly. Confirmed by a real, reproduced Unity restriction (see below), plus `CompiledProgram`
   being Runtime-owned and the driving primitives being Runtime-internal.
3. Initial scheduling-policy scope: `Immediate`/`Budgeted` only. `BatchedJobsSameFrame`/`PipelinedJobs`
   are disclosed follow-up requiring a separate population-level coordinator component, for a real
   structural reason (`SchedulingPolicyDriver.TryRunBatchedJobsSameFrame` takes a whole agent
   population in one call; one independent per-`GameObject` `Update()` cannot do that alone).
4. Attach/trace shape: the host owns its own `NativeTraceChannelOwnerV1` per instance and exposes it
   as a plain accessor -- proven to need zero changes to `P3-010`'s `NativeExecutionDebuggerSession`
   or `ADR-P6-015`'s expected recorder shape.
5. Update timing: `Update()`, not `FixedUpdate()` -- no documented tie between tree progression and
   the physics timestep exists anywhere in `execution-and-scheduling.md`/`benchmarks.md`.
6. Lifecycle: construct in `Awake()`/an explicit bootstrap call, dispose in `OnDestroy()` -- proven
   leak-free in the spike.

See the ADR for full reasoning per point.

## Real finding: Unity refuses to attach components from Editor-only or Test-flagged assemblies

The first spike attempt placed the prototype `MonoBehaviour` in a `Tests/Editor/`-rooted asmdef.
Both `manage_gameobject`'s `AddComponent` action and a direct `GameObject.AddComponent<T>()` call via
`execute_code` failed, with Unity logging: `"Can't add script behaviour
'ProductionPlayModeHostSpike' because it is an editor script. To attach a script it needs to be
outside the 'Editor' folder."` Isolated one cause at a time across two asmdef revisions:

- An asmdef with `"includePlatforms": ["Editor"]` is refused, independent of any Test-assembly flag.
- An asmdef with `"optionalUnityReferences": ["TestAssemblies"]` (the flag `AIBT.Editor.Tests` itself
  carries) is also refused, independent of the platform restriction.

Only after removing both did `AddComponent` succeed. This is real, load-bearing evidence for the
ADR's assembly-placement decision, not a convenience finding -- it structurally rules out
`AIBT.Editor` and any `*.Tests` assembly as the host's home, regardless of any other architectural
reasoning.

## Spike (`Spikes~/ProductionPlayModeHost/`, archived from `Tests/Editor/Spikes/ProductionPlayModeHostSpike/`, deleted from `Tests/` after verification)

`ProductionPlayModeHostSpike.cs` (`AIBT.Spikes` namespace, its own temporary non-test,
non-Editor-restricted asmdef) is a `MonoBehaviour` that, on `Awake()`, compiles a minimal
always-`Running`-leaf tree through the public `AIBT.Authoring.CanonicalTreeJson`/
`ReferencePreviewDriver` (chosen deliberately so the spike needed zero `AIBT.Runtime`-internal access
-- see the ADR's "Explicitly unverified" section for why this does not stand in for the real native
driving path), and constructs a real, public `NativeTraceChannelOwnerV1`. Every `Update()` it calls
`ReferencePreviewDriver.RunTick()` once and writes a real `UpdateStarted`/`UpdateCompleted` record
pair into its own trace channel through the channel's existing public writer-lease API
(`TryAcquireWriter`/`NativeTraceWriterV1.TryAppend`/`TryReleaseWriter`). `OnDestroy()` disposes the
channel's `Allocator.Persistent` arrays.

Live verification via Unity MCP against the real, running `6000.5.8f1` Editor:

- Real Play mode entered (`manage_editor` `play`, not `-batchmode`).
- The host ticked to completion of 2 frames, then stalled for over 20 seconds of real wall-clock
  time -- the Editor's own player loop throttles heavily while unfocused and not requesting
  continuous repaint. One `SceneView.RepaintAll()`/`EditorApplication.QueuePlayerLoopUpdate()` nudge
  (issued once, live, via `execute_code`) was enough to make it tick continuously and unattended
  afterward, reaching **32,295 real `Update()` calls** with zero console errors by the time the
  debugger-attachment proof ran. This throttling is a real Editor-Play-mode-as-proxy characteristic,
  not a defect in the host shape -- a built Standalone Player has no such throttling (not itself
  re-verified here; disclosed in the ADR).
- Mid-session, while the host was actively ticking, a fresh
  `AIBT.Editor.Debugger.NativeExecutionDebuggerSession` was constructed live, `Attach(host.
  TraceChannelOwner)` called, then `TryReadTrace`: `attachOk=True`, `failureCode=None`,
  `stepHistoryCount=64` (the channel's own ordinary record capacity), `droppedCount=192`,
  `isFaulted=True`. The fault is real and expected: the spike's own `emissionCapacity` (256, sized
  only for this disposable spike, not a host-design number) was exhausted by ~32,000 ticks x 2
  records each, and the channel correctly entered its documented permanently-faulted state. The
  debugger session still read it successfully and reported the fault honestly via the existing,
  unmodified `NativeDebuggerTraceView.IsFaulted` field -- zero special-casing needed on the host or
  reader side for this real failure mode. `comp.TotalUpdates` was identical immediately before and
  after the attach/read call (`32295` both times), confirming the read did not perturb or stall the
  live host (matching `P3-010`'s own non-perturbation claim for its read API).
- Play mode exited cleanly (`manage_editor` `stop`); console showed zero errors afterward, confirming
  `OnDestroy`'s `TraceChannelOwner.TryDispose()` freed the `Allocator.Persistent` arrays with no
  "Native Collection has not been disposed" leak diagnostic.

## Verification

```text
Compilation: clean (0 errors) after two asmdef revisions (see "Real finding" above)
Live Play mode via Unity MCP (manage_editor play/stop): entered and exited cleanly
Live sustained ticking via Unity MCP (execute_code polling comp.TotalUpdates): 2 -> 16,409 -> 26,117
  -> 32,295 real Update() calls across one continuous session, zero errors
Live debugger-attachment proof via Unity MCP (execute_code, P3-010's unmodified
  NativeExecutionDebuggerSession.Attach/TryReadTrace against the host's own live-owned channel):
  attachOk=True, failureCode=None, stepHistoryCount=64, droppedCount=192, isFaulted=True (real,
  capacity-driven, read correctly), updatesBefore=32295, updatesAfter=32295 (no perturbation)
No native memory leak on Play-mode exit (console clean after stop)
Tools~/Verification/Verify-Static.ps1 -- passed (see command output below)
git diff --check -- clean
```

Full detached EditMode regression was attempted (`run_tests`, `EditMode`, no filter) as an
additional, not-card-required sanity check after the spike's own required verification had already
passed. The Unity Editor became unresponsive to the MCP bridge for an extended period during that
run; the owner confirmed the run should not be pursued further and to proceed without it. The stuck
job was cleared (`run_tests` `clear_stuck: true`) and the Editor returned to a responsive state.
**Disclosed, not run**: the full detached EditMode regression this session attempted is **not
completed** -- this card's own Required verification does not list it (only the disposable spike and
`Verify-Static.ps1` are required), so this does not block acceptance, but it is recorded here rather
than silently omitted.

## Scope and limitations

- This card decided on paper only -- no production file changed. A future, not-yet-numbered
  implementation card builds the real host inside `Runtime/Integration/` per the ADR.
- The spike's own tree-execution driving used the reference-executor backend
  (`ReferencePreviewDriver`), not the native `SchedulingPolicyDriver`/`NativeLifecycleMachineV1` path
  the ADR's actual decision targets -- a deliberate substitution disclosed in the ADR, made only to
  avoid needing `AIBT.Runtime`-internal access from a disposable, non-privileged spike assembly. The
  future implementation card, building inside `AIBT.Runtime` itself, has full internal access and
  must drive the native path directly.
- Real Standalone-Player-build attachment was not exercised, matching every prior Play-mode-adjacent
  card's own disclosed scope (`P3-010`, `P3-011`, `P5-008`).
- Multiple simultaneous per-instance hosts in one scene were not exercised.
- `BatchedJobsSameFrame`/`PipelinedJobs` support requires a separate, not-yet-designed
  population-level coordinator -- explicitly out of this card's scope, per its own Forbidden-changes
  clause permitting a narrowed initial policy set with a stated reason.
