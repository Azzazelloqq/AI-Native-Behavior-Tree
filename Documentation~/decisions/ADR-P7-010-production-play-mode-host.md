# ADR P7-010: Production Play-mode host

- Status: Accepted 2026-09-01
- Date: 2026-09-01
- Decision ID: AIBT-034

## P7-030 owner-approved implementation addendum (2026-09-04)

The owner approved `Planning~/Evidence/P7-030/implementation-proposal.md` before implementation.
This addendum resolves the previously unspecified host integration choices without changing the
native execution semantics:

- Retain terminal Success/Failure and stop; no implicit restart. Disabling pauses stepping;
  enabling resumes. Destruction drains cancellation/Exit for active work before disposing storage.
  A terminal-pending Exit keeps its terminal reason. Faulted callbacks are not retried during cleanup.
- Use scaled Unity game time by default, or a caller-supplied nonnegative/nondecreasing microsecond
  clock. Freeze clock/update identity while resuming budget-suspended work. At timeScale zero,
  the clock stops advancing but Tick is not automatically suppressed.
- Keep the existing Tick-only bootstrap overload compatible. Add a lifecycle overload taking
  `DispatchLifecycle`, trace capacity, `Func<long>` clock (null means default), and an out failure.
  The immutable `DispatchRequest` exposes node index, callback phase, logical update ID/time and
  actual Exit/Abort reasons. It exposes neither native ownership tokens nor fabricated Burst contexts.
- `StepBudget` is nullable: null selects Immediate, zero executes no steps, a positive value limits
  each Unity frame segment. A resume is the same logical update, not a new eligible update.
  `TotalUpdates` counts logical updates. `LastFailure` retains the failure that stopped the host.
- A full-lifecycle caller adapter executes its own node implementation. Automatic generated-catalog
  workspace construction and population-wide scheduled execution remain outside this host scope.
- Native dispatch results may carry their existing frame/control reasons so the host need not
  duplicate lifecycle state. The recorder may emit existing budget yield/resume event kinds and
  correct Exit/Abort reasons. The host uses Detailed trace level because Lifecycle filters budget
  events, and releases writer leases between frame segments for debugger reads.

Normal disable pauses stepping, not the selected clock; an expired deadline is observed at the next
eligible update. Destruction inside a callback defers storage release until that callback has been
acknowledged. Native VM, compiled format and node ABI contracts remain unchanged.

Implementation detail: beginning an eligible update can already request reactive or timeout
cancellation. Destruction promotes that pending cancellation to a whole-tree `TreeStopped`
request, through an internal opt-in argument on `TryRequestAbort`. Ordinary abort requests
retain their existing preconditions. This avoids failing teardown merely because native
cancellation was already queued; it does not allow updates after terminal completion.

## Context

`P3-009`, `P3-010`, `P3-011`, `P6-008`, and `P6-012`'s own gate session each independently found the
same gap: no production component anywhere in AIBT drives a compiled tree's lifecycle during actual
Unity Play mode. Every prior card worked around it with a self-driven or benchmark-driven substitute
(own-instance preview, own-instance debugger attach, `SchedulingPolicyDriver`-driven benchmarks) and
disclosed the gap rather than building a host, since doing so was outside each of those cards' own
scope. `Planning~/Evidence/P6-GATE/phase7-inputs.md` restates it as blocking for Phase 7: "any Phase 7
claim about hot reload or in-game debugging in a real Play-mode session needs new work, not a wrapper
around something Phase 5 or 6 already built." This card decides, on paper plus a disposable live
spike, the host's shape, its assembly/layer placement, its initial scheduling-policy scope, and how it
satisfies `P3-010`/`P3-011`'s and `P7-007`'s already-accepted "caller-owned session" attachment shape
-- without building any production code.

## Spike evidence (`Spikes~/ProductionPlayModeHost/`, 2026-09-01, this workstation)

