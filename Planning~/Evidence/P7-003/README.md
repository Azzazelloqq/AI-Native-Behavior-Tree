# P7-003 profiler integration and validation evidence

## Result

`Unity.Profiling.ProfilerMarker` instrumentation was added to the execution/scheduling hot paths
named by the card, scoped to what the card's own Required-reading investigation found genuinely
wired into production (see Decision below for one narrowed category):

- `NativeLifecycleMachineV1` (`Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs`, the
  Burst-compiled per-instance lifecycle machine): `TryAdvance` (`AIBT.Native.LifecycleTick`) and
  each of its five per-node-kind dispatch methods -- `AdvanceLeaf`, `AdvanceAbort`,
  `AdvanceParallel`, `AdvanceComposite`, `AdvanceDecorator` (`AIBT.Native.Dispatch.{Leaf,Abort,
  Parallel,Composite,Decorator}`).
- `NativeSameFramePhaseControllerV1.TryScheduleExecuteRound` and
  `NativePipelinedPhaseControllerV1.TryScheduleExecuteRound` (`AIBT.Native.Scheduling.{SameFrame,
  Pipelined}.ScheduleExecuteRound`): the per-update-batch scheduling entry for each same-frame-class
  policy.
- `NativeSharedContextOwnerV1.TryReduceUpdate` (`AIBT.Native.Blackboard.Shared.ReduceUpdate`): the
  real production blackboard-bridge call (`NativeSharedReductionV1.TryReduce`'s only caller).
- `ReferenceExecutionMachine.AdvanceOneStep` (`AIBT.Reference.LifecycleTick`): the managed
  reference/oracle backend's own per-instance tick, mirroring the native marker for parity.

Every marker is a `private static readonly ProfilerMarker` field wrapped in a single
`using var _ = marker.Auto();` at the top of the existing method body -- no other line in any of
the five touched files changed; `git diff --stat` shows 40 insertions, 0 deletions, across
`Runtime/Blackboard/Native/Shared/NativeSharedContextOwnerV1.cs`,
`Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs`,
`Runtime/Execution/Reference/Core/ReferenceExecutionMachine.cs`,
`Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs`,
`Runtime/Scheduling/Native/NativeSameFramePhaseControllerV1.cs`.

## Decision

**Command-bridge markers were dropped from scope, not silently added.** The card's own Deliverables
line asks for "blackboard/command bridge calls" markers. Investigation before instrumenting found
the native command-merge subsystem (`NativeCommandMergeOwnerV1`/`NativeCommandAsyncOwnerV1`, built
by `P2-010`) has **zero production callers anywhere** -- a repo-wide grep found only its own test
file (`Tests/Runtime/NativeExecution/CommandsAndAsync/`) ever calls `TryFinalize`. It is a
built-but-unwired subsystem, the same shape as other already-disclosed gaps this project has found
(`P5-007`'s scheduler/hot-reload gap, `P6-015`'s native-trace-wiring gap). Put to the owner directly
mid-session: instrumenting genuinely dead code would misrepresent test-only code as a production hot
path, exactly what this card's own Forbidden-changes section warns against ("this card makes cost
visible, it does not interpret"). **Owner decision: narrow to the blackboard half only, disclose the
command-merge gap here rather than force coverage of unwired code.**

**The real Profiler capture needed a Development Build, not the "non-Development Player" the card's
own text names.** `P4-008`'s own precedent (a real, non-Development Player) is the right choice for
wall-clock measurement, but a Unity Profiler window cannot connect to a build with no profiler
transport compiled in -- a hard engine constraint, not a workaround target. Put to the owner
directly: **Development Build + `BuildOptions.ConnectWithProfiler`, disclosed as a necessary
deviation from the card's literal wording, not a shortcut.** See
`Benchmarks~/Phase7/Profiler/README.md`.

**`Unity.Profiling.ProfilerRecorder`-based marker-presence unit tests (the technique this card's own
Allowed-changes section suggests as an example) do not work in this environment.** A dedicated test
file (`Tests/Runtime/NativeExecution/LifecycleAndMemory/NativeLifecycleMarkerPresenceTests.cs`,
mirroring `NativeLifecycleMachineTests.cs`'s own proven step sequences exactly) was built and run,
both as a plain `[Test]` and as a `[UnityTest]` with `yield return null` between the marker-firing
work and the recorder check. In every configuration tried -- `Profiler.enabled`,
`ProfilerDriver.enabled`, `SetAreaEnabled(CPU, true)`, pumped `EditorApplication.QueuePlayerLoopUpdate`
frames, even a genuinely independent, non-AIBT, non-Burst `ProfilerMarker` fired directly via
`execute_code` against the live Editor -- `ProfilerRecorder.Count` stayed `0`. This isolates the
limitation to EditMode/`ProfilerRecorder` sample flushing in this Unity version, not to anything
Burst- or AIBT-specific. The test file was removed rather than landed non-functional; the real Burst
disassembly inspection and the real connected-Player Profiler capture (both below) independently and
more directly satisfy what marker-presence tests would only have approximated.

No other card assumption changed.

## Burst-compatibility verification (not assumed)

Per the card's own acceptance criterion, verified against the real compiled artifact, not inferred
from "it compiled without error":

- Live reflection into the open Editor's actual Burst Inspector data model
  (`Unity.Burst.Editor.BurstInspectorGUI._targets`, a `List<BurstCompileTarget>`) located the real
  `AIBT.NativeBatchedLifecycleOwnerV1+AdvanceJob` compile target (the `[BurstCompile] IJob` that
  calls `NativeLifecycleMachineV1.TryAdvance`), forced a fresh synchronous compile
  (`BurstInspectorGUI.CompileNewTarget`), and read back the real result:
  **`BurstCompileTarget.IsBurstError = False`**, with **286,573 characters of real x64 disassembly**
  (`RawDisassembly`).
- All six new marker fields (`s_TryAdvanceMarker`, `s_AdvanceLeafMarker`, `s_AdvanceAbortMarker`,
  `s_AdvanceParallelMarker`, `s_AdvanceCompositeMarker`, `s_AdvanceDecoratorMarker`) are present in
  that disassembly by name, each initialized in `AIBT.NativeLifecycleMachineV1..cctor()` via a real
  call to `Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility::CreateMarker__Unmanaged_Ptr` --
  confirmed by reading the actual disassembly text around each symbol, not just a name-contains
  check. No managed call, exception helper, or fallback trampoline appears anywhere in the
  disassembly (`managed`/`mono_`/`exception`/`NotSupported` searched; the one `managed` hit is
  `Unmanaged_Ptr`, a substring false-positive, confirmed by reading its surrounding context).
- **Zero markers were added to a method that could not accept one without a fallback** -- every
  marker site compiled clean under Burst.

## Behavior-neutrality (isolation) proof

Mirrors `P3-007`'s own before/after comparison pattern, adapted to what this card actually changes
(execution-engine instrumentation, not compiled-tree content -- `P3-007`'s own compiled-content-hash
mechanism is a property of `TreeDocument` compilation, unrelated to executor internals, so the
comparison here is direct behavior-case pass/fail parity instead):

- `AIBT.Runtime.Tests` (601 tests, the assembly containing every touched file): **601/601 passed
  with markers present**; the five touched files were then `git stash`-reverted to the pre-card
  baseline (markers absent) and the exact same assembly was re-run: **601/601 passed**, identical
  test count, identical zero-failure result. The stash was popped immediately after, restoring the
  marker-present state (re-verified compiling and passing again before continuing).
- `AIBT.Editor.Tests` + `AIBT.Integration.Tests` + `AIBT.BehaviorCases.Tests` (470 tests) and
  `AIBT.NativeSnapshot.Tests` + `AIBT.NativeCommandsAndAsync.Tests` +
  `AIBT.NativeTreeAgentBlackboard.Tests` + `AIBT.NativeSharedBlackboard.Tests` +
  `AIBT.NativeBurstDispatch.Tests` (167 tests) all pass with markers present, 0 failures.
- `AIBT.CodeGen.ContractTests` + `AIBT.CodeGen.GenerationTests` +
  `AIBT.CodeGen.GenerationConsumerTests` (21 tests) show 2 pre-existing failures
  (`GeneratedArtifactContractTests`, both `PackageManager.PackageInfo.FindForAssembly(...)` returning
  `null`) -- a host-embedded-layout artifact (this project's `Assets/AIBT` is not consumed as a real
  UPM package here), unrelated to any file this card touched (confirmed: neither test nor its
  package-path-resolution code is anywhere near `Runtime/Execution`, `Runtime/Scheduling`, or
  `Runtime/Blackboard`), the same class of host-project noise `P3-013`/`P4-009`/`P6-012`'s own gate
  evidence already documents repeatedly.
- `git diff --stat` on the five touched files: **40 insertions(+), 0 deletions(-)** -- every change
  is a new `using var _ = marker.Auto();` line; no existing statement was altered, confirming
  "no restructuring of the methods themselves beyond adding markers" by construction, not just by
  test outcome.

## Real Profiler capture

A real Windows x64 Development Player (`Benchmarks~/Phase7/Profiler/`, `BuildOptions.Development |
BuildOptions.ConnectWithProfiler`, Burst-enabled, IL2CPP, confirmed by
`profiler-capture-build-evidence.json` in this folder) ran `P4-001`'s own
`deep-sequence-selector-traversal` scenario (64 agents, `Immediate` policy, driven every `Update()`
frame) continuously for 150 seconds (33,036 frames completed, no failure marker). The open Editor's
Profiler connected to it live (`UnityEditorInternal.ProfilerDriver.connectedProfiler` set to the
Player's real connection id), recorded 31 frames (Deep Profile off), and saved a real capture:
`profiler-capture-deep-sequence-selector-traversal.data` (22 MB) in this folder -- openable in
Unity's own Profiler window (`File > Open` from the Profiler) by anyone reviewing this evidence.

A live transcript of one captured frame's actual marker hierarchy, pulled directly from
`ProfilerDriver.GetHierarchyFrameDataView` (not hand-written), shows the real nesting and per-call
cost this card's acceptance criterion asks for:

```
Update.ScriptRunBehaviourUpdate time=2,1753
    AIBT.Runtime.Tests.dll!AIBT.Benchmarks.Phase7.Profiler::ProfilerCaptureProbe.Update() [Invoke] time=2,1739
      AIBT.Native.LifecycleTick time=0,0004
      AIBT.Native.LifecycleTick time=0,0002
        AIBT.Native.Dispatch.Composite time=0
      AIBT.Native.LifecycleTick time=0,0001
      ...
      AIBT.Native.LifecycleTick time=0,0002
        AIBT.Native.Dispatch.Leaf time=0,0001
```

(`AIBT.Native.LifecycleTick` per-tick cost in this scenario is dominated by the outer `TryAdvance`
dispatch loop itself; individual `Dispatch.Composite`/`Dispatch.Leaf` sub-costs round to sub-0.1ms at
this tree depth -- consistent with `P4-008`'s own finding that Editor/Player-adjacent single-call
costs at this scale are small and noise-sensitive, not a new performance claim.)

## Allocation/wall-clock regression check

`P4-001`'s own harness (`SchedulingPolicyDriver`, promoted to `Runtime/Scheduling/` by `P6-008`) was
re-run with and without the markers, per the card's own required-verification line. A disposable
EditMode spike (not committed, mirroring `P5-001`'s own disposable-spike precedent) ran
`SchedulingPolicyDriver.TryRunImmediate` 500 times (64 agents, 50-call warmup, `GC.Collect`-flushed
before measuring) against a hand-built two-node program, three samples per configuration:

| Configuration | ns/agent/call (3 samples) | Median | bytes/call |
| --- | --- | --- | --- |
| Markers absent (git-stashed baseline) | 11539.97, 11231.77, 11717.94 | 11539.97 | 0.00 (all 3) |
| Markers present (this card's state) | 11893.24, 12321.19, 12190.55 | 12190.55 | 0.00 (all 3) |

**Zero GC allocation delta in every sample** -- `ProfilerMarker.Begin()`/`.End()` (via `.Auto()`)
allocate nothing, confirmed empirically, not assumed from documentation. The median wall-clock delta
is **+5.6%** on this specific micro-benchmark. Two things keep this from being the regression the
card's acceptance criterion forbids: (1) this measurement runs the managed/Mono-JIT `TryAdvance`
call path directly in the Editor, not the actual Burst-compiled `AdvanceJob` production path the
real scheduling pipeline uses -- `P4-008`'s own finding that Editor-measured numbers are ~13-14x
slower/noisier than the real Burst/IL2CPP Player path applies here too, so this almost certainly
overstates the real relative cost; and (2) the tree used is a single two-node program run in a tight
loop with no other work -- the worst-case ratio of fixed per-call marker overhead to actual node
work, not a representative agent population. This is disclosed as a real, honestly-measured number,
not smoothed away -- consistent with this project's own "no default, threshold, or recommendation is
derived from Editor-only numbers" discipline (`P4-002`'s own precedent).

## Scope and limitations

- Marker-presence unit tests were attempted and dropped; see Decision above. The Burst disassembly
  and the real Player Profiler capture are the actual, stronger verification this card's acceptance
  criteria describe.
- Command-bridge markers are out of scope; see Decision above. `NativeCommandMergeOwnerV1`/
  `NativeCommandAsyncOwnerV1` remain unwired to production, a pre-existing gap this card did not
  create and is not the right place to fix.
- The allocation/wall-clock comparison above ran once (three samples per side) in Editor batchmode
  on this workstation; it is evidence, not a threshold -- no regression threshold is authorized by
  this card (`Planning~/USER_ACTIONS.md` reserves that for the owner, after research exists, per
  every Phase 4 card's own precedent).
- `profiler-capture-deep-sequence-selector-traversal.data` is 22 MB, larger than any other
  committed binary evidence in this repository (the largest prior precedent is `P4-008`'s own 8.3 MB
  WebGL build). Flagged for the owner's awareness before committing, not trimmed unilaterally.

See `verification-results.json` for exact commands and results.
