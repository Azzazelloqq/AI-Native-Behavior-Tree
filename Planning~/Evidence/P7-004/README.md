# P7-004 long-running and stress test suite evidence

## Result

Done. Adds the "long-running and stress tests" layer `Documentation~/roadmap.md` names for Phase 7
(`Tests/Runtime/Stress/`, new, test-only -- no production file under `Runtime/`, `Authoring/`,
`Editor/`, or `MCP/` was touched, per this card's own Allowed-changes fence).

## What was built

Three new files, one per deliverable:

- **`NativeExecutionSoakTests.cs`** (3 tests): a single long-lived native instance driven through
  20,000 tick/`Waiting` cycles with zero managed GC allocation after warmup (extending `P2-021`'s
  own established `GcAllocIs.Not.AllocatingGCMemory()` technique by two-plus orders of magnitude in
  tick count); the SAME 20,000-cycle run asserting every owned `NativeArray<T>`'s own `.Length`
  never changes (the engine's fixed-capacity design promise, made explicit and checked rather than
  assumed); and a command/completion-table boundary test (see the real finding below).
- **`NativeExecutionLargePopulationStressTests.cs`** (1 test): 10,240 agents (10x `P4-002`'s largest
  measured population, 1024) driven to completion via `SchedulingPolicyDriver.TryRunBatchedJobsSameFrame`
  with `batchSize=128` (`P4-002`'s own largest measured, deliberately-costly batch size), every
  single agent asserted to reach the identical deterministic outcome a 16-agent control population
  (`P4-002`'s smallest measured point) also reaches -- no crash, no per-agent corruption from
  population scale.
- **`HotReloadUnderLoadStressTests.cs`** (2 tests): reload fired *repeatedly* against a live
  population, for both backends -- a 40-instance population per backend, split into a "never
  reloaded" group (compared bit-for-bit against an all-untouched control) and a "repeatedly
  reloaded" group (full-restarted every one of 10 waves, then confirmed still healthy afterward).
  Proves `P5-004` (reference executor) and `P7-012` (native backend) hold under repetition and
  concurrent population load, not just the single-reload cases their own evidence already covers.

## Two real findings from this card's own test-driven verification

Both surfaced as genuine test failures during development (not assumed, not guessed at) and are
disclosed here rather than smoothed over -- exactly the kind of thing this card's own Required
verification exists to catch:

1. **A normally-completed native instance is terminal.** `NativeLifecycleMachineV1.TryBeginUpdate`
   requires `control.HasRootStatus == 0`; reading `PopFrame`/`PopAbortedFrame` directly shows
   `HasRootStatus` is set to `1` on every normal root completion and is cleared *only* on the abort
   path -- never after a normal completion. The soak tests' first draft assumed a tree could be
   ticked to completion and then begun again repeatedly (a "restart every frame" model); every such
   attempt failed with `NativeLifetimeStateInvalid` on the second cycle. Fixed by redesigning the
   soak tests around the engine's actual, already-established usage pattern (confirmed against
   `NativeExecutionEquivalenceTests.Scenario`'s own `BeginNextUpdate` convention): a real
   long-running agent stays perpetually active, `Waiting` between ticks, resumed via `TryBeginUpdate`
   -- never re-entered after reaching root `Completed`. Not a defect; a real, previously-undocumented
   usage constraint this card's own soak-test design had to discover and conform to.
2. **`NativeCommandAsyncOwnerV1`'s own operation-record table is a monotonic lifetime log, not a
   reclaimable ring buffer.** Confirmed by reading `TryStart`/`TryCancel` directly:
   `control.OperationCount` only ever increments; cancelling or consuming an operation marks its
   own state in place but never removes or compacts its slot. `capacity.operationRecords` therefore
   bounds "how many async operations an instance may ever start across its whole lifetime," not
   "how many are concurrently in flight." The soak test's third assertion was redesigned around this
   real contract: it proves the boundary itself is safe (a clean `CapacityExceeded` at exactly the
   configured capacity, never silent corruption or an off-by-one) rather than asserting a
   "never exhausted" property that would be false by design. A genuinely long-running production
   instance that starts many async operations over its lifetime must size this capacity for that
   full lifetime, or periodically reset (e.g. via hot reload, `P5-004`/`P7-012`) -- existing,
   unchanged behavior, newly documented here.

## Verification

```text
Verify-Static.ps1 -- passed
Unity MCP run_tests (EditMode):
  - AIBT.Tests.Runtime.Stress.* -- 6/6 passing, run twice in a row (reproducible, no
    non-deterministic timing anywhere in the suite -- every reload wave uses a fixed, seeded
    index partition, never UnityEngine.Random or wall-clock timing)
  - Full EditMode project regression -- 1615 total, 1612 passed, 3 failed, all 3 pre-existing and
    unrelated to this card (the same CodeGen-test-assembly-path environment issue and unrelated
    LocalSaveSystem failure already disclosed in P7-012's own evidence)
```

No stress test surfaced a real production defect this pass -- both findings above are usage-pattern
corrections to the tests themselves, not bugs in `Runtime/`.

## Scope and limitations

- `Benchmarks~/Phase7/Stress/` (an isolated Player-build harness) was not added -- an EditMode-only
  run proved sufficient for every deliverable (all three run in well under 10 seconds combined);
  not assumed needed, per the plan's own "out of scope for this pass" note.
- No new performance default, threshold, or "acceptable reload overhead" claim is introduced -- this
  card verifies correctness only, per its own Forbidden-changes clause.
- The large-population determinism-drift proof compares terminal status only (not per-agent step
  counts, which `SchedulingPolicyDriver.TryRunBatchedJobsSameFrame` does not expose per-agent) --
  sufficient to catch cross-lane corruption for this fixture, but a narrower signal than a full
  per-agent trace comparison would be.
- The hot-reload-under-load isolation proof compares `Control.Depth` for never-reloaded native
  lanes (not a full per-node Frame/Generation/NodeMemory comparison) -- a real, meaningful state
  check, but narrower than `P7-012`'s own single-reload batch-isolation proof, which does compare
  full per-lane traces. Widening this comparison was judged out of proportion to what repeated-wave
  isolation specifically needs to prove; disclosed rather than silently narrowed without comment.
