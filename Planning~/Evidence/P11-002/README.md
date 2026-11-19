# P11-002 evidence: native node library

Decision: `Documentation~/decisions/ADR-P11-002-native-node-library.md` (owner decision of 2026-11-19,
AIBT-050, every §8 recommendation). Stage 1 of ADR §7 is implemented (section below) and was
accepted by the orchestrator on `4af5571` ("Acceptance run" at the end of its section). Stage 2 was
approved by the owner on 2026-11-19, is implemented (section "Stage 2" after the stage 1 material)
and was accepted by the orchestrator on `bad2892`. Stage 3 (random nodes; hosts seed random streams)
was approved by the owner on 2026-11-20. Stage 4 is not started; it needs the owner's go-ahead.

## Stage 1 — blackboard and flow nodes (2026-11-19)

Base: `feature/aibt-consumer-workflow` at `ccde239`. Editor 6000.6.9f1, project "Modules"
(Pipeline port 7810). Tests ran with the async `Tools/Unity/Invoke-UnityTests.ps1` after
`Invoke-UnityRecompile.ps1`, touched classes only, one runner at a time. Raw outputs, dumps,
screenshots and the scripts that made them are in `C:\UnityProjects\_aibt_acceptance\P11-002\stage1\`
(outside the repository).

Commits (submodule only):

| Commit | Content |
|---|---|
| `442d9aa` | the 20 nodes |
| `760a1b3` | host fix allowed by the owner (ADR "Owner decisions" item 7) and its regression test, with the library test kit |
| the commits after `760a1b3` | catalog document, bake message, tests, demo fixture, docs, CHANGELOG, this file |

### The 20 nodes

All are in shard `aibt.native.standard` (assembly `AIBT.StandardNodes`). The shard version stays 1.
Each node has node version 1, Tree scope only, `deterministic: true`, cost `Trivial`, and an
`[AibtNodeDocumentation]` whose "when not to use" names the `aibt.unity.*` counterpart. Sources:
`Authoring/Registry/Generated/StandardNative/StandardNativeConditions.cs`, `StandardNativeActions.cs`
and `StandardNativeFlowNodes.cs`.

| Node | Kind | Parameters | Bindings | Statuses |
|---|---|---|---|---|
| `int-at-least` | observer condition | `threshold` UInt32 | `key` Int32 read | S/F |
| `int-equals` | observer condition | `value` UInt32 | `key` Int32 read | S/F |
| `int-keys-at-least` | observer condition | — | `left`, `right` Int32 read | S/F |
| `float-keys-at-least` | observer condition | — | `left`, `right` Float32 read | S/F |
| `float3-within-distance` | observer condition | `distance` Float32, `flat` Bool | `a`, `b` Float3 read | S/F |
| `float3-is-set` | observer condition | — | `key` Float3 read | S/F |
| `set-bool` | action | `value` Bool | `key` Bool write | S/F |
| `set-int` | action | `value` UInt32 | `key` Int32 write | S/F |
| `set-float3` | action | `x`, `y`, `z` Float32 | `key` Float3 write | S/F |
| `copy-bool` / `copy-int` / `copy-float` / `copy-float3` | action | — | `from` read, `to` write | S/F |
| `increment-int` | action | `amount` UInt32 | `key` Int32 read-write | S/F |
| `increment-float` | action | `amount` Float32 | `key` Float32 read-write | S/F |
| `toggle-bool` | action | — | `key` Bool read-write | S/F |
| `float3-distance` | action | `flat` Bool | `a`, `b` Float3 read, `result` Float32 write | S/F |
| `succeed` | action | — | — | S |
| `fail` | action | — | — | F |
| `idle` | action | — | — | R |

Native/Blackboard: the 17 blackboard nodes. Native/Flow: `succeed`, `fail`, `idle`.

### Contract choices (made in stage 1, not fixed by the ADR)

1. **A failed read gives Failure.** No write happens after it.
2. **`int-at-least` / `int-equals` compare as `long`.** The `UInt32` parameter is compared with the
   `Int32` value as 64-bit numbers, so a threshold or value above 2147483647 never matches.
   Parameters cannot be negative (generator types: Bool, UInt32, UInt64, Float32). For a negative
   bound, compare two keys with `int-keys-at-least`.
3. **`set-int` with a value above 2147483647 fails without writing.** A negative value is written
   with `copy-int` from a key.
4. **`increment-int` wraps.** `unchecked(value + amount)`; an amount of 4294967295 subtracts one.
5. **Non-finite results fail without writing.** This covers `increment-float` (sum overflows to
   ±∞), `float3-distance` (squared length not finite) and `set-float3` (non-finite parameter). The
   runtime refuses a non-finite Float32 write with `InvalidEncoding` and latches a frame failure, so
   each node checks first.
6. **Distance comparisons use squared lengths.** `float3-within-distance` succeeds when
   `|a−b|² ≤ distance²`. A negative distance never succeeds; the managed `is-within-distance`
   squares it and would succeed. `flat` sets Y to 0 on both points.
7. **`float3-is-set` means "not (0, 0, 0)"** (−0 counts as 0), like the managed `is-vector3-set`.
8. **Flow nodes are actions with `AbortOnly` cancellation**, with status masks Success only,
   Failure only and Running only. `idle` runs until something aborts it.
9. **Observer conditions declare only `Tick` and `Evaluate`.** They have no Enter, Abort or Exit.
10. **Names.** Bindings are `key`, `left`/`right`, `a`/`b`, `from`/`to`, `result`. Parameters are
    `threshold`, `value`, `amount`, `distance`, `flat`, `x`/`y`/`z`. A shared internal
    `StandardNativeMath` holds the finiteness and distance helpers.

### Host fix (owner decision 7)

`Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:840` (`DispatchSlot.TryCreate`,
since P7-037) looked up a blackboard handle's configuration field as
`catalog.ConfigurationFields[ConfigurationFieldOrdinal]`. The ordinal is local to the node's
dispatch case, but the array holds the fields of every case. Every other site uses
`dispatchCase.FirstConfigurationField + ordinal`. A handle that is field 0 of its node always
worked. A handle at local field 1 or higher worked only when the global field with the same index
happened to sit at the same offset. That holds for the first case of a catalog (cases are ordered by
type ID), which is why Swarm Arena `steer`/`within-radius` and the samples ran. Ten of the new
nodes have such handles. With the shard in a catalog they failed loudly:

- a parameter at the wrong offset (`increment-int`, `increment-float`: `amount` sorts before `key`)
  failed bootstrap with `NativeCapacityPlanInvalid`, or `InvalidArgument` from
  `TreeSimulation.TryAddGroup`;
- a second handle at the wrong offset (`copy-*`, `*-keys-at-least`, `float3-within-distance`,
  `float3-distance`) bound two handles to one slot. `NativeBurstDispatchBindingValidationV2` refused
  that at the first update with `NativeLifetimeStateInvalid`.

The owner allowed the one-line fix on 2026-11-19 ("Да, чинить"; ADR "Owner decisions" item 7):

```csharp
var field = catalog.ConfigurationFields[(int)(dispatchCase.FirstConfigurationField + catalogBinding.ConfigurationFieldOrdinal)];
```

Nothing else under `Runtime/**` changed. No other site had the defect: the binding validation
searches within the case's own field range.

**Regression test.** `Tests/Integration/NativeRuntime/GeneratedDispatchFieldOrdinalRegressionTests.cs`
has 7 cases:
- a precondition: the test catalog has handles whose global field of the same index sits elsewhere;
- on `ProductionTreeHost`: a handle after a parameter (`increment-int` 5 + 3 → 8), a second
  handle (`copy-int`), two read handles (`int-keys-at-least` 1 ≥ 2 → Failure), and a third handle
  after a parameter (`float3-distance` (0,0,0)–(3,4,0) → 5);
- `TreeSimulation` in place and with Jobs.

On the old line 1/7 pass (`runs/04-regression-RED-old-line.txt`, only the precondition). With the fix
7/7 pass (`runs/05-after-fix-library-and-regression.txt`).

**Audit: can a tree that passes today read a wrong key silently?** No, in no catalog set of the
package. The script is `scripts/AuditFieldOrdinals.cs` and its output is
`scripts/audit-field-ordinals.txt`. It covers all 14 `[AibtCatalogSet]`s loaded in the Editor and
compares, for every binding at local field 1 or higher, the correct offset with the one the old
line read.
- Wrong offset: the 10 new nodes, in the 6 sets that list the shard. Also
  `aibt.tests.external-tree-effect` (`target`, field 1) in `StandardNativeProbeCatalog`,
  `JobsEquivalenceCatalog` and `NativeLibraryCatalog`. It read offset 0, which holds 0, and its
  access ordinal is 0 too. The value matches for every tree, so the node behaved correctly.
- The same offset by position: `aibt.tests.generated-node`, `external-tree-effect` in
  `ExternalTreeWriteFixtureCatalog`, and Swarm Arena `steer` (fields 2, 4) and `within-radius`
  (fields 1, 2).
- Read from source, not loaded: `Samples~/BurstNodes` `async-write-action` is the first case, so it
  matches. `Benchmarks~` PerfCatalog has no binding at field 1 or higher in the nodes its trees use.

For the 10 new nodes, offset 0 holds either the first handle (refused loudly, as above) or the
`amount` parameter. A non-zero amount fails bootstrap; an amount of 0 matches by accident. A silent
wrong read needs two misread bindings pointing at each other's handles. No catalog in the package
has that.

**Existing tests whose result changes with the fix:** none written before P11-002. Where the
offsets coincide, the read is the same, and `external-tree-effect` reads the same 0.
`JobsPolicyEquivalenceTests` gave 28/28 before the fix (`runs/03`) and after it (`runs/08`). Only the
new stage 1 tests and the regression test went from red to green. No expectation of an existing test
changed with the fix.

### Observers on the native path

ADR §7 stage 1 lists "Observer conditions guard a memory composite (abort on key change) natively".
Stage 1 found that native hosts do not run observer re-evaluation. `NativeObserverQueueV1` and
`NativeLifecycleMachineV1.TryApplyObserverTransition` have no caller under `Runtime/`. The host
trace of an observed tree has no observer event: `scripts/DiagnoseObserver.cs` →
`scripts/diagnose-observer.txt`, where the compiled program has 1 observer with mode Self and one
watched key, and update 0 runs the memory sequence to `idle` with no abort. The native golden adapter
(`Tests/Integration/NativeRuntime/NativeBehaviorCaseAdapter.cs:339-379`) simulates observers in
test code. This is existing behaviour, not caused by P11-002. Per the orchestrator's instruction
there was no runtime change.

**Deviation from ADR §7 stage 1 (ADR §7 not edited).** The guard is tested as what actually
happens. `Tests/Integration/NativeRuntime/NativeLibraryReactiveGuardTests.cs` (9 cases) checks that a
library condition as the first child of `aibt.core.reactive-sequence` aborts the memory sequence
under it at the next update after its key changes:
- by a node write (`increment-int` spends `fuel` 2 → 0 inside update 1, so update 1 ends Running
  with `done` = true, and update 2 gives Failure);
- by an external fact write (`float3-is-set` on `target`; `Fact(target, 0)` makes the next update
  Failure).

Each case runs with and without an `observer` block, and the results are identical. The two
observer cases first written in `NativeLibraryNodeTests` assumed an abort inside the update and
failed. They were removed. `[AibtObserverCondition]` stays on the six library conditions, and
`observer` JSON still compiles; no validation was added.

**Facts established.**
- (a) `TreeSimulation` behaves like the hosts. The abort comes at tick 2, in place and with Jobs, with
  or without the observer block (`TreeSimulation_AReactiveGuardAbortsTheMemorySequenceOnTheNextTick`,
  4 cases).
- (b) The managed side does not run it either. `Agents/Runtime/ManagedTreeInstance.cs:127` builds
  the reference executor with `ReferenceObserverConditionRegistry.Empty`. A `BehaviorTreeAgent`
  running the same tree with `aibt.unity.*` nodes and an observer block stops at the first queued
  re-evaluation with AIBT4002 "No explicit observer condition evaluator is bound for the compiled node
  type and version". There, fuel is 1 and `done` is false (`scripts/ProbeManagedObserver.cs` →
  `scripts/probe-managed-observer.txt`). Without the block the agent matches native: tick 1 fuel 0,
  `done` true, running; tick 2 Failure. The reference executor implements the specified queue only
  when it is given evaluators, as the golden cases do.
- (c) A native tree with an observer block on a memory composite and no reactive parent is not
  silently ignored. Validation refuses it with AIBT2034 `InvalidObserverContext` ("Observer must be a
  condition child of one reactive composite…"), tested by
  `AnObserverBlockOutsideAReactiveCompositeIsRefusedAtCompile`. The silent case is a valid observer
  under a reactive composite: it compiles, runs and changes nothing. The reactive composite's own
  re-tick then gives the guard at the next update.

**The specifications say otherwise** (not edited):

- `Documentation~/specifications/execution-semantics-v1.md:225`: "When a watched slot version changes
  during Execute, the observer is queued once. The queue is drained at the observer reevaluation
  point after the current atomic node step and before selecting the next ordinary frame transition."
- `Documentation~/specifications/update-phases-v1.md:8`: "After each atomic node step, queued
  blackboard observers are reevaluated in stable order before the next ordinary frame transition."

**Docs.** One paragraph in `Documentation~/production-host.md` (section "Clock, budget and
completion") and three sentences in the README "Standard nodes" section. They say that native hosts
do not run observer re-evaluation today and that a reactive composite is the native way to guard.

### Other changes

- `MCP/Documentation/McpNodeCatalogDocumentGenerator.cs` builds the package registry with
  `StandardNativeShard`. `Documentation~/generated/node-catalog.md` was regenerated with the
  generator, not edited by hand: 91 node types, every `aibt.native.*` node with its contract.
  `MCP/AIBT.Mcp.asmdef` references `AIBT.StandardNodes`.
- `Editor/Graph/ProductionTreeAssetBaker.cs`: same diagnostic code and conditions. For a type ID of
  the package shard the message adds "It is a package node of shard aibt.native.standard: add
  typeof(AIBT.StandardNodes.StandardNativeShard) to your catalog set ([AibtCatalogSet])."
- `README.md`: native node count and table rows (Native/Blackboard 20, Native/Flow 4), plus an
  opt-in paragraph.
- `Tests/Integration/AIBT.Integration.Tests.asmdef` references `AIBT.Agents` (the parity test).

### Tests

| Class | Cases | What it checks |
|---|---|---|
| `Tests/Integration/NativeRuntime/StandardNativeShardTests` (existing, one expectation changed) | 3 | The generator takes the grown shard: it is `IsUsable` in a catalog set next to a project shard and runs natively; both shards' nodes are declared |
| `Tests/Integration/NativeRuntime/NativeLibraryNodeTests` | 55 | One behaviour test or more per node on `ProductionTreeHost`, including the ADR edge cases: `set-int` above 2³¹−1, `increment-int` wrap, `flat` distance, zero vector, non-finite results, failed reads. Plus one tree with all 20 nodes |
| `Tests/Integration/Simulation/NativeLibrarySimulationTests` | 1 | Every library tree of `NativeLibraryCases` as a `TreeSimulation` group with mixed think schedules, in place and with Jobs. The per-tick digests are identical, and the root results equal the host's |
| `Tests/Integration/NativeRuntime/NativeLibraryParityTests` | 2 | Managed `aibt.unity.*` on `BehaviorTreeAgent` vs `aibt.native.*` on `ProductionTreeHost` over a scripted fact sequence: the same root results and key values (floats within a stated relative tolerance with an absolute floor; `determinism-v1.md:12`), plus `idle` under a reactive selector |
| `Tests/Integration/NativeRuntime/NativeLibraryReactiveGuardTests` | 9 | The native guard over a memory composite (section above) |
| `Tests/Integration/NativeRuntime/GeneratedDispatchFieldOrdinalRegressionTests` | 7 | The host fix (section above) |
| `Tests/Editor/Consumer/NativeLibraryBakeMessageTests` | 1 | The bake message names the shard to add; another shard's node keeps the old message |
| `Tests/Editor/Documentation/McpDocumentationGeneratorsTests` | +1 | `node-catalog.md` lists every `aibt.native.*` node with its contract, and the managed nodes are still listed |

`Tests/Integration/NativeRuntime/NativeLibraryTestKit.cs` and `NativeLibraryCases.cs` are shared
helpers (catalog set `NativeLibraryCatalog`: the shard and the four test leaves).

### Changed expectations of existing tests (old → new)

Only the node-list tests changed:

| Test | Old | New |
|---|---|---|
| `StandardNativeShardTests.TheGeneratedCatalogDeclaresBothShardsNodes` | the 4 P9-002 IDs | `StandardNativeLibraryIds.All` (24) |
| `StandardNodeCatalogTests.TheProjectCatalogOffersTheStandardNativeShardAsNativeOnlyNodes` | the 4 IDs | `StandardNodeCatalogTests.StandardNativeTypeIds` (24) |
| `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` | built-ins + 4 native IDs | built-ins + the 24 |

`McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` fails in the host
project "Modules" before and after. It finds 96 nodes instead of 91 because the 5 Swarm Arena sample
nodes are in the project, a known failure unrelated to P11-002. The other 7 cases of the class
pass.

### The four existing nodes are unchanged

`existing-nodes-unchanged.txt`: the manifest and descriptor JSON SHA-256 of `bool-is-true`,
`float-at-least`, `set-float` and `wait-microseconds` are identical before (`ccde239`) and after
(node count 4 → 24, "ALL FOUR UNCHANGED: True"). The dumps are in `before/`, `after-nodes/` (after
`442d9aa`) and `after-final/` (after the fix and tests; identical to `after-nodes/`).

### Fingerprints before and after

| | Before (`ccde239`) | After |
|---|---|---|
| shard `aibt.native.standard` version | 1 | 1 |
| shard NodeRegistryHash | `b91422c3980bf4f9…` | `f582da57173a960c…` |
| shard DescriptorHash | `9b984c00561de9e0…` | `c998e1fc2370ba28…` |
| `StandardNativeProbeCatalog` | `ccaa001780ece657…` | `a89c927f3c3e279d…` |
| `JobsEquivalenceCatalog` | `5546760e5cf50d64…` | `b6bf793584d32bd8…` |
| `SimulationTestCatalog` | `11e741e4fafa49b2…` | `c5aa1cd1915ab1d2…` |
| `SimulationIoCatalog` | `dfaa0aaedcab4a23…` | `82ba9dc6c0f56080…` |
| `SimulationActionCatalog` | `fa865ce86e1ae682…` | `e92c76d3b7248d32…` |
| `NativeLibraryCatalog` (new) | — | `52b44879e8593ecf…` |

The full values are in `before/catalog-sets.txt` and `after-final/catalog-sets.txt`.

### What has to be prepared again

- In the package, only test catalog sets list the shard. Their trees are compiled in the tests, and
  no baked fixture asset depends on them.
- `Benchmarks~` Phase 10 PerfCatalog lists the shard. Its runtime assets need preparing again in
  the benchmark's own project.
- Nothing in the parent project "Modules" was prepared or changed. Swarm Arena does not list the
  shard.

### Palette and demo tree

- `palette-conditions-native-blackboard.png`, `palette-actions-native-blackboard.png`,
  `palette-actions-native-flow.png`: the AIBT Graph node palette ("Create Node") opened in each Native
  group. The popup was opened and navigated through the window's own code by an Editor script
  (`scripts/OpenPalette.cs`, `scripts/NavigatePalette.cs`) and captured with `capture_window`
  (screen method). The search field still shows the text `native` from the first capture
  (`palette-search-native.png`). The listed group is not filtered by it.
- `palette-tree.txt`: the palette tree as text from `NodeSearchProvider.BuildTree` with the
  window's registry: 8 conditions in Native/Blackboard, 12 actions in Native/Blackboard and 4 in
  Native/Flow, each with its type ID. Titles that a managed node shares get the type ID in brackets.
- `demo-tree-graph.png`: `Tests/Fixtures/P11-002/native-library-demo.aibt.json`, built only from
  `aibt.core.*` and `aibt.native.*` nodes (11 of the library nodes), opened in the AIBT Graph
  (`Save-UnityWindow.ps1 -OpenAsset`). The graph shows it Valid. A reactive selector chooses between
  a guarded chase (`float3-is-set` → memory sequence of `float3-distance`, `float3-within-distance`,
  `set-bool`, `idle`) and a patrol (`increment-int`, inverter over `int-keys-at-least`, `copy-bool`,
  `succeed`). The windows were closed afterwards. No layout or scratch file was left.

### Runs

All in `runs/`, through `Tools/Unity/Invoke-UnityTests.ps1` unless stated.

| Run | Result |
|---|---|
| `01-library-integration-first` | NativeLibraryNodeTests 26/59 (test kit ambiguity and the host defect) |
| `02-library-node-tests-after-kit-fix` | 28/59; all 31 failures are nodes with a handle at local field 1 or higher |
| `03-regression-and-jobs-before-fix` | regression 1/5, JobsPolicyEquivalence 28/28, parity 0/2 |
| `04-regression-RED-old-line` | regression 1/7 on the old line |
| `05-after-fix-library-and-regression` | regression 7/7, node 56/59, simulation 1/1, parity 1/2 (wrong expectations in my tests) |
| `06`, `07` | node 57/59, parity 1/2 → 2/2 |
| `08-touched-classes-after-fix` | 19 classes, all green except the known McpToolDispatcher case (7/8): StandardNativeShard 3/3, StandardNodeCatalog 6/6, McpDocumentationGenerators 14/14, NativeLibraryBakeMessage 1/1, JobsPolicyEquivalence 28/28, TreeSimulation 13/13, HostEquivalence 4/4, Io 20/20, Action 46/46, Debug 15/15, GeneratedDispatchLifecycleProof 14/14, ExternalTreeBlackboardWrite 9/9, ProductionTreeWorldEffect 6/6, NativeTreeAgent 16/16, SubtreeNative 7/7, GeneratedCatalogDispatchCanary 2/2, regression 7/7, NativeLibrarySimulation 1/1 |
| `09-guard-node-parity` | ReactiveGuard 9/9, node 55/55 (observer cases removed), parity 2/2 |
| `10-codegen-contract-tests` | `AIBT.CodeGen.ContractTests` 6/6 (ABI v2 unchanged) |
| `11-verify-static` | `Tools~/Verification/Verify-Static.ps1`: 7 schemas, 182 work items, passed |
| `12-final-touched-classes` | after the docs, CHANGELOG and demo fixture: StandardNativeShard 3/3, StandardNodeCatalog 6/6, McpToolDispatcher 7/8 (the known case), McpDocumentationGenerators 14/14, NativeLibraryBakeMessage 1/1, NativeLibraryNode 55/55, NativeLibraryParity 2/2, NativeLibraryReactiveGuard 9/9, NativeLibrarySimulation 1/1, regression 7/7, SubtreeExpander 26/26 (scans every package tree, the demo fixture included), UserGuideSnippet 3/3 |
| `13-verify-static-final` | `Verify-Static.ps1` again after the evidence and task card edits: 7 schemas, 182 work items, passed |

The public API diff from `ccde239` is additive: no public member was removed (`git diff` outside
`Tests/`).

### Open questions for the owner

1. **Observer re-evaluation on native hosts** (section above). This is existing behaviour, not
   caused by P11-002, and it differs from `execution-semantics-v1.md:225` and
   `update-phases-v1.md:8`. A possible follow-up work item (not created): implement the queue on
   native hosts, or change the specification and the observer contract of native conditions.
   Until then the docs say to guard with a reactive composite.
2. **AIBT2043 on native writers.** The graph shows "Input key '…': no node in this tree writes it"
   (Info) for keys that native nodes write through generated `bindings` (in the demo tree: `alert`,
   `caught`, `distance`, `steps`). `TreeValidator.ValidateBlackboardWriteCoverage` counts only
   manifest accesses and `BlackboardKey` parameters as writes, so this applies to the four P9-002
   nodes too (`set-float`). It is only a note and blocks nothing. Not changed.

### Not covered by stage 1

- Stages 2–4 (timing, signal and random nodes, host seeding, user guide page, demo scene and clip).
- A Player build and IL2CPP with the grown shard.
- Performance of the new nodes.
- A full nine-assembly test run (touched classes only, as briefed).

### Observations (not changed)

- `BehaviorTreeAgent.Restart(true)` on a tree still running `idle` faults with AIBT4001, even after
  `Stop()`. Seen in the parity test, which therefore creates a fresh agent per step. The root cause
  is not confirmed.
- The managed agent faults with AIBT4002 on any tree with an observer block (fact (b) above).

### Acceptance run (orchestrator, 2026-11-19, stage 1)

Read by the orchestrator on `4af5571` (clean working tree): the whole diff `ccde239..4af5571`, the
three node sources, the changed existing files and the new tests.

- **Protected paths.** Under `Runtime/` only the allowed line of
  `GeneratedTreeDispatchAdapterV2.cs` changed. Nothing changed in `CodeGen~`,
  `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, `Documentation~/specifications/`,
  `package.json`, `Schemas~`, `Samples~`, `Authoring/Validation`, `Authoring/Compilation` or
  `Agents`.
- **Existing tests.** The only changed expectations are the three node lists in the table above
  (4 IDs → 24).
- **Full run of all nine assemblies**, 22:37–22:52; Editor-log counters did not move (`No command
  named` 0, main-thread timeouts 1). Raw output: `C:\UnityProjects\_aibt_acceptance\P11-002\stage1-acceptance\full-run-4af5571.txt`.

| Assembly | `34e80f0` | `4af5571` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | — |
| `AIBT.Integration.Tests` | 240/240 | 314/314 | `NativeLibraryNodeTests` 55, `NativeLibraryReactiveGuardTests` 9, `GeneratedDispatchFieldOrdinalRegressionTests` 7, `NativeLibraryParityTests` 2, `NativeLibrarySimulationTests` 1 |
| `AIBT.Editor.Tests` | 789/790 | 791/792 | `NativeLibraryBakeMessageTests` 1, one case in `McpDocumentationGeneratorsTests` |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | — |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | — |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | — |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | — |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | — |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | — |

The one failure is the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (the project
"Modules" registers five sample nodes: 96 found, 91 expected).

Not done by the orchestrator: the regression test was not run again on the old line (the red state
is the session's `runs/04`, 1/7); the field-ordinal audit and the managed-observer probe were read,
not repeated; no Player or IL2CPP build; no performance measurement.

Open for the owner (neither blocks stage 2): the two questions above (observer re-evaluation on
native hosts against `execution-semantics-v1.md:225`; AIBT2043 not counting writes through native
bindings). The stage 1 line of ADR §7 "observer conditions guard a memory composite natively" is
met through a reactive composite at the next update, not through observer re-evaluation.

## Stage 2 — timing and signal nodes (2026-11-19)

Base: `feature/aibt-consumer-workflow` at `82eb8ea`. Editor 6000.6.9f1, project "Modules"
(Pipeline port 7810). Tests ran with the async `Tools/Unity/Invoke-UnityTests.ps1` after
`Invoke-UnityRecompile.ps1`: touched classes and classes whose catalog sets list the shard, one
runner at a time. Raw outputs, dumps, screenshots, the clip and the scripts that made them are in
`C:\UnityProjects\_aibt_acceptance\P11-002\stage2\` (outside the repository). Nothing changed under
`Runtime/`, `CodeGen~`, `Schemas~`, `Authoring/Validation`, `Authoring/Compilation`, `Agents`,
`Documentation~/specifications/` or in `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`
(`public-api-diff.txt`).

Commits (submodule only):

| Commit | Content |
|---|---|
| `3d6f0a7` | the 4 nodes |
| `c055691` | tests, test kit additions, the changed node-list expectations |
| `00e6045` | `node-catalog.md` regenerated with the generator (91 → 95 node types) |
| the commits after `00e6045` | guide page and its snippet test, Native Guard sample, README, CHANGELOG, this file and the task card |

### Fact checks before the node code

All three passed, so the nodes follow the ADR:

1. **Int64 Tree keys end to end.** An Int64 key with `BlackboardWriteHandle<long>` /
   `BlackboardReadHandle<long>` compiles, bakes and runs on `ProductionTreeHost`, takes a
   `NativeTreeAgent.SetFact<long>` fact and runs in `TreeSimulation`
   (`NativeLibraryAgentTests.AnInt64DeadlineFact_ReachesDeadlinePassed` and the deadline cases
   beyond the Int32 range).
2. **The shard's effect channel arrives on all three paths:** `NativeTreeAgent.OnEffect`
   (`NativeLibraryAgentTests`), `ProductionTreeHost.TryDrainEffects` with the stable id
   FNV-1a-64(`aibt.native.signal`) = `0x382b261d75afa998` (`NativeLibraryTimingTests`) and
   `CommandChannel<int>` in `TreeSimulation` (`NativeLibraryTimingSimulationTests`).
3. **Declaring the channel changes nothing for trees without `signal`.** The simulation builds its
   channel map per program from the nodes the program uses. A group whose tree has no `signal`
   answers `UnknownChannel` for `aibt.native.signal`, as before. Every existing test whose catalog
   set lists the shard passed unchanged (run `02`). The only changed expectations are the node lists
   below.

### The 4 nodes

Shard `aibt.native.standard` (assembly `AIBT.StandardNodes`), shard version 1. Each node has node
version 1, Tree scope only, `deterministic: true`, cost `Trivial` and an `[AibtNodeDocumentation]`
whose "when not to use" names the `aibt.unity.*` counterpart. Source:
`Authoring/Registry/Generated/StandardNative/StandardNativeTimingNodes.cs`.

| Type ID | Kind | Category | Parameters | Keys / channel | Result |
|---|---|---|---|---|---|
| `aibt.native.wait-until-true` | Action (AbortOnly) | Native/Timing | `timeout-microseconds` UInt32 (0 = none) | `key` Bool read | Success when the key is true; Failure when the timeout has passed since Enter; otherwise Running |
| `aibt.native.set-deadline` | Action | Native/Timing | `duration-microseconds` UInt32 | `key` Int64 write | writes now + duration, Success; past `long.MaxValue` Failure without writing |
| `aibt.native.deadline-passed` | Condition (not an observer) | Native/Timing | — | `key` Int64 read | Success when now >= key |
| `aibt.native.signal` | Action | Native/Signals | `code` UInt32 | channel `aibt.native.signal`, Int32 | emits `code`, Success; above 2147483647 Failure and nothing emitted |

### Contract choices (made in stage 2, not fixed by the ADR)

- `wait-until-true`: the key wins over a timeout reached in the same update. The timeout counts from
  Enter (memory `started-at`), so each new start after an abort or a root restart counts again.
  Timeout 0 means no timeout. A key that cannot be read fails the node, as in stage 1. The check is
  `now >= start && now - start >= timeout` (`StandardNativeTime.HasReached`, no overflow).
- `set-deadline` writes nothing and fails when now + duration would pass `long.MaxValue`. Exactly
  `long.MaxValue` is written.
- `deadline-passed` reads time, so it is not an observer condition. A key never set (0) has passed.
- `signal`: the configuration field is `code` and the command field `signal`; the binding id is the
  channel name `aibt.native.signal`. It is an action (AbortOnly like the other library actions) and
  emits once per tick of the node.
- **Host behaviour, not a node contract:** `ProductionTreeHost` refuses a negative or
  non-monotonic clock (`ProductionTreeAgentCore.TryBeginUpdateAt`, `NativeLifetimeStateInvalid`),
  so a negative "now" never reaches a node. That case was removed from the tests (run `01`).
- `NativeTreeAgent` reads `Time.timeAsDouble` and takes no clock from outside. The controlled-clock
  cases (before / at / after, exactly at the timeout) therefore run on `ProductionTreeHost` with a
  clock passed to `ProductionTreeProgram.TryBootstrap`. The agent tests check facts, signals and one
  time point per update.

### Found: v1 reactive composites restart waits

`aibt.core.reactive-sequence` / `reactive-selector` (v1) abort and re-enter their running child at
every update (`reference-executor-machine-v1.md`, "Reactive replacement"). A `wait-until-true` (or
`wait-microseconds`) directly under one starts again at every update and never times out. The case
"under a v1 reactive selector it restarts every update and never times out" pins this. The guide
tells authors to use `reactive-*-retaining` above waits, and the Native Guard tree does. Existing
behaviour, not changed.

### Tests

| Class | Cases | What it checks |
|---|---|---|
| `Tests/Integration/NativeRuntime/NativeLibraryTimingTests` | 30 | 29 step-by-step cases on `ProductionTreeHost` with a controlled clock and effect capacity 8: root status, key values and drained signals after each step, and a second drain is empty. `wait-until-true`: true at start; no timeout + fact write; timeout 1000 (Running at 999 after start, Failure exactly at 1000); the key wins at the timeout; the maximum timeout; the key set by a node write (parallel with a writer); aborted by a retaining reactive selector and restarted (the timeout counts from the new start); under a v1 reactive selector. `set-deadline`: now + duration; 0; beyond Int32; exactly `long.MaxValue`; overflow fails and the key keeps 7. `deadline-passed`: before / exactly at / after; never set; Int64 before / at; written as a fact; under an inverter. `signal`: 7, 0 and 2147483647 once each with the stable id; 2147483648 and 4294967295 fail and emit nothing; two signals in order; a signal after a fact. A cooldown shared by two branches through one key. Plus `EveryStage2NodeHasBehaviourCases`: each of the 28 library nodes has stage 1 or stage 2 cases |
| `Tests/Integration/NativeRuntime/NativeLibraryAgentTests` | 4 | `NativeTreeAgent` with the scheduler: signals reach `OnEffect` exactly once per emission after a fact write; a code above Int32 fails and nothing arrives; an Int64 fact reaches `deadline-passed`; `set-deadline` and `deadline-passed` in one update see the same time point |
| `Tests/Integration/Simulation/NativeLibraryTimingSimulationTests` | 1 | Seven timing and signal trees as `TreeSimulation` groups of three agents thinking every 1, 2 and 3 ticks, in place and with Jobs: identical per-tick digests including the commands; signal ticks and wait completions match the tick time (`tick * 33333` µs); signal bounds (0, max, too large); a tree without `signal` answers `UnknownChannel` |
| `Tests/Editor/Documentation/UserGuideSnippetTests` | +1 | `NativeLibraryGuideSnippetsAreQuotedFromTheNativeGuardSample`: the guide's 3 C# blocks are quoted from the sample, and the JSON nodes it shares with the sample tree (at least 4) have the sample's types |

`NativeLibraryTestKit.cs` gained `StandardNativeLibraryIds.Stage2`, Int64 keys, an optional clock
and effect capacity, `LibraryHost.Fact(string, long)`, `ReadLong` and `DrainEffects`.
`NativeLibraryTimingCases.cs` holds the cases.

### Changed expectations of existing tests (old → new)

Only the node lists and one category check:

| Test | Old | New |
|---|---|---|
| `StandardNativeShardTests.TheGeneratedCatalogDeclaresBothShardsNodes` | `StandardNativeLibraryIds.All` (24) | `StandardNativeLibraryIds.All` (28) |
| `StandardNodeCatalogTests.StandardNativeTypeIds` (used by `TheProjectCatalogOffersTheStandardNativeShardAsNativeOnlyNodes` and `McpToolDispatcherTests`) | 24 IDs | 28 IDs |
| `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` | built-ins + 24 | built-ins + 28 (only a comment changed; the list comes from `StandardNativeTypeIds`) |
| `NativeLibraryNodeTests.TheGeneratorAcceptsTheShardNextToAProjectShard_AndOneTreeDispatchesEveryLibraryNode` | category Native/Blackboard or Native/Flow; 24 IDs | also Native/Timing and Native/Signals; 28 IDs |

The known failure `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` now finds 100 nodes
instead of 95 (the five Swarm Arena sample nodes in the project), as before.

### The 24 existing nodes are unchanged

`all24-unchanged.txt` and `all24-unchanged-final.txt`: the manifest and descriptor JSON SHA-256 of
each of the 24 stage 1 nodes are identical before (`82eb8ea`) and after (node count 24 → 28,
"ALL 24 EXISTING NODES UNCHANGED: True"). `existing-nodes-unchanged.txt` repeats the check for the
four P9-002 nodes. Dumps: `before/`, `after-nodes/` (after `3d6f0a7`) and `after-final/` (after the
sample and docs; byte-identical to `after-nodes/`).

### Fingerprints before and after

| | Before (`82eb8ea`) | After |
|---|---|---|
| shard `aibt.native.standard` version | 1 | 1 |
| shard NodeRegistryHash | `f582da57173a960c…` | `3887345debad49ac…` |
| shard DescriptorHash | `c998e1fc2370ba28…` | `d9242278d7667e39…` |
| `StandardNativeProbeCatalog` | `a89c927f3c3e279d…` | `db139644a75736e5…` |
| `JobsEquivalenceCatalog` | `b6bf793584d32bd8…` | `19ddbb52f52a810a…` |
| `SimulationTestCatalog` | `c5aa1cd1915ab1d2…` | `5649dde12fb4fac8…` |
| `SimulationIoCatalog` | `82ba9dc6c0f56080…` | `de1a2fb16fdfe9f9…` |
| `SimulationActionCatalog` | `e92c76d3b7248d32…` | `d639b822606503ac…` |
| `NativeLibraryCatalog` | `52b44879e8593ecf…` | `8ff74edb8c740a14…` |

All sets are `IsUsable` after. Full values: `before/catalog-sets.txt`, `after-final/catalog-sets.txt`.

### What has to be prepared again

- Every runtime asset prepared against a catalog set that lists `StandardNativeShard`. In the
  package these are only test sets (compiled in the tests; no baked fixture depends on them) and the
  `Benchmarks~` Phase 10 PerfCatalog (in the benchmark's own project). The CHANGELOG says so.
- Swarm Arena and First Agent do not list the shard and are not affected.
- In the parent project only the scratch demo asset below, which was prepared after the nodes.

### Guide, README, CHANGELOG, sample

- `Documentation~/native-node-library.md` (new): opting in (quoted from the sample's catalog set),
  what a library node may do, a managed → native table of all 28 nodes, and recipes: "is false" and
  "below" with `aibt.core.inverter`, a shared cooldown, signals, waiting for the game, and guarding a
  branch with v1 or retaining reactive composites. It says plainly that native hosts do not
  re-evaluate observers. Linked from the README.
- `README.md`: Native/Timing (3) and Native/Signals (1) rows and links to the guide. No new C#
  block (`UserGuideSnippetTests` still counts 5).
- `CHANGELOG.md`: the 4 nodes, the fingerprint change and preparing again, the guide and the sample.
- **Native Guard** sample, `Samples~/NativeGuard`: the tree `native-guard.aibt.json` (only
  `aibt.core.*` and `aibt.native.*`), `Catalog/NativeGuardCatalog.cs` (the whole opt-in),
  `Runtime/NativeGuard.cs` (facts with `SetFact`, signals with `OnEffect`, the visuals),
  `Editor/NativeGuardSetup.cs` (menu **AIBT > Samples > Native Guard > Create Demo**: prepares the
  runtime asset and builds the scene) and a README. Listed in `Samples~/README.md` and in the
  `samples` array of `package.json` (a new entry; the package version is unchanged, open question 1).

The tree: a retaining reactive selector `guard` over `zap` (reactive sequence: `bool-is-true`
`intruder-close`, `deadline-passed` `weapon-ready-at`, then a memory sequence of `set-deadline`
1.5 s, `signal` 1, `idle`), `flare` (the same on `intruder-seen` with `signal` 2), `watch`
(`intruder-seen`, `idle`) and `patrol` (memory sequence: `signal` 3, `wait-until-true` `gate-open`
with a 3 s timeout, `signal` 4, `wait-microseconds` 2 s). Zap and flare share the cooldown key.

### Screenshots and clip

- `palette-actions-native-timing.png` (Set Deadline, Wait Until True),
  `palette-conditions-native-timing.png` (Deadline Passed) and `palette-actions-native-signals.png`
  (Signal): the AIBT Graph "Create Node" palette opened and navigated by `scripts/OpenPalette.cs` and
  `NavigatePalette.cs`, captured with `capture_window`. `palette-tree.txt` lists them with their type
  IDs.
- `graph-edit.png`: the sample tree in the AIBT Graph, Valid, with 4 AIBT2043 notes (open question 4).
- `graph-live-1.png`, `graph-live-2.png`: Play Mode with the tower selected, "Live: Guard Tower
  (native) — running". Run 1 highlights guard → patrol → "Rest 2 s"; run 2, two seconds later,
  guard → watch → "Watch". The blackboard panel shows "live: —" for the keys of the native agent.
- `guard-edit.png`: the scene before Play.
- `clip/native-guard.mp4` (30 s of the Game view with the on-screen log; `Save-UnityClip.ps1 -Scene
  Assets/AibtNativeGuardDemo/NativeGuard.unity -Play -Seconds 30 -StillsEvery 3 -Source window
  -Window GameView -Method screen`) and `clip/stills/`, one every 3 s. The stills show:
  - 0 s: the first patrol is done (log "4 sweep + close, 3 open gate"), the tower cyan from the
    sweep, the intruder far left, all facts false.
  - 3 s: the intruder at the tower, seen and close true; the log shows two flares.
  - 6 s: the intruder was zapped and sent away to the left, the facts are false, and the patrol ran
    again ("4 sweep + close, 3 open gate" after "1 zap").
  - 12 s: the intruder seen again (seen true, close false) and the gate standing open (gate-open
    true): the flare branch took over from the patrol while it waited.
  - 21 s: the intruder away again; a new "3 open gate" heads the log.
  - 27 s: the tower red right after a zap, the gate open.
- The first recording showed the intruder stuck at the tower (a zap pushed it back, but it kept
  walking in) and the raised gate hiding the tower. The sample was changed (a zap also turns the
  intruder around; the gate stands to the side) and recorded again. Only the second clip is kept.
- The Console shows no message from the demo.

### Runs

All in `runs/`, through `Tools/Unity/Invoke-UnityTests.ps1` unless stated.

| Run | Result |
|---|---|
| `01-stage2-classes-first` | NativeLibraryTimingTests 30/31 (the negative-clock case: the host refuses the clock, see above; the case was removed), NativeLibraryAgentTests 4/4, NativeLibraryTimingSimulationTests 1/1 |
| `02-stage2-and-shard-users` | NativeLibraryTimingTests 30/30, NativeLibraryAgentTests 4/4, NativeLibraryTimingSimulationTests 1/1, StandardNativeShard 3/3, StandardNodeCatalog 6/6, McpToolDispatcher 7/8 (the known case), McpDocumentationGenerators 14/14, NativeLibraryNode 55/55, NativeLibraryParity 2/2, NativeLibraryReactiveGuard 9/9, NativeLibrarySimulation 1/1, GeneratedDispatchFieldOrdinalRegression 7/7, JobsPolicyEquivalence 28/28, TreeSimulation* 102/102, NativeLibraryBakeMessage 1/1 |
| `03-docs-sample-contract` | after the guide, sample and README: UserGuideSnippet 4/4, SubtreeExpander 26/26, ProjectTreeIndex 4/4, McpDocumentationGenerators 14/14, NativeLibraryTimingTests 30/30, NativeLibraryAgentTests 4/4; `AIBT.CodeGen.ContractTests` 6/6 (ABI v2 unchanged) |
| `04-verify-static` | `Tools~/Verification/Verify-Static.ps1` after the evidence and task card edits: 7 schemas, 182 work items, passed |

The public API diff from `82eb8ea` is additive (`public-api-diff.txt`): no line was removed from any
`.cs` file outside `Tests/`. The new public members are the node structs of the 4 nodes.

### Parent-project files (scratch, not committed)

- `Assets/Samples/AIBT/Native Guard/` and `Assets/Samples/AIBT/Native Guard.meta`: a copy of
  `Samples~/NativeGuard` where a Package Manager import would put it, with the `.meta` files Unity
  generated. (`Assets/Samples/AIBT.meta`, `First Agent` and `Simple Agent` next to it were already
  there, untracked.)
- `Assets/AibtNativeGuardDemo/` and its `.meta`: `NativeGuard.runtime.asset`, `NativeGuard.unity`,
  `Gate.mat`, `Ground.mat`, `Intruder.mat` and their `.meta` files, made by Create Demo.

They can be deleted. Nothing in the package refers to them.

### Open questions for the owner

1. **`package.json` `samples` entry.** The brief forbids changing the package version. The sample
   needs a `samples` entry to be importable from the Package Manager, so the entry was added and the
   version was not changed. If `package.json` must stay untouched, the entry can be dropped (the
   sample then stays in `Samples~` without being importable).
2. **v1 reactive composites and waits.** The README advice "put the condition first under a
   reactive-sequence" is right for conditions, but a wait directly under a v1 reactive composite
   never times out (section above). The guide explains it; the README was not changed.
3. **Observers on native hosts** (stage 1, question 1) are still not re-evaluated. The guide says so.
4. **AIBT2043** (stage 1, question 2) now also shows for `weapon-ready-at` in the sample, which
   `set-deadline` writes through native bindings.

### Not covered by stage 2

- Stages 3–4 (random nodes and host seeding, quickstart, MCP recipes).
- A controlled clock on `NativeTreeAgent` (it has none); those cases run on `ProductionTreeHost`.
- Observer re-evaluation, the AIBT2043 hint, performance, a Player or IL2CPP build.
- A full nine-assembly run (touched classes and classes whose sets list the shard only).
- The Package Manager import itself: the sample was copied into `Assets/Samples/AIBT/Native Guard/`
  by hand.

### Acceptance run (orchestrator, 2026-11-19, stage 2)

Read by the orchestrator on `bad2892` (clean working tree): the diff `82eb8ea..bad2892`, the node
source, the sample, the guide page, the changed existing files, the test classes, the screenshots
and the clip stills.

- **Protected paths.** Nothing changed under `Runtime/`, `CodeGen~`, `Schemas~`,
  `Authoring/Validation`, `Authoring/Compilation`, `Agents`, `Documentation~/specifications/` or in
  `ExpectedPublicAbiV2.txt`. `package.json` gained one `samples` entry (Native Guard), as the other
  samples have; the version is unchanged.
- **Existing tests.** The only changed expectations are the node lists and the category check in
  the table above (24 IDs → 28).
- **The sample copy** in the parent project (`Assets/Samples/AIBT/Native Guard/`) is identical to
  `Samples~/NativeGuard` (`diff -r`, `.meta` files aside).
- **README** (after the run, `README.md` only): the guard advice now says to use the retaining
  reactive composites above waits and multi-step branches, as the guide does (open question 2).
  `UserGuideSnippetTests` 4/4 after the edit.
- **Full run of all nine assemblies**, 23:44–23:55; Editor-log counters did not move (`No command
  named` 0, main-thread timeouts 1). Raw output: `C:\UnityProjects\_aibt_acceptance\P11-002\stage2-acceptance\full-run-bad2892.txt`.

| Assembly | `4af5571` | `bad2892` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | — |
| `AIBT.Integration.Tests` | 314/314 | 349/349 | `NativeLibraryTimingTests` 30, `NativeLibraryAgentTests` 4, `NativeLibraryTimingSimulationTests` 1 |
| `AIBT.Editor.Tests` | 791/792 | 792/793 | one case in `UserGuideSnippetTests` |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | — |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | — |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | — |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | — |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | — |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | — |

The one failure is the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (100 found,
95 expected: the five sample nodes registered in the project "Modules").

Not done by the orchestrator: the sample was not imported through the Package Manager (the copy in
`Assets/Samples` was made by hand by the stage session); the clip was not recorded again (its stills
were read); no Player or IL2CPP build; no performance measurement.

Seen in the live screenshots and not part of this stage: the blackboard panel of the graph shows
`live: —` for the keys of a native agent, and the tree list marks Native Guard "Needs native
compile" while its prepared asset is running.

Open for the owner (none blocks stage 3): the questions above.
