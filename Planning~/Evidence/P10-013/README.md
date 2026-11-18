# P10-013 evidence — stage 1 (2026-11-17)

ADR: `Documentation~/decisions/ADR-P10-013-non-restarting-reactive-composites.md`, accepted
2026-11-17 with all nine §6 recommendations (decision AIBT-048). This stage implements ADR §5
stage 1: specification text, manifests, reference executor, behaviour cases. Stages 2 (native) and
3 (IL2CPP) are not started.

## 1. What changed

| Area | Change |
|---|---|
| v1 pins (first, before production code) | `Tests/Editor/Agents/ReactiveV1RestartPinTests.cs`: `wait(ticks: 3)` under v1 `reactive-sequence` is Running for 12 updates; `Timeout(3 s)` over a long child under v1 never fires; `Cooldown(on-enter)` blocks with `Failure` on update 2. Each with a memory-sequence control. Ran 3/3 on unchanged code, confirming ADR §1.3 including the inferred decorator cases. |
| Specifications | `execution-semantics-v1.md`: new section "Retaining reactive sequence and selector" (ADR §2.1 text, §2.2 interactions, informative pitfall note) and an observer-parent paragraph. `reference-executor-machine-v1.md`: atomic-steps addition and section "Retaining reactive composites" (update-start processing, retained branch steps). `compiled-program-v1.md`: the two type IDs and their memory layout. v1 sections unchanged word for word. |
| Decisions | ADR status Accepted with the owner's nine decisions; `decisions.md` row AIBT-048; P7-028 sentence amended ("reserved for structural composites and decorators executed by the native lifecycle machine; adding one needs an ADR"). |
| Manifests | `BuiltInNodeManifests`: `aibt.core.reactive-sequence-retaining` / `aibt.core.reactive-selector-retaining`, version 1, composites, memory 8 bytes / alignment 4 / Activation (offset 0 child cursor, offset 4 retained ordinal + 1 during a guard pass). v1 manifests unchanged byte for byte. |
| Authority | `RuntimeBuiltInCatalogAuthority` regenerated (two entries added, nothing else moved); hash `6c8a5da9…bd66` → `6ba5502ce790f694d14e8dce65998395c032867f781e9cef2e5c01bffaeafb46`. The old hash stays in historical evidence P7-019/P7-022 only. |
| Validator / compiler | `TreeValidator`: observer whose parent is a retaining composite → `InvalidObserverContext`, "Retaining reactive composites do not support observers yet: …". The generated compiler's observer builder throws the same reason defensively. |
| Reference executor | `ReferenceExecutionMachine`, `ReferenceFrame`, `ReferenceReactiveCompositeHandlers`: guard pass, parked retained branch, resume, end, takeover, abort from above during a pass, update-start processing per ADR §2.1, budget suspension between all steps. `HasReactive` now means v1 only; a new `IsRetaining` binding flag selects the new path, so v1 code paths are untouched. |
| Native rejection | See §3. |
| Fixtures | Registry/content hash pins updated: `NodeRegistryHashTests` (built-in registry hash), `NodeRegistryBuilderTests` (count 13 → 15), `minimal-compiled-v1.golden.json` (compiled content hash), `NodeManifestContractTests` (new IDs and layout). |
| Generated docs | `Documentation~/generated/node-catalog.md` (65 → 67 types) and `api-reference-authoring.md` (2 public consts) regenerated with the generators the freshness test uses. |
| Docs | `README.md` node table, `simulation-container.md` ("Not under a reactive composite"), `CHANGELOG.md`. |

## 2. Tests

New tests:

| Assembly | Class | Count | Covers |
|---|---|---|---|
| Editor | `ReactiveV1RestartPinTests` | 3 | v1 pins (above) |
| Editor | `RetainingReactiveManagedTests` | 3 | the same three subtrees finish under the retaining sequence through `ManagedTreeProgram`/`ManagedTreeInstance` (managed agent path) |
| Editor | `TreeValidatorTests.Validate_ObserverUnderARetainingReactiveCompositeIsRejectedWithItsReason` | 4 | observer `self`/`lower-priority`/`both` under each retaining type rejected with the reason; same tree under v1 accepted |
| Runtime | `RetainingReactiveExampleTests` | 7 | ADR §2.5 examples 1, 2, 3, 3-alt, 4, 5, 6 with exact callback order (trace records) |
| Runtime | `RetainingReactiveLifecycleTests` | 26 | budget of 1 step per segment equals unlimited for 10 scenarios (records, results, step counts, suspensions = steps − 1); start/resume cost one step each (empty pass = 2); nesting (inner takeover, outer guard failure deepest-first); retaining inside `Parallel`; `Parallel` as a guard that takes over; v1 inside retained and v1 above retaining; abort from above during a pass (with an active guard branch, and between guards); external abort between updates; empty sequence/selector; root completion and restart; memory layout during a pass; P9-001 reevaluation request below a retaining composite (deferred to after the guard pass, dropped when the pass ends the composite) |
| Integration | `SemanticSliceBehaviorTests` / `SemanticSliceCompilationTests` | 5 + 5 | golden cases `retaining-crew` (ex. 1), `retaining-wait` (ex. 3), `retaining-takeover` (ex. 4), `retaining-pitfall` (ex. 5), `retaining-timeout` (ex. 6) pass on the reference executor and round-trip canonically |
| Integration | `RetainingReactiveNativeRejectionTests` | 6 | generated compiler reports `AIBT3012` at `/nodes/<id>/type`; native machine build refuses the program; v1 controls accepted |

