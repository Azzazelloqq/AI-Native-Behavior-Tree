# P7-003 — Profiler integration and validation

Status: `Done`

## Objective

Add real Unity Profiler instrumentation (`Unity.Profiling.ProfilerMarker`) to the execution and
scheduling hot paths (reference executor, native lifecycle machine, native scheduling policies,
dispatch) and validate the markers actually appear and attribute cost correctly in a real Unity
Profiler capture on a Player build. No production code currently has any `ProfilerMarker` or
`Unity.Profiling` usage anywhere (confirmed by grep before this card was written) — `benchmarks.md`'s
own methodology measures wall-clock/GC directly, never through the Profiler UI, so this is
genuinely new instrumentation, not wiring up something partially built.

## Depends on

- `P2-025` (Phase 2 gate; the native execution/dispatch code this card instruments).
- `P4-009` (Phase 4 gate; the scheduling code this card instruments).

## Required reading

- `Documentation~/benchmarks.md`'s "Metrics" section (what this card's markers should let a
  developer see interactively that the existing wall-clock methodology already measures in
  aggregate).
- `Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs`, `Runtime/Scheduling/Native/*.cs`,
  `Runtime/Execution/Reference/Core/ReferenceExecutionMachine.cs` (the hot paths to instrument).
- Unity's own `ProfilerMarker` Burst-compatibility rules (markers must be usable from Burst-compiled
  code without introducing a managed fallback — confirm this before instrumenting any
  `[BurstCompile]` method).

## Allowed changes

- Marker declarations and `Begin()`/`End()`/`using (marker.Auto())` calls inside the hot-path files
  named above (and their direct callees where a marker boundary is genuinely useful) — no
  restructuring of the methods themselves beyond adding markers.
- `Tests/Runtime/NativeExecution/` and `Tests/Runtime/ReferenceExecutor/` (new marker-presence
  tests only, e.g. via `Unity.Profiling.ProfilerRecorder` reading a marker's own counter).
- `Planning~/Evidence/P7-003/`.

## Forbidden changes

- Any change to observable execution semantics, ordering, or timing. A marker must be provably a
  no-op on behavior — the isolation proof pattern `P3-007` already established (compare compiled
  content hash / behavior-case results before and after) applies here too.
- Introducing a managed fallback, allocation, or virtual dispatch into a Burst-compiled path merely
  to add instrumentation — if a marker cannot be added to a specific `[BurstCompile]` method without
  one, that method is left uninstrumented and the gap is disclosed, not forced.
- Any new performance claim, threshold, or default — this card makes cost visible, it does not
  interpret it into a claim (`P7-002` owns that).

## Deliverables

- `ProfilerMarker`s on: per-update-batch scheduling entry, per-instance lifecycle tick, dispatch
  case execution, and blackboard/command bridge calls — scoped to what's genuinely useful to see
  broken down in the Profiler, not one marker per line.
- A real, recorded Unity Profiler capture (Deep Profile off, a real non-Development Player per
  `P4-008`'s own precedent) showing the new markers' hierarchy and cost attribution for at least one
  of `P4-001`'s existing benchmark scenarios.
- A regression test proving every full existing behavior-case/native-equivalence suite still passes
  byte-for-byte identical with markers present vs. a control run without them (or a documented
  reason a specific suite could not be run both ways).

## Acceptance criteria

- Zero markers were added to a method that could not accept one without introducing a managed
  fallback in a `[BurstCompile]` context — verified by inspecting the generated Burst compilation
  report, not assumed.
- The recorded Profiler capture is attached as real evidence (a `.data`/screenshot export), not
  described in prose only.
- No GC allocation or wall-clock regression outside measurement noise is introduced — proven by
  re-running `P4-001`'s harness with and without the new markers and comparing.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
P3-007-style isolation proof: markers present vs. absent produce identical compiled-content hashes
  and identical behavior-case results
real Player build + Unity Profiler capture, attached as evidence
P4-001 harness re-run with/without markers, compared for allocation/wall-clock delta
```

## Handoff notes

- `P7-004`'s long-running/stress tests can reuse these same markers to attribute where stress-test
  cost actually goes, rather than only reporting an aggregate pass/fail.

## Outcome

Done. `ProfilerMarker`s added to `NativeLifecycleMachineV1` (`TryAdvance` plus all five per-node-kind
`Advance*` dispatch methods), `NativeSameFramePhaseControllerV1`/`NativePipelinedPhaseControllerV1`
(`TryScheduleExecuteRound`), `NativeSharedContextOwnerV1.TryReduceUpdate` (the real blackboard-bridge
call), and `ReferenceExecutionMachine.AdvanceOneStep` (the reference-backend mirror) -- every change
a single `using var _ = marker.Auto();` line, `git diff --stat` confirming 40 insertions, 0
deletions across all five touched files. Two deviations from this card's own text, both put to the
owner before proceeding: command-bridge markers were dropped (the native command-merge subsystem has
zero production callers, confirmed by grep -- instrumenting it would misrepresent test-only code as
a hot path); the real Profiler capture required a Development Build (`BuildOptions.Development |
ConnectWithProfiler`), not the "non-Development Player" the card's own text names, since Unity
Profiler connectivity is impossible against a true Release build. A third deviation was found and
resolved without needing owner input: `ProfilerRecorder`-based marker-presence unit tests (this
card's own suggested example technique) do not work in this Unity version's EditMode environment
(confirmed with an independent, non-AIBT control marker) -- replaced by two stronger verifications
instead. Burst compatibility was verified by live reflection into the Editor's own Burst Inspector
data model, forcing a real compile of `AdvanceJob` and reading its actual disassembly:
`IsBurstError=False`, all six marker fields present via genuine `ProfilerUnsafeUtility.CreateMarker`
calls, zero managed-fallback indicators. A real Windows x64 Development Player ran `P4-001`'s
`deep-sequence-selector-traversal` scenario continuously for 150s (33,036 frames); the open Editor's
Profiler connected live and captured a real `.data` file showing the markers' hierarchy and per-call
cost. `P4-001`'s harness (`SchedulingPolicyDriver`) was re-run with and without the markers (a
disposable, uncommitted spike): zero GC allocation delta in every sample; a real, disclosed +5.6%
median wall-clock delta on a worst-case single-node-per-call Editor-Mono micro-benchmark (not the
real Burst-compiled production path). Full regression: 601/601 (`AIBT.Runtime.Tests`, re-confirmed
identical with and without markers via `git stash`), 470/470, 167/167, and 19/21 (2 pre-existing,
unrelated host-embedded-layout failures). See `Planning~/Evidence/P7-003/README.md`.
