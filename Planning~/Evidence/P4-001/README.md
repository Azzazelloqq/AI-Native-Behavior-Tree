# P4-001 benchmark scenario catalog and harness evidence

## Result

- `Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs` (new, internal): constructs N independent
  native tree-instance agents from one already-compiled `CompiledProgram` and drives them under
  each of the three accepted Phase 2 fixed policies (Immediate, Budgeted, BatchedJobsSameFrame).
  Deliberately Runtime-only (no `AIBT.Authoring` dependency) -- it takes a compiled program as
  input rather than compiling one -- so the identical file is shared unchanged between this
  in-project EditMode harness-correctness suite and the isolated Player-benchmark project under
  `Benchmarks~/Phase4/Scheduling/` (which copies it in alongside `Runtime/` and `Authoring/`,
  mirroring `Benchmarks~/Phase2/Dispatch/`'s isolated-project technique). No new `AIBT.Authoring`
  reference was added to `Tests/Runtime/AIBT.Runtime.Tests.asmdef`; that boundary is untouched.
- `Tests/Runtime/Benchmarking/SchedulingPolicyDriverTests.cs` (new): proves the driver itself is
  correct against a minimal hand-built two-node program -- drives 8 agents to the configured
  terminal leaf status under all three policies, and proves the Immediate path introduces no
  managed allocation beyond agent construction (`GcAllocIs.Not.AllocatingGCMemory()`), satisfying
  this card's "harness introduces no new managed allocation" acceptance criterion at the driver
  level.
- `Benchmarks~/Phase4/Scheduling/Unity/SchedulingScenarios.cs` (new, isolated-project-only):
  the scenario catalog. Six of the fourteen `Documentation~/benchmarks.md` scenarios are
  implemented (scheduling-baseline-empty-job, shallow-tree-cheap-conditions,
  deep-sequence-selector-traversal, wide-branching-frequent-failures,
  predominantly-running-actions, many-programs-small-populations) -- the ones needing only
  built-in composites and constant-status leaves. Trees are built as canonical `.aibt.tree` JSON
  and compiled through the real `ReferenceCompiler`/`CanonicalTreeJson` pipeline (via
  `ReferencePreviewDriver.CreatePreviewNodeRegistry()`, reused rather than inventing a second
  registry-building path, same reuse decision as P3-012's fixtures), never bespoke
  `CompiledProgram` construction. The remaining eight scenarios (typed-blackboard access, command
  emission, async/managed-node boundary, computationally expensive Burst nodes, mixed
  cheap/expensive agents, event-driven wakeup, same-frame/pipelined/budgeted comparison,
  hot-reload/debug-instrumentation overhead) need leaf semantics that do not exist in reusable
  form anywhere in AIBT yet; each is a documented placeholder (name + what it isolates) in the
  runner's `documentedNotYetImplementedScenarios`, never silently faked with a structurally
  similar tree that would not actually isolate what it claims to.
- `Benchmarks~/Phase4/Scheduling/Unity/SchedulingBenchmarkRunner.cs` (new, isolated-project-only):
  the `-executeMethod` entry point. Sweeps every implemented scenario against all three policies
  at two agent-count points (16, 128 by default); `BatchedJobsSameFrame` is additionally swept
  across batch size (8/32/128 by default) and `JobsUtility.JobWorkerCount` (1 and the machine's
  `JobWorkerMaximumCount` by default, validated against that maximum before any work starts, and
  restored via `finally` even on failure) -- `Immediate`/`Budgeted` are plain managed loops with
  neither Jobs nor batching, so both fields record `-1` (not applicable) on their cases. Records
  raw per-sample timing/allocation data separately from descriptive min/median/p95/max summaries,
  and captures full environment metadata (Unity/package/OS/CPU/build config), mirroring
  `DispatchBenchmarkRunner`'s pattern. `PipelinedJobs` and `Auto` are explicit documented policy
  placeholders (`documentedNotYetImplementedPolicies`) -- neither is implemented, and no case ever
  substitutes one of the three accepted fixed policies for either, per this card's
  forbidden-changes clause.
- `Benchmarks~/Phase4/Scheduling/Unity/AIBT.Runtime.Tests.asmdef` (new, isolated-project-only):
  deliberately reuses the existing friend assembly name `AIBT.Runtime.Tests` (`Runtime/AssemblyInfo.cs`'s
  `InternalsVisibleTo` target), the same technique `Benchmarks~/Phase2/Dispatch/` already uses with
  `AIBT.NativeBurstDispatch.Tests`, so the isolated copy of `SchedulingPolicyDriver.cs` can
  construct the internal native execution types without widening production visibility. No new
  `InternalsVisibleTo` grant was added anywhere.
- `Benchmarks~/Phase4/Scheduling/Run-SchedulingBenchmark.ps1` (new): builds the isolated project
  (copies `Runtime/`, `Authoring/`, `Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs`, and
  this card's `Unity/` folder), pins Burst 1.8.30 / Collections 6.5.0 / Newtonsoft JSON 3.2.2, and
  runs Unity headless. Mirrors `Run-DispatchBenchmark.ps1` parameter-for-parameter where the
  scenario matches (`-WarmupSamples`, `-MeasuredSamples`, `-OutputPath`, `-IsolatedProjectPath`).
- `Benchmarks~/Phase4/README.md` and `Benchmarks~/Phase4/Scheduling/README.md` (new): scope,
  measured-work description, run instructions, and the recorded canonical run's summary table.
- Full EditMode suite: 1314 tests, 1311 passed; 3 pre-existing failures unrelated to this card
  (same as recorded in `Planning~/Evidence/P3-009/`, `P3-010/`, `P3-012/`: two
  `AIBT.Tests.CodeGen.Generation.GeneratedArtifactContractTests` package-detection failures and
  one `LocalSaveSystem.Tests.SaveStoreTests.SaveStore_AutoSave_WritesToDisk`). Confirmed via
  `git status` inside the `AIBT` submodule that this session touched only
  `Tests/Runtime/Benchmarking/` and `Benchmarks~/Phase4/` -- nothing in `CodeGen` or
  `LocalSaveSystem`.

## Correction: parameter-matrix sweep was initially incomplete

The card's own Deliverables text calls for "a parameter-matrix runner sweeping logarithmic agent
counts, tree shape, batch parameters, and worker-thread counts where controllable." The first
version of this card marked `Done` only swept agent count and scenario (tree shape); `batchSize`
was a single fixed CLI value and `JobsUtility.JobWorkerCount` was recorded as environment metadata
only, never varied. The user caught this gap directly by asking whether the benchmark was
actually built to inform thread-count/batch-size configuration. It was not, yet -- this was a real
scope miss against the card's own stated deliverable, not a matter of interpretation.

Fixed by extending `SchedulingBenchmarkRunner` to sweep `batchSize` (8/32/128) and
`JobsUtility.JobWorkerCount` (1 and this machine's `JobWorkerMaximumCount`, 23) for
`BatchedJobsSameFrame` specifically -- the only one of the three policies that actually schedules
Unity Jobs (`NativeBatchedLifecycleOwnerV1.TrySchedule`); `Immediate` and `Budgeted` are plain
managed loops with nothing for either parameter to affect, so sweeping them there would have been
wasted work, not thoroughness. `JobsUtility.JobWorkerCount` was confirmed settable (with a real
`get`/`set`/restore round trip) via `execute_code` against the live Unity MCP session before
building on that assumption.

This produced a real, reproducible finding: at 16 agents, `workerThreadCount=1` measures lower
median ns/agent than `workerThreadCount=23` in every implemented scenario -- sometimes by more
than 2x -- because the fixed cost of waking/coordinating more worker threads outweighs the
parallelism gained at this population size; the gap narrows at 128 agents. See
`Benchmarks~/Phase4/Scheduling/README.md`'s tables. This is exactly the kind of input
`Documentation~/execution-and-scheduling.md`'s batch-size/worker-count calibration work needs, not
a recommendation in itself.

`Run-SchedulingBenchmark.ps1` also still passed the old singular `-aibtBatchSize` CLI flag after
the C# side was renamed to `-aibtBatchSizes` -- a second, related gap that would have silently
fallen back to the runner's own default sweep instead of honoring `-BatchSizes` from the caller.
Fixed alongside the sweep extension; verified with an explicit non-default
`-BatchSizes '4,16' -WorkerThreadCounts '1,4'` invocation before regenerating the canonical run
with defaults.

## Debugging note (kept, not silently smoothed over)

Two real bugs were found and fixed during the isolated-project run, not just in the in-project
unit tests:

1. **Wrong Copy-Item parameter.** `Run-SchedulingBenchmark.ps1` originally used
   `Copy-Item -LiteralPath (Join-Path $benchmarkSource '*') ...`; `-LiteralPath` does not expand
   `*`, so the scenario/runner/asmdef files silently never reached the isolated project (only the
   copied `SchedulingPolicyDriver.cs` did) -- Unity's compile errors then pointed at
   `SchedulingPolicyDriver.cs`'s *own* internal-type references, which was a red herring pointing
   at the real cause: naming. Fixed by switching to `-Path`, which does expand wildcards.
2. **Wrong friend-assembly name.** Even after the copy was fixed, the isolated asmdef was
   initially named `AIBT.SchedulingBenchmark`, which `Runtime/AssemblyInfo.cs` does not grant
   `InternalsVisibleTo`. `NativeLifecycleNodeKindV1`/`NativeLifecycleMachineV1`/etc are `internal`
   to `AIBT.Runtime`; renaming the isolated asmdef to `AIBT.Runtime.Tests` (an existing granted
   name) fixed it, matching the Dispatch benchmark's precedent exactly.
3. **Duplicate JSON node.** `SchedulingScenarios.BuildFixedShape`'s depth==1 case appended the
   root leaf twice (once via the general leftover-frontier loop, once via a redundant explicit
   `if (depth == 1)` branch), causing `CanonicalTreeJson.Parse` to reject it
   (`AIBT1003: Duplicate object properties are not permitted`). Fixed by deleting the redundant
   branch.

All three were caught by actually running the isolated project end-to-end, not just by compiling
in-editor -- consistent with this project's discipline of closing the full evidence loop rather
than stopping at "it compiled."

## Recorded numbers

See `Benchmarks~/Phase4/Scheduling/Results/scheduling-windows-editor-20260819-165205.json` for
every raw sample and `Benchmarks~/Phase4/Scheduling/README.md` for the summarized tables (median
ns/agent per scenario, per policy, per agent count, and -- for `BatchedJobsSameFrame` -- per batch
size and worker-thread count). Not reproduced again here to avoid a second copy drifting from the
authoritative JSON.

## Decision

- **Scope narrowed to six structural scenarios**, self-directed (not escalated): implementing all
  fourteen catalog scenarios with rich leaf semantics (blackboard-heavy, command-heavy,
  async-heavy) is a substantially larger effort with no reusable infrastructure yet to build on;
  narrowing to the six that need only built-in composites/constant-status leaves, and explicitly
  documenting the rest as placeholders, extends the exact placeholder discipline this card's own
  acceptance criteria already require for the `PipelinedJobs`/`Auto` policy entries. This is a
  scope decision within the card's own "produces the ability to measure" objective, not a
  DECISION_BOUNDARIES-level architectural escalation -- no new assembly boundary, no forbidden
  change, no missing dependency.
- **`Tools~/Verification/P4/` was not created.** The card's "Allowed changes" permits it but does
  not require it; nothing in this card's actual deliverables or acceptance criteria needed a
  dedicated verification tool (unlike `Tools~/Verification/P2/`'s CodeGen-artifact-contract and
  Android-build audits, which exist because P2 had codegen/cross-platform claims to verify). Left
  for a later card if one turns out to need it.
- Reused `ReferencePreviewDriver.CreatePreviewNodeRegistry()` rather than building a second
  registry-construction path, consistent with P3-004--P3-012's established reuse pattern.

## Scope and limitations

- One measured run per case on one workstation; not a stable baseline, not generalized to other
  hardware (`Planning~/USER_ACTIONS.md` requires owner approval across multiple hardware classes
  before any threshold is adopted).
- `BatchedJobsSameFrame` measures far higher ns/agent than `Immediate`/`Budgeted` at these small
  scales (16/128 agents, 1-63 nodes) -- a descriptive observation about fixed per-batch overhead
  at this scale, not a general claim; P4-002 (full-scale sweep) and later batch-size-calibration
  work are exactly where this gets characterized properly.
- No performance threshold, regression bound, or scheduling recommendation is drawn from these
  numbers, per this card's own forbidden-changes clause -- including the batch-size/worker-thread
  finding above: it is a real, reproduced observation, not a recommended worker-thread setting.
- The batch-size/worker-thread sweep only covers `BatchedJobsSameFrame`, only two agent-count
  points, and only this one workstation's thread topology; it is evidence for later calibration
  work, not a calibrated result itself.
- Eight of fourteen catalog scenarios and both `PipelinedJobs`/`Auto` policies remain documented
  placeholders; this card produces the *ability* to measure, not the full catalog's results.
- Raw results here are an input to Phase 4's broader benchmark research (P4-002 runs this harness
  at full scale), not a substitute for it, per this card's own handoff note.

See `verification-results.json` for exact commands and results.