The golden cases use three new test-fixture leaves in `SemanticSliceNodeContracts`
(`aibt.test.count-to-two`, `aibt.test.count-to-three`, `aibt.test.failure-except-update-3`). They are
not added to the native golden lists (`NativeExecutionEquivalenceTests` etc.), per the stage plan.

Final single pass per assembly (2026-11-17, after all changes):

| Assembly | Baseline at `23a465d` | Stage 1 | Difference |
|---|---|---|---|
| AIBT.Runtime.Tests | 710/710 | 743/743 | +33 new (7 examples, 26 lifecycle) |
| AIBT.Editor.Tests | 639/640 | 649/650 | +10 new (3 pins, 3 managed, 4 validator); the one failure is the known `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` |
| AIBT.Integration.Tests | 192/192 | 208/208 | +16 new (10 golden rows, 6 native rejection) |
| AIBT.BehaviorCases.Tests | 65/65 | 65/65 | 0 |
| AIBT.CodeGen.ContractTests | 6/6 | 6/6 | 0 |

Not run in this stage (no code they cover changed beyond the native rejection, which the new
integration tests cover): NativeBurstDispatch, NativeTreeAgentBlackboard, NativeSharedBlackboard,
PlayMode.

`Tools~/Verification/Verify-Static.ps1`: passed (177 work items). `ExpectedPublicAbiV2.txt`:
byte-identical (`git diff --quiet`).

## 3. Native rejection (mechanism)

No native kind exists for the new type IDs until stage 2. They are refused at both entry points:

1. **Authoring (generated compiler).** `GeneratedCompiledProgramV2Compiler.Compile` adds
   `ReferenceCompilerDiagnosticCodes.UnsupportedCapability` (`AIBT3012`, error) per retaining node, at
   `/nodes/<id>/type`: "'<type>' is not supported by the native executor yet (ADR-P10-013 stage 2). A
   tree that uses it runs only on the reference executor (BehaviorTreeAgent); for native execution use
   'aibt.core.reactive-sequence' or 'aibt.core.reactive-selector', which restart their running child on
   every update." This covers production tree asset baking, graph execution and the Editor's
   native-only validation. Trees validated through the reference compiler (managed agents) are not
   affected.
2. **Runtime.** `NativeReactivePolicyV1.IsUnsupportedRetaining` is checked in
   `NativeLifecycleMachineV1.ValidBinding` (the IDs are classified `GeneratedLeaf`, never v1 reactive,
   and the binding is refused, so machine creation fails) and explicitly in
   `GeneratedTreeRuntimeDefinitionV2.TryCreate` (`BurstContextResult.TypeMismatch` before the catalog
   lookup). The first is tested (`NativeHotReloadInstance.TryBuild` fails); the second is verified by
   reading only: building a generated catalog for this test was out of proportion for a one-line guard.

Native diff: `NativeReactivePolicyV1.cs` +10 lines (constants and the predicate),
`NativeLifecycleMachineV1.cs` +1 line (`ValidBinding`). No behaviour change for any other type.

## 4. Public API delta

Additive only: `AIBT.Authoring.BuiltInNodeManifests.RetainingReactiveSequenceTypeId` and
`RetainingReactiveSelectorTypeId` (public `const string`, beside the existing public type-ID
constants). No Runtime public API change; ABI v2 unchanged.

## 5. Deviations from and additions to the ADR wording

None contradicts the ADR. These are precisions the ADR left open, written into
`reference-executor-machine-v1.md`:

1. **Empty guard pass costs two steps.** Start and resume each count as one transition also when the
   retained child is child 0 (example 4, update 4). The ADR says "starting a guard pass is one
   composite transition" without excluding the empty case.
2. **Guard-ended composite records.** When a guard ends the composite, the composite's `NodeTicked`
   (terminal result) is recorded after the retained branch's abort records and before its `Exit`.
   This matches example 3-alt (`ok` E T→F X; `wait` A X(Ab); seq X→F); the ADR does not place the
   composite's tick record explicitly.
3. **Takeover point.** A guard that returns `Running` takes over when execution would leave the
   composite's subtree (the update would wait, or an enclosing `Parallel` would suspend the branch).
   The retained branch is aborted at that point, and the update then continues exactly as it would
   have. A `Parallel` inside the guard branch suspending its own branches does not count as leaving.
4. **Abort from above.** No extra step is spent moving from the guard branch to the retained branch
   (one deepest-first traversal: guard branch, retained branch, composite).
5. **Observers.** The reference machine faults with `InvalidCompositeState` on an observer whose owner
   is retaining, as a defence behind the validator. An observer elsewhere in the tree that fires
   during a takeover abort is evaluated after that abort rather than before it. No observer can sit
   under a retaining composite, and no test covers this.
6. **P9-001 reevaluation requests** (managed layer, not covered by the ADR). A request for a memory
   composite below a retaining composite that has not yet done its guard pass waits for that pass. It
   is applied when the pass resumes the branch, and dropped if the pass ends the composite or a guard
   takes over. Under a v1 reactive ancestor the request is skipped as before.
7. **Abort over a takeover in progress.** If an abort from above arrives while a takeover abort is
   traversing the retained branch, the retained remainder is aborted first, then the held guard branch.
   Both are deepest-first and complete. The reverse order of rule 3 (guard first) is not possible here,
   because the retained abort is already under way.

## 6. Open issues

- Stage 2: native implementation, equivalence on the five new golden cases (add them to the native
  lists then), hot reload v1↔retaining, simulation-container test of example 2' (race with a game
  completion). Until then the simulation container and production assets cannot use the new nodes.