A disposable `MonoBehaviour` (`ProductionPlayModeHostSpike`, live in a temporary scene, driven
entirely via Unity MCP against the real, unmodified `6000.5.8f1` Editor) proved the following:

1. **A production Play-mode host cannot live in an Editor-only assembly.** The first spike attempt
   placed the prototype in a `Tests/Editor/`-rooted asmdef. Unity refused `AddComponent` both through
   `manage_gameobject` and through direct `GameObject.AddComponent<T>()` via `execute_code`, logging:
   `"Can't add script behaviour 'ProductionPlayModeHostSpike' because it is an editor script. To
   attach a script it needs to be outside the 'Editor' folder."` This reproduced identically for two
   different causes, isolated one at a time: (a) an asmdef with `"includePlatforms": ["Editor"]`, and
   independently (b) an asmdef carrying `"optionalUnityReferences": ["TestAssemblies"]` (which is what
   `AIBT.Editor.Tests` itself uses). Removing the platform restriction and moving off the
   Test-assembly-flagged asmdef both were independently necessary before `AddComponent` succeeded.
   This is real, load-bearing evidence, not a convenience finding: the host **must** compile for
   Player platforms and must not sit in a Test-flagged assembly, which rules out `AIBT.Editor` (and
   any `*.Tests` assembly) as its home regardless of any other reasoning below.
2. **Real, sustained per-frame ticking across real Play-mode frames.** Once attachment succeeded, the
   component's `Update()` drove one compiled tree instance every frame. The Unity Editor's own player
   loop throttles heavily while unfocused and not requesting continuous repaint (observed: stuck at 2
   ticks for over 20 seconds of real wall-clock time until a `SceneView.RepaintAll()`/
   `EditorApplication.QueuePlayerLoopUpdate()` nudge was issued once) -- a real Editor-Play-mode
   proxy characteristic, not a defect in the host design itself; a real Player build has no such
   throttling. After the nudge, the host ticked continuously and unattended, reaching over 32,000 real
   `Update()` calls with zero errors.
3. **A host-owned trace channel accepts `P3-010`'s and `P7-007`'s expected attach shape with zero API
   changes.** The host constructed its own `NativeTraceChannelOwnerV1` (public, `Allocator.Persistent`)
   and wrote real `UpdateStarted`/`UpdateCompleted` records into it every tick through the channel's
   existing public writer-lease API. Mid-session, while the host was actively running,
   `AIBT.Editor.Debugger.NativeExecutionDebuggerSession.Attach(host.TraceChannelOwner)` followed by
   `TryReadTrace` succeeded (`failureCode=None`) with zero changes to either type -- confirming the
   exact "whatever owns a running native pass hands this session its `NativeTraceChannelOwnerV1`
   reference directly" contract `P3-010`'s own evidence already documented as the expected shape for a
   future host. A second, unplanned but informative result: after ~32,000 ticks the channel's fixed
   `emissionCapacity` (256, sized only for this spike, not a host-design number) was exhausted and the
   channel entered its documented permanently-faulted state (`_control[2]`); the debugger session still
   read it successfully and correctly reported `IsFaulted=true` via the existing, unmodified
   `NativeDebuggerTraceView.IsFaulted` field -- proving the read side already handles this real failure
   mode gracefully, with no host-side special-casing required.
4. **No native memory leak on scene/Play-mode teardown.** `OnDestroy()` disposed the owned
   `NativeTraceChannelOwnerV1`'s `Allocator.Persistent` arrays; exiting Play mode produced no
   "Native Collection has not been disposed" diagnostic.
5. **The spike's own tree-execution path is `AIBT.Authoring.ReferencePreviewDriver` (reference
   executor), not the native `SchedulingPolicyDriver`/`NativeLifecycleMachineV1` path this ADR's
   decision actually targets** -- see "Explicitly unverified" below for why and what it does not
   invalidate.

## Decision

