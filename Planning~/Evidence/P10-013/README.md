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