- Stage 3: IL2CPP measurement (`act-retain16`, `io-retain16`).
- The `GeneratedTreeRuntimeDefinitionV2.TryCreate` rejection has no automated test (§3).
- Known pre-existing failure unchanged: `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`.

---

# P10-013 evidence — stage 2 (native executor), spike outcome (2026-11-18)

Written before the main stage-2 work, as the task card requires.

**Question.** How does `NativeLifecycleMachineV1` hold the retained branch while a guard branch
runs: (a) reuse the `Parallel` suspended-frame storage, or (b) stack the guard frames above the
retained branch with an explicit parent index?

**(b) rejected.** The machine treats `_frames[0 .. Depth)` as the active path with an implicit
parent at `index - 1` in many places: `TryActivate` (cooldown parent at `Depth - 2`), `PopFrame` /
`PopAbortedFrame` (parent at `Depth - 1`), `TrySuspendParallelBranch` (owner search downward),
`ScheduleReactiveReplacement` / `ScheduleExpiredTimeout` (shallowest-first scans), `CopyActivePath`
(diagnostics, `DescribeAgent`). `ParentFrameIndex` is written but no dispatch path reads it. A
non-contiguous stack would change every one of these v1 paths.

**(a) chosen, with one change of owner.** The retained branch is moved into the suspended-frame
region at the top of the frame array (`[SuspendedFrameStart, Length)`), with the same copy and
compaction as a suspended `Parallel` branch. The guard branch then runs on the ordinary contiguous
stack above the owner. On a takeover the guard branch is moved into the same region ("held"), the
retained branch is restored and aborted, and the held branch is restored afterwards. The segment
records are not `NativeParallelBranchStateV1` entries (their count is fixed by the program's
parallel child count) but fields of the internal `NativeLifecycleControlV1`: at most one retaining
composite holds parked frames at any time, because a guard branch is a new activation (nothing in it
can be in a guard pass) and a resumed branch is unparked before a deeper composite parks. The
compaction in `RestoreSuspendedBranch` also shifts these two segment records.

**Capacity.** No change. The arena allocates `NodeCount` frames
(`NativeInstanceArenaCapacityV1.TryDerive`), and every node has at most one live frame (active,
suspended, parked or held): the guard child and the retained child are different children of the
owner, and a node's frame exists only while its parent's is live. So the active stack plus all
segments never exceed `NodeCount`, also with parking. The existing region checks stay as defensive
`NativeInstanceCapacityExceeded` failures.

**Per-frame state.** "Guard pass already done in this update" reuses `NativeFrameStateV1.LastUpdateId`
for retaining frames (written today only for leaves). The guard cursor and the retained ordinal + 1
live in the composite's 8-byte activation memory from stage 1.

**Contracts.** No compiled-format, ABI v2, trace-format or public API change. Internal only:
two `NativeLifecycleNodeKindV1` values, fields in `NativeLifecycleControlV1` (internal struct).

# P10-013 evidence — stage 2 (native executor and equivalence), result (2026-11-18)

## What changed

- **`Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs`.** Two internal kinds
  `RetainingReactiveSequence = 12`, `RetainingReactiveSelector = 13` (appended). Binding check:
  8-byte, 4-aligned activation memory. Update start goes through `PrepareUpdateStart`: it finds the
  shallowest retaining frame that is running a child and has not had its guard pass this update (the
  *cut*); v1 expired-timeout and v1 reactive-replacement scheduling scan only the frames above the cut
  (unchanged when there is no retaining frame); otherwise the retained branch is parked at the cut.
  The same processing runs when a Parallel branch is restored and when a retained branch is resumed
  (deeper retaining composites get their own pass, as in the reference). Guard pass: `StartGuardPass`
  (1 step, `ChildSelected`), guards selected and accepted as ordinary children, `ResumeRetainedBranch`
  (1 step). A guard that ends the composite or starts running begins the retained abort
  (`BeginRetainedAbort`, reason `Explicit`, source = the guard child): from a running leaf the abort
  chains directly; after a Parallel visit the rest is replayed later (`RetainVisitRestPending`) with no
  extra step. `PopAbortedFrame` completes the boundary at the owner (`CompleteRetainedAbortBoundary`):
  End → the composite ends with the guard's result; Takeover → the held guard branch is restored. An
  abort from above that reaches an owner with parked frames unparks them in the same advance
  (`TryUnparkForAbort`); an external abort during an End/Takeover abort supersedes it
  (`RetainAbortSuperseded`, precision 7). `NativeLifecycleControlV1` (internal) gained the retain
  phase/segment fields and an abort-source record (`HasAbortSource`, `AbortSourceNode`) used by the
  equivalence tests; the trace format is unchanged.
- **Segment restore.** `RestoreSuspendedBranch` delegates to `TryRestoreSegment`. For programs
  without a retaining kind it is the original algorithm, byte for byte. Programs with a retaining kind
  use an in-place three-reversal rotation, because the original copy overwrites a lower suspended
  segment when no frame is free (finding F-S2-1 below), which parking makes reachable.
- **Classification.** `NativeReactivePolicyV1` (`IsRetaining`, `IsRetainingTypeId`, `Matches`; the
  stage-1 `IsUnsupportedRetaining` removed), `NativeHotReloadInstance.ClassifyKind`,
  `NativeHotReloadStateMigration.IsCompositeKind`, `TreeGroup.Debug.CoreTypeNames`.
  `ProductionTreeAgentCore`, `GeneratedTreeDispatchAdapterV2` and `SimulationProgramMapV1` classify
  through `ClassifyKind` and needed no edit.