1. **Shape: one `MonoBehaviour` per tree instance, attached to a `GameObject` the game author (or
   spawning code) owns -- not a `ScriptableObject`-driven singleton.** Argued from Unity's own
   lifecycle guarantees, not convenience: only a `MonoBehaviour` receives `Update()`/`OnDestroy()`
   tied to the owning `GameObject`'s and scene's lifecycle; a `ScriptableObject` has no per-frame
   callback without a separate player-loop injection this design does not need to invent. This also
   matches `architecture.md`'s own data-ownership row ("Agent state | Mutable per execution | Runtime |
   memory only") and every existing per-agent construction in this codebase
   (`SchedulingPolicyDriver.TryCreateAgents`, `SchedulingAgent`) -- one host instance per tree
   instance, not a shared manager owning all state.
2. **Location: `Runtime/Integration/`, inside `AIBT.Runtime` -- never `AIBT.Editor`, never a
   `*.Tests` assembly.** Three independent reasons converge on the same answer:
   - The spike's own reproduced Editor-script/Test-assembly `AddComponent` refusal (finding 1 above)
     structurally rules out `AIBT.Editor` and `*.Tests` assemblies, not just as a style preference.
   - `CompiledProgram` -- what the host actually consumes -- is itself a `Runtime`-owned model. A
     shipped game needs only `AIBT.Runtime` plus an already-compiled program to execute a tree; it
     does not need `AIBT.Authoring`'s JSON/validation/compilation machinery at all. A host living in
     `AIBT.Runtime` keeps that minimal-footprint property intact.
   - The actual driving primitives (`NativeLifecycleMachineV1`, `SchedulingPolicyDriver`-equivalent
     step handling) are `internal` to `AIBT.Runtime`. A host built inside that assembly gets natural
     internal access with zero new `InternalsVisibleTo` grants -- unlike the `AIBT.Authoring`-crossing
     public-facade pattern `P3-009`/`P5-008`/`P6-013` established for **Editor-only** tooling, which
     is the wrong precedent here since this component must ship inside real Player builds.
   - `Runtime/Integration/` already exists (`Runtime/Integration/Snapshots/`, from `P2-009`) as exactly
     the sub-area `architecture.md`'s own "Integration boundary" section describes: "Jobs never call
     scene objects or arbitrary Unity APIs. Integrations build job-safe snapshots before execution and
     apply emitted commands afterward." A per-frame host is precisely that boundary component.
3. **Initial scheduling-policy scope: `Immediate` and `Budgeted` only. `BatchedJobsSameFrame` and
   `PipelinedJobs` are explicit, disclosed follow-up, for a real structural reason, not convenience.**
   Both batch-level policies require coordinating many tree instances into one
   `NativeBatchedLifecycleOwnerV1`/pipelined dispatch in a single call
   (`SchedulingPolicyDriver.TryRunBatchedJobsSameFrame`'s own signature takes a whole
   `SchedulingAgent[]` population, not one agent). A single per-`GameObject` `MonoBehaviour` ticking
   independently in its own `Update()` cannot itself perform that population-wide batch dispatch --
   it would need a second, separate population-level coordinator component (a manager gathering
   every registered per-instance host once per frame) that this card does not design. A future card
   may add that coordinator; until then, a host built per this ADR honestly supports only the two
   policies that are naturally per-instance.
4. **Attach/trace shape: the host owns (constructs) its own `NativeTraceChannelOwnerV1` per tree
   instance and exposes it as a plain accessor -- exactly the "caller-owned session" shape
   `NativeExecutionDebuggerSession.Attach` and `ADR-P6-015`'s external recorder already expect.**
   Proven directly by finding 3 above: zero changes needed to either `P3-010`'s or `P6-015`'s already
   -accepted public types. A future implementation card wires `P6-015`'s real recorder (`P7-007`'s own
   scope) into the host's drive loop as an additive hook, per `ADR-P6-015` item 1 ("Any production
   driver that calls `TryBeginUpdate`/`TryAdvance`/`TryCompleteDispatch` in a loop... additively calls
   the recorder").
