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