- **Stage-1 rejection removed:** `GeneratedCompiledProgramV2` (`AddUnsupportedNativeNodes`, `AIBT3012`)
  and `GeneratedTreeRuntimeDefinitionV2`.
- **Capacity:** unchanged (spike outcome above).
- **Docs:** `simulation-container.md` (retaining composites for guarded actions; the two-long-steps
  pitfall kept), `CHANGELOG.md`, the task card, `work-items.json`. Generated docs: no text depended on
  the stage-1 limitation, nothing to regenerate.

## Tests

New:

| Test | Assembly | What it pins |
|---|---|---|
| `RetainingReactiveNativeEquivalenceTests` (31: 30 pass, 1 ignored) | Runtime | 22 scenarios (ADR examples 1–6 and variants, two guards, empty pass, nesting ×3, retaining inside Parallel, Parallel guard takeover, Parallel in the retained branch aborted by a guard, v1 inside and above retaining, Timeout above, selector guard ends, empty composites, retained branch completes): native records equal reference records including abort reason and source, for Immediate, a one-step budget and the batched job; semantic steps equal for every scenario without a Parallel. Plus: empty pass costs 2 steps; abort from above during a guard pass (3 stop points) and between updates; tree stop during a takeover (precision 7); memory layout during a pass; worst-case takeover fits in NodeCount frames. Ignored: F-S2-1. |
| `RetainingReactiveHotReloadTests` (6: 5 pass, 1 ignored) | Runtime | Unchanged program migrates the retained branch and resumes it; structural edit under a retaining composite resets its cursor and aborts the retained branch with `HotReload`; v1↔retaining is `IncompatibleRestart`, restart root = the node, the restarted subtree runs the new semantics (both directions); retaining on the active path changed to v1 restarts through `NativeHotReloadFullRestart`. Ignored: F-S2-2. |
| `RetainingReactiveNativeAcceptanceTests` (6) | Integration | Replaces `RetainingReactiveNativeRejectionTests`: the generated compiler accepts both type IDs (no `AIBT3012`), the native build classifies and runs them. |
| `NativeExecutionEquivalenceTests`, 5 new cases | Integration | `retaining-wait/crew/takeover/pitfall/timeout` golden cases through `NativeBehaviorCaseAdapter` under Immediate, Budgeted (1 step per segment), BatchedJobsSameFrame and Pipelined. |
| `JobsPolicyEquivalenceTests.Retaining` (5) | Integration | Retaining sequence / effect / selector-in-parallel-loop populations: default, Immediate, BatchedJobsSameFrame and JobsAgentFrame frame-by-frame identical (trace records, results, effects, Tree blackboard bytes); retaining emitter entered once vs v1 every update; 0 managed allocations in 200 warmed-up frames under each of the three forced policies. |
| `TreeSimulationActionTests.Retaining` (4) | Integration | Under a retaining selector (in place and jobs): the job starts once, is polled, its completion is consumed (JobDone), the next job is preempted once (`Preempted`, Abort value), same-step completion + alarm → preemption wins, `StaleAction` afterwards; active path and `DescribeAgent` show top → job (not the guard), traced and untraced. The F2 test is unchanged. |

Test helpers changed additively: `P113Fixture` exposes the compiled program and flattened nodes,
`ScriptedReferenceLeaf.NextScriptedStatus()`, `RetainingTree.Names`; `NativeBehaviorCaseAdapter`
learned the two kinds and the `count-to-two/three` and `failure-except-update-3` fixture leaves, and for
programs with a retaining kind reads composite results, abort reasons/sources and exit reasons from
the machine (programs without one keep the original approximation).
`JobsPolicyEquivalenceTests` and `TreeSimulationActionTests` became `partial` to hold the new tests in
separate files; no existing test, golden case or fixture changed.

Final pass (one run per assembly, 2026-11-18, Unity 6000.6.9f1):

| Assembly | Baseline | Now |
|---|---|---|
| AIBT.Runtime.Tests | 743/743 | 778/780 (2 ignored: F-S2-1, F-S2-2) |
| AIBT.Integration.Tests | 208/208 | 222/222 (6 rejection tests replaced by 6 acceptance tests; +14 new) |
| AIBT.BehaviorCases.Tests | 65/65 | 65/65 |
| AIBT.NativeBurstDispatch.Tests | 79/79 | 79/79 |
| AIBT.NativeTreeAgentBlackboard.Tests | 33/33 | 33/33 |
| AIBT.NativeSharedBlackboard.Tests | 36/36 | 36/36 |
| AIBT.CodeGen.ContractTests | 6/6 | 6/6 |
| AIBT.Editor.Tests | 649/650 | 649/650 (the known `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`) |

`Tools~/Verification/Verify-Static.ps1` passes (177 work items);
`Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt` is byte-identical to HEAD.

## Public API delta

None. All new members are internal (`NativeLifecycleNodeKindV1` values, `NativeLifecycleControlV1`
fields, `NativeReactivePolicyV1` helpers).

## Deviations and notes

1. **Segment restore algorithm** differs for programs that contain a retaining kind (rotation instead
   of copy). Same result whenever the original is correct; correct where the original corrupts
   (F-S2-1). Programs without a retaining kind run the original code.
2. **Step counts with Parallel.** The native machine counts `ParallelBranchSuspended` as a semantic
   step and the reference does not (pre-existing, v1). The equivalence tests compare steps only for
   scenarios without a Parallel; records are compared for all.
3. **Stage-1 worked examples** run through the native twin harness in `Tests/Runtime`
   (`RetainingNativeTwin`), not through `NativeBehaviorCaseAdapter`, which only knows the
   integration fixture leaves; the five golden cases do run through the adapter.