5. **Update timing: `Update()` (once per rendered frame), not `FixedUpdate()`.** `conventions.md`
   defines "Update" as "one request to progress a tree instance" with no tie to a fixed physics
   timestep anywhere in `execution-and-scheduling.md`/`benchmarks.md`. Tying tree progression to
   `FixedUpdate()` would silently couple behavior-tree cadence to the physics timestep for no
   documented reason. A future card may add an explicit cadence override (mirroring
   `NativeAutoSelectionV1`'s existing `UpdateCadence` override field) -- not decided here.
6. **Lifecycle: construct in `Awake()` (or an explicit `Bootstrap` call for callers that need to
   supply the compiled program after instantiation), dispose deterministically in `OnDestroy()`.**
   Proven leak-free in the spike (finding 4). Domain reload mid-Play-session and Standalone-Player
   process-exit teardown are not exercised here (see "Explicitly unverified").

## Acceptance criteria mapped

- The spike ran in real Play mode (not `-batchmode`), observed live via Unity MCP against the open
  Editor: confirmed -- over 32,000 real `Update()` calls in one continuous live session.
- The ADR states plainly which scheduling policies the initial host design supports
  (`Immediate`/`Budgeted`) and which are explicit follow-up (`BatchedJobsSameFrame`/`PipelinedJobs`),
  with a real structural reason, not a convenience shortcut.
- `P3-010`/`P3-011`'s own already-accepted public APIs required zero changes to attach to the new
  host's shape: confirmed -- `NativeExecutionDebuggerSession.Attach`/`TryReadTrace` were called
  completely unmodified against a live, host-owned channel, including a real fault-state read.

## Consequences

- A future, not-yet-numbered implementation card builds the real host type inside
  `Runtime/Integration/` per this ADR's shape, driving `SchedulingPolicyDriver`'s (or a promoted
  equivalent's) `Immediate`/`Budgeted` methods directly -- with full internal access, not the
  reflection/public-facade workaround this disposable spike needed.
- `P7-007` (native trace production-wiring implementation) can proceed independently, since this ADR
  confirms the host will satisfy `ADR-P6-015`'s "caller-owned session" shape without requiring any
  change to that ADR's own decision.
- A second, separate future card (disclosed, not scoped here) is needed to design the population-level
  coordinator that would let `BatchedJobsSameFrame`/`PipelinedJobs` run under a production host.
- This ADR grants no new public API to `AIBT.Runtime` itself -- it decides where and in what shape a
  future implementation card should build one.

## Explicitly unverified (stated, not generalized)

- **The spike's own tree-execution driving used the public `AIBT.Authoring.ReferencePreviewDriver`
  (reference-executor backend), not the native `SchedulingPolicyDriver`/`NativeLifecycleMachineV1`
  path this ADR's decision actually targets.** This was a deliberate, disclosed substitution: it let
  the spike's `MonoBehaviour` need zero `AIBT.Runtime`-internal access and stay clear of the
  Test-assembly/Editor-script `AddComponent` restriction (finding 1) without touching any forbidden
  production file. A future implementation card building the real host **inside** `AIBT.Runtime` has
  full internal access by construction and must drive `SchedulingPolicyDriver`'s native path directly
  -- it must not re-derive or keep this reference-backend workaround.
- Real Standalone-Player-build attachment (outside the Editor) was not exercised, matching every prior
  Play-mode-adjacent card's own disclosed scope (`P3-010`, `P3-011`, `P5-008`): Editor Play mode is an
  accepted proxy for this card's own spike; a full Player-build proof is the future implementation
  card's job.
- Multiple simultaneous tree-instance hosts in one scene were not exercised -- only one
  `GameObject`/component ran in the spike.
- Domain reload occurring mid-Play-session, and process-exit teardown in a built Player (as opposed to
  exiting Editor Play mode, which was exercised and proven leak-free), were not exercised.
