# P7-003 — Profiler integration and validation

Status: `Draft`

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