4. **Capacity overflow** cannot be reached with a valid machine: `TryCreate` requires at least
   NodeCount frames and each node has at most one live frame. The overflow branches
   (`NativeInstanceCapacityExceeded`, `InstanceFrames`) stay as defensive failures and are not
   exercised by forcing an invalid state; the worst case (takeover with every node live) is tested.
5. **One-step budget = unlimited** is verified at the machine level (budget driver with resume inside
   the same update). A host-level `StepBudget` spreads an update over frames by design, so it is not
   frame-by-frame comparable.
6. **Mid-update active path.** During a guard pass `CopyActivePath` shows the guard branch (the
   retained branch is parked). Between updates, which is when `TryGetActivePath`/`DescribeAgent` are
   called, it shows the retained branch.
7. **Hot reload with an abort pending at update start** (e.g. after a structural-edit reset) skips
   the guard pass of that update, as v1 reactive replacement does; the composite restarts from its
   reset cursor.

## Open issues (pre-existing, v1, not changed)

- **F-S2-1.** `RestoreSuspendedBranch` copies a segment down before shifting the lower segments; with
  no free frame (frames = NodeCount) it overwrites a lower suspended branch. Repro:
  `parallel [memory-sequence [ok, work], other]` ticks `work` twice and never `other` on update 2.
  Pinned by the ignored `V1ParallelWithTwoSuspendedBranchesMatchesTheReference`. The fix is to use the
  rotation for every program (drop the `_safeSegmentRestore` condition); left for the owner because v1
  behaviour must not change in this stage.
- **F-S2-2.** `NativeHotReloadFullRestart.TryRestart` begins an update and then requests a `HotReload`
  abort; a v1 reactive composite with a running branch has already scheduled its reactive
  replacement at update start, so `TryRequestAbort` refuses (`NativeLifetimeStateInvalid`). The API
  has no production caller. Pinned by the ignored `AV1ReactiveCompositeWithARunningBranch_CanBeFullyRestarted`.
- **Observation, not verified:** in the `ParallelAbortPending` path an abort from above of a Parallel
  with running branches sets `AbortTargetDepth = 0`, which may widen a Timeout or v1 reset target that
  ends inside the Parallel. Read only; no test written.

# P10-013 evidence — stage 3 (IL2CPP measurement), result (2026-11-18)

Benchmark harness only; no runtime code, spec, test expectation, ABI or existing benchmark tree
changed. Raw data, logs, tables and scripts: `C:\UnityProjects\_aibt_acceptance\perf-data\P10-013\`
(`build_players.ps1`, `run_measurements.ps1`, `retain_table.py` → `tables.md`). Every number below is
in `tables.md`, which `python -X utf8 retain_table.py > tables.md` recomputes from `raw/`.

## Verdict per ADR §5 stage-3 target

| target | result | verdict |
|---|---|---|
| `act-retain16`: 0 `Preempted` cancels in steady state | 0.00 cancels per tick of any reason, 23 and 0 workers, every run (v1 `act-reactive16`: 200 `Preempted`/tick) | **met** |
| consumed completions = reported − in flight | 166.60 reported = 166.60 consumed per tick, in-flight change over the window 0, lost 0 (v1: 100 reported, 0 consumed, 100 lost) | **met** |
| orders > 0 | 166.60 per tick, one per consumed completion (v1: 0) | **met** |
| `Step` `act-retain16` / `io-retain16` ≤ 1.2 | 2.039 (1.913–2.118) at 23 workers, 2.347 (2.255–2.431) at 0 workers | **missed**; cause: 2.33× the thinks (below) |
| `io-retain16` `Step` median not above `io-reactive16` | 23 workers 236.3 vs 238.0 µs, paired 0.990 (0.889–1.047); 0 workers 2480.1 vs 2670.7 µs, paired 0.926 (0.906–0.939) | **met** (at 23 workers the difference is inside the noise; at 0 workers −7.4 %, inside the ADR's 5–10 % estimate) |
| v1 trees head/base within run-to-run noise | hosts 0.996–1.023 (noise 4.5 %); container reactive trees 0.987–1.046, pooled 1.000–1.030; loop trees at 0 workers 0.919–1.050; **loop trees at 23 workers pooled 1.079 (`io-loop16`) and 1.090 (`act-loop16`), +5–6 µs per Step, in both run orders, noise 7.2–8.7 %** | **met, except the loop trees at 23 workers: missed by a small margin** (at the edge of the noise, but reproduced) |
| 0 bytes per tick in `Step` | 0 B mean and 0 B in the worst frame, every tree, both Players, both worker counts | **met** |

**Why `act-retain16` / `io-retain16` is about 2, not ≤ 1.2.** The target assumed that `act-retain16`
thinks as often as `io-retain16`. It does not:
- The retained job reads its completion, gives an order and succeeds. The repeater starts the next
  job in the same think.
- The game completes each action 3 ticks later, and the completion wakes the agent. Every agent
  therefore runs a job cycle every 3 ticks: 166.6 starts, completions, consumes and orders per tick
  (500 / 3).
- That adds 166.6 completion wake-ups per tick to the 100 scheduled thinks. Diagnostic run D counts
  233.3 thinks per tick, against 100 for `io-retain16` and 200 for v1 `act-reactive16`.
- Per think, `act-retain16` costs 2.03 µs against 2.36 µs for `io-retain16` at 23 workers (0.859×),
  and 24.83 against 24.80 µs at 0 workers (1.001×).

The retaining composite adds no per-think cost. The ratio measures how much more work the tree does
now that its actions complete. ADR §1.3 already said that the wake-up thinks remain; the ≤ 1.2 target
did not account for the shorter job cycle. Against its v1 twin, `act-retain16` costs
1.054 (0.957–1.092)× `act-reactive16` at 23 workers and 1.012 (0.986–1.074)× at 0 workers, and does
166.6 useful job cycles per tick instead of 0. Its `Step` p99 is higher than v1's (734.3 vs 606.9 µs
at 23 workers, 8827.7 vs 6484.4 µs at 0 workers); not investigated.

**v1 loop trees at 23 workers.** These trees contain no reactive composite.
- B (base, head, head, base; 2 runs per block): `io-loop16` 1.061, `act-loop16` 1.141.
- C (head, base, base, head; 4 runs per block, added for this): 1.084 and 1.082.
- Pooled 12+12 runs: 64.6 → 69.7 µs and 64.1 → 69.8 µs.
- At 0 workers the same trees give head/base 0.92–1.05 (pooled 0.959 and 0.926, noise 17 %). The
  host check gives 1.023 / 0.998 (`shared-loop16`, 500 / 2000 agents).

The difference appears only with job workers and only on the cheapest trees (Step ≈ 65 µs, about
0.65 µs per think). That points to the jobified update path rather than per-node work. It was not
investigated: reported, not fixed, as the brief requires.

## Method

**Players.** IL2CPP, Release, High stripping, non-development, Unity 6000.6.9f1, Intel Core Ultra 9
275HX (24 logical), Windows 11.
- **Project.** `C:\UnityProjects\_aibt_acceptance\P9Samples-6069`, not open in any Editor.
  `build_players.ps1` builds it in batch mode through the harness's `Invoke-Perf.ps1 -Step Prepare`
  and `-Step Build`.
- **Package.** The manifest pointed at a `git archive` copy of each commit.
- **Restore.** Harness, manifest and lock file were backed up to
  `P9Samples-6069-backup-before-p10013` and restored afterwards. They are byte-identical to the
  backup; the manifest points at `aibt-659a8b1-6069` again.

| Player | AIBT | Harness |
|---|---|---|
| `Build\P10013` (head) | `d47f948` (`aibt-d47f948-6069`) | this stage's, with `io-retain16` / `act-retain16` baked |
| `Build\P10013-base` (base) | `1dd7dd0` (`aibt-1dd7dd0-6069`) | the same; the retain trees are skipped (`AIBT2021 Node type is not present`) |

Checked in the built Players: the head metadata contains `aibt.core.reactive-sequence-retaining`
and the `io-retain16` asset; the base contains neither.

**Trees.** `act-retain16` and `io-retain16` (`Tools/gen_trees.py`, `retain_trees`) are byte-identical
to `act-reactive16` / `io-reactive16` except for `treeId`, `name` and the root type
(`aibt.core.reactive-sequence-retaining`). Existing trees are unchanged.
- **Pitfall check (ADR §2.5 example 5), done before measuring.** The 14 children before the last are
  `sense` and 13 `wait(ticks: 1)`, each completing in its first tick, so they are the guards. The
  last child (`hold`, or the repeater around `aibt.perf.job`) is the only multi-update child and is
  the one retained.
- **Sanity run.** A one-run check (not kept) showed the intended behaviour before the measurement.
- **Measured confirmation.** No cancels, every completion consumed, one order per consume;
  `io-retain16` gives the same 100 orders per tick as `io-reactive16`.

**A — `sim-retain`, head Player.**
- **Agents and timing.** 500 agents, `Spread(5)`, jobs on, 200 warm-up and 1000 measured ticks.
- **Game side.** The `sense` input is written every tick and `order` commands are read; the game
  completes every started action 3 ticks later.
- **Order.** Six trees per run, ABC…FF…CBA (`io-loop16`, `io-reactive16`, `io-retain16`,
  `act-loop16`, `act-reactive16`, `act-retain16`).
- **Runs.** 6 Player runs per worker count, in blocks of 3 runs ordered 23, 0, 0, 23 workers (23 is
  the default `JobWorkerCount`). Each tree has 12 instances per cell (two per run).
- **Paired ratios.** Per run, the mean of a tree's two halves; the ratio of two trees in the same
  run; the median (min–max) over the 6 runs.

**B — v1 regression, both Players.**
- **Sets.** `sim-v1` (`io-loop16`, `io-reactive16`, `act-loop16`, `act-reactive16`; settings as in
  A; one instance per tree per run) and `host-check` (`NativeTreeAgent`, default profile,
  `shared-loop16` / `shared-reactive16` at 500 and 2000 agents).
- **Order.** Player runs ABBA: base, head, head, base, 2 runs per block (4+4 per side).
- **Workers.** Container at 23 and 0 workers, hosts at the default worker count.

**C — replication of the B container cells** (added after B; see deviations). Head, base, base,
head, 4 runs per block (8+8 per side), 23 and 0 workers.

**D — thinks per tick (diagnostic, not timing).** Set `sim-retain-thinks`, one run, head Player,
23 workers. After every `Step` the harness reads every agent's `AgentDebugState` and counts the
agents whose `LastThinkTick` is this tick, with their reasons.

**Noise.** Run-to-run noise is the largest same-Player block-to-block difference of the medians.
- **Hosts:** 4.5 %.
- **Container, 23 workers:** 8.7 % (B) and 7.2 % (C).
- **Container, 0 workers:** 17.4 % (B) and 17.0 % (C). The loop trees dominate this figure: at 0
  workers they are bimodal between sessions (`io-loop16` base median 96.5 µs in B, 159.9 µs in C,
  minutes apart). The reactive trees at 0 workers moved ≤ 4.6 % block to block.
- **Inside one A run:** the paired ratios vary by about ±5 % (23 workers) and ±2 % (0 workers).

## Tables

### A. `sim-retain` (head `d47f948`), median (min–max) of 12 instances per cell

| workers | tree | Step µs median | Step µs p99 | bytes/tick | starts/tick | cancels/tick | reported/tick | consumed/tick | orders/tick | faults |
|---:|---|---:|---:|---:|---:|---|---:|---:|---:|---:|
| 23 | `io-loop16` | 64.9 (62.6–89.8) | 187.0 (172.7–223.4) | 0 | 0 | 0 | 0 | 0 | 5.47 | 0 |
| 23 | `io-reactive16` | 238.0 (229.8–269.9) | 405.3 (358.4–443.8) | 0 | 0 | 0 | 0 | 0 | 100.00 | 0 |
| 23 | `io-retain16` | 236.3 (216.5–258.9) | 390.4 (344.6–447.4) | 0 | 0 | 0 | 0 | 0 | 100.00 | 0 |
| 23 | `act-loop16` | 65.1 (62.3–72.0) | 215.6 (196.7–230.6) | 0 | 5.47 | 0 | 5.19 | 5.19 | 5.19 | 0 |
| 23 | `act-reactive16` | 454.9 (422.6–518.7) | 606.9 (575.4–693.3) | 0 | 200.00 | Preempted 200.00 | 100.00 | 0.00 | 0.00 | 0 |
| 23 | `act-retain16` | 473.6 (451.7–533.0) | 734.3 (688.8–805.9) | 0 | 166.60 | 0 | 166.60 | 166.60 | 166.60 | 0 |
| 0 | `io-loop16` | 98.3 (91.5–135.2) | 1406.8 (1320.6–1590.9) | 0 | 0 | 0 | 0 | 0 | 5.47 | 0 |
| 0 | `io-reactive16` | 2670.7 (2575.5–2861.2) | 3185.1 (2797.7–3683.5) | 0 | 0 | 0 | 0 | 0 | 100.00 | 0 |
| 0 | `io-retain16` | 2480.1 (2408.2–2596.9) | 2854.7 (2660.0–3170.4) | 0 | 0 | 0 | 0 | 0 | 100.00 | 0 |
| 0 | `act-loop16` | 97.7 (96.2–129.9) | 1774.2 (1726.2–1939.3) | 0 | 5.47 | 0 | 5.19 | 5.19 | 5.19 | 0 |
| 0 | `act-reactive16` | 5702.6 (5599.5–6195.4) | 6484.4 (6102.7–7486.8) | 0 | 200.00 | Preempted 200.00 | 100.00 | 0.00 | 0.00 | 0 |
| 0 | `act-retain16` | 5792.5 (5577.0–6253.0) | 8827.7 (8419.1–9648.5) | 0 | 166.60 | 0 | 166.60 | 166.60 | 166.60 | 0 |

The per-tick flows are identical in every instance (deterministic trees). `act-loop16` starts 5.47
and consumes 5.19 per tick. The difference is actions still running when the window ends, the same
in every instance (300 live actions at both ends of the window, 0 completed and not consumed).

| paired ratio (median, min–max over 6 runs) | 23 workers | 0 workers |
|---|---:|---:|
| `act-retain16` / `io-retain16` | 2.039 (1.913–2.118) | 2.347 (2.255–2.431) |
| `act-reactive16` / `io-reactive16` | 1.860 (1.834–2.003) | 2.111 (2.102–2.236) |
| `act-loop16` / `io-loop16` | 0.960 (0.863–1.038) | 1.000 (0.836–1.187) |
| `io-retain16` / `io-reactive16` | 0.990 (0.889–1.047) | 0.926 (0.906–0.939) |
| `act-retain16` / `act-reactive16` | 1.054 (0.957–1.092) | 1.012 (0.986–1.074) |
| `io-retain16` / `io-loop16` | 3.475 (3.113–3.694) | 25.077 (20.701–26.407) |

### D/E. Thinks per tick and Step per think

| tree | thinks/tick | scheduled | with a completion | µs per think, 23 workers | µs per think, 0 workers |
|---|---:|---:|---:|---:|---:|
| `io-loop16` | 100.00 | 100.00 | 0.00 | 0.65 | 0.98 |
| `io-reactive16` | 100.00 | 100.00 | 0.00 | 2.38 | 26.71 |
| `io-retain16` | 100.00 | 100.00 | 0.00 | 2.36 | 24.80 |
| `act-loop16` | 105.19 | 100.00 | 5.19 | 0.62 | 0.93 |
| `act-reactive16` | 200.00 | 100.00 | 100.00 | 2.27 | 28.51 |
| `act-retain16` | 233.30 | 100.00 | 166.60 | 2.03 | 24.83 |

µs per think = the A Step median divided by the D thinks per tick. D's own Step times are perturbed by
the count and are not used.

### B and C. v1 regression, base `1dd7dd0` vs head `d47f948`

Step µs median; each cell is the median (min–max) of the runs.

| workers | tree | B base (4) | B head (4) | B head/base | C base (8) | C head (8) | C head/base | pooled head/base (12+12) |
|---:|---|---:|---:|---:|---:|---:|---:|---:|
| 23 | `io-loop16` | 64.6 (60.8–66.1) | 68.5 (65.0–72.8) | 1.061 | 64.6 (62.2–67.2) | 70.0 (64.4–73.5) | 1.084 | 1.079 |
| 23 | `io-reactive16` | 245.2 (235.1–249.0) | 242.0 (231.7–245.2) | 0.987 | 241.8 (228.4–268.9) | 242.2 (230.0–273.4) | 1.002 | 1.000 |
| 23 | `act-loop16` | 61.0 (59.5–62.1) | 69.6 (68.5–73.7) | 1.141 | 64.8 (63.2–67.5) | 70.2 (66.0–82.7) | 1.082 | 1.090 |
| 23 | `act-reactive16` | 437.7 (420.2–492.8) | 458.0 (445.4–474.1) | 1.046 | 471.5 (465.4–486.4) | 478.9 (440.8–527.4) | 1.016 | 1.012 |
| 0 | `io-loop16` | 96.5 (89.8–106.2) | 101.3 (94.2–109.7) | 1.050 | 159.9 (133.9–169.2) | 151.0 (115.1–179.1) | 0.944 | 0.959 |
| 0 | `io-reactive16` | 2556.7 (2504.8–2708.1) | 2605.2 (2595.4–2617.1) | 1.019 | 2925.3 (2873.1–2999.2) | 2910.1 (2634.2–2997.5) | 0.995 | 1.001 |
| 0 | `act-loop16` | 105.0 (90.3–115.2) | 96.5 (93.4–132.6) | 0.919 | 160.3 (131.4–173.1) | 155.7 (113.5–186.9) | 0.971 | 0.926 |
| 0 | `act-reactive16` | 5592.3 (5485.8–5829.6) | 5762.1 (5751.1–5766.4) | 1.030 | 6163.0 (5839.9–6291.5) | 6236.8 (5996.9–6461.1) | 1.012 | 1.030 |

The per-tick flows (starts, cancels, consumed, orders) are identical on both Players for every tree:
`act-reactive16` 200, 200 `Preempted`, 0, 0; `act-loop16` 5.47, 0, 5.19, 5.19. Bytes per tick 0,
faults 0. The p99 values and block ratios are in `tables.md`.

| host tree | N | base bracket µs (4) | head bracket µs (4) | head/base | base drive µs | head drive µs | head/base drive |
|---|---:|---:|---:|---:|---:|---:|---:|
| `shared-loop16` | 500 | 418.6 (408.9–457.9) | 428.1 (419.9–435.2) | 1.023 | 253.3 | 260.9 | 1.030 |
| `shared-loop16` | 2000 | 2099.3 (1980.7–2215.2) | 2095.7 (1918.4–2219.7) | 0.998 | 1298.8 | 1282.3 | 0.987 |
| `shared-reactive16` | 500 | 2479.1 (2474.2–2705.6) | 2469.8 (2461.4–2559.3) | 0.996 | 2279.0 | 2271.3 | 0.997 |
| `shared-reactive16` | 2000 | 10266.6 (10004.2–10464.8) | 10284.8 (9994.0–10429.7) | 1.002 | 9301.8 | 9323.5 | 1.002 |

For context only (other sessions, not compared): P10-012 measured `io-loop16` 61 / 92 µs and
`io-reactive16` 238 / 2582 µs; this session's head gives 64.9 / 98.3 and 238.0 / 2670.7 µs.

## Harness changes (`Benchmarks~/Phase10/PlayerPerf/`)

- **`Tools/gen_trees.py`:** `retain_trees()` generates `io-retain16` and `act-retain16`.
- **`PerfBuild.cs`:** `OptionalNativeTrees` (the two retain trees) are baked only when the package
  has the node types, so the base Player builds from the same harness.
- **`PerfRunner.cs`:**
  - New sets `sim-retain`, `sim-v1` and `sim-retain-thinks`.
  - `runs.jsonl` `sim` gains `cancelledByReasonMeasured` and `liveStart`, `liveEnd`,
    `liveCompletedStart`, `liveCompletedEnd` (`TreeGroup.TryCopyLiveActions`, read outside the
    timing). Consumed = started − cancelled − Δlive, since a consume has no event.
  - Only with `SimCountThinks`: `thinksMeasured` and its reasons.
- **`README.md`:** section "Retaining reactive composites (P10-013 stage 3)".

## Deviations from the method in the brief

1. **Replication C added.** B's container cell at 23 workers showed `act-loop16` head/base 1.141 on
   4+4 single-instance runs. C repeats both container cells in the reversed order with 8+8 runs, so
   one block does not decide the verdict. C ran after D in the same session.
2. **Thinks were counted in a separate diagnostic run (D)**, not in the timing runs. The per-think
   costs combine A's Step medians with D's counts; this holds because the per-tick flows are
   deterministic and identical between A and D.
3. **The Players were built twice.** The measurement uses only the second build (the first lacked
   the think counter). Both Players have the identical harness; the head has two extra baked trees.
4. **B container cells** run one instance per tree per run (`sim-v1`), with ABBA at the Player-run
   level rather than inside a run (two Players cannot share a run).
5. **A order.** ABBA by worker-count blocks (23, 0, 0, 23) plus ABC…CBA inside each run; "6 runs per
   cell" gives 12 instances per tree.
6. **Other processes.** The `C:\UnityProjects\Modules` Editor was open (idle, about 0 CPU, checked
   before each build and session, no tests running). An Editor for another project (Unity
   7000.0.0a8) ran throughout. Neither was touched.
7. **Steady state** = the whole 1000-tick measured window after 200 warm-up ticks; the flows were
   constant across all instances.

## Not measured

- The retaining **selector** (no benchmark tree; the ADR target is about the sequence).
- The cause of the loop-tree difference at 23 workers and of `act-retain16`'s higher p99.
- Retaining trees on `NativeTreeAgent` hosts (the host check covers v1 trees only).
