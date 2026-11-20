# P11-002 evidence: native node library

Decision: `Documentation~/decisions/ADR-P11-002-native-node-library.md` (owner decision of 2026-11-19,
AIBT-050, every §8 recommendation). Stage 1 of ADR §7 is implemented (section below) and was
accepted by the orchestrator on `4af5571` ("Acceptance run" at the end of its section). Stage 2 was
approved by the owner on 2026-11-19, is implemented (section "Stage 2" after the stage 1 material)
and was accepted by the orchestrator on `bad2892`. Stage 3 (random nodes; hosts seed random streams)
was approved by the owner on 2026-11-20, is implemented (section "Stage 3") and was accepted by the
orchestrator on `e944c5c`. Stage 4 (documentation pass) was approved by the owner on 2026-11-20, is
implemented (sections "Stage 4" and "Stage 4 — rework") and was accepted by the orchestrator on
`0485779`. All four stages are accepted; the item is in `Review`.

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

## Stage 3 — random nodes and host seeding (2026-11-20)

Base: `feature/aibt-consumer-workflow` at `d1218b1`. Editor 6000.6.9f1, project "Modules"
(Pipeline port 7810). Raw outputs, dumps, screenshots, clips and the scripts that made them are in
`C:\UnityProjects\_aibt_acceptance\P11-002\stage3\` (outside the repository).

### Plan for `Runtime/`, written before the change

Read first: `GeneratedTreeDispatchAdapterV2.cs` (slot creation `:906-907` makes one zeroed state word
and one zeroed increment word for a case with `HasRandomStream`, none otherwise; `SeedRandomStreams`
`:594-599` seeds only slots that have a word, via `DispatchSlot.SeedRandomStream` `:1318-1325` and
`NativeRandomStreamDerivationV1.TryDerive`), `NativeBurstDispatchBatchOwnerV2.cs:587-590` (a request
of a random-capable case with an even increment is refused: the `InvalidEncoding` the pin test
records), `ProductionTreeProgram.TryBootstrap`, `ProductionTreeHost.TryBootstrap` (generated
overload `:226-245`; the tree instance id is the process-wide counter `s_nextTreeInstanceId`, `:43`,
`:240`), `ProductionTreeAgentCore.TryBootstrapGenerated` (shared with the simulation container),
`NativeTreeAgent.StartHost` (`:229-256`; every start and restart adds a new `ProductionTreeHost`),
`ProductionTreeScheduler` (does not bootstrap hosts) and `TreeGroup.TryResetToFreshInstance`
(`Runtime/Simulation/TreeGroup.cs:504-511`: next container-local instance id, then
`SeedRandomStreams(Settings.RandomSeed, semantic hash)`).

Lines to change (nothing else under `Runtime/`):

1. `Runtime/Integration/ProductionTreeHost.cs` — the generated `TryBootstrap` (`:226-245`) gets an
   overload with a `ulong randomSeed` before `out failure`; the existing overload calls it with
   `DefaultRandomSeed` (0). After `_core.TryBootstrapGenerated` succeeds and before the first update,
   the new overload calls `_core.GeneratedDispatch.SeedRandomStreams(randomSeed, semantic hash of
   definition.Binding.SemanticProgram)` — the call `TreeGroup` makes — and records the seed in a new
   read-only `RandomSeed` property. New constant `DefaultRandomSeed = 0`. Why here and not in
   `ProductionTreeAgentCore`: the core is shared with `TreeSimulation`, which seeds itself; seeding
   in the host leaves the container path byte for byte as it is. The legacy delegate overloads have
   no generated adapter and no streams and do not change.
2. `Runtime/Integration/ProductionTreeProgram.cs` — an overload
   `TryBootstrap(host, ulong randomSeed, out failure, effectCapacity, clock, traceCapacity)`; the
   existing one calls it with `ProductionTreeHost.DefaultRandomSeed`.
3. `Runtime/Integration/NativeTreeAgent.cs` — a serialized `ulong _randomSeed` (default 0) with a
   public `RandomSeed` property; `StartHost` passes it to the new `ProductionTreeProgram.TryBootstrap`
   overload.

Why trees without random nodes cannot change: `SeedRandomStream` returns at once for a slot without
a stream word (`_randomStates.Length == 0`), and a tree without a random-capable node has no such
slot, so the seeding loop writes nothing. The before/after dumps below test it.

Choices for the owner to overrule:

- **Default seed 0**, as `TreeSimulationSettings.Default` (`RandomSeed = 0`).
- **Where the setting lives:** a bootstrap argument for `ProductionTreeHost` /
  `ProductionTreeProgram` (fixed for the host's life, like the clock), and a serialized field plus
  property on `NativeTreeAgent`, read at each start and restart.
- **Restart:** a host has no restart (its root result is terminal). `NativeTreeAgent` restarts by
  replacing its host, so every run is a new tree instance with a new instance id and freshly derived
  streams — the next run draws a different sequence. This is what the container does under
  `RootCompletionPolicy.Restart` (a new container-local instance id, then `SeedRandomStreams`), and
  `time-and-random-v1.md` ("Stream lifecycle") allows it: a host that wants a distinct sequence after
  a restart supplies a distinct tree instance identity under its lifecycle policy. Re-seeding the
  same id to repeat the sequence would need a host restart that keeps the instance id, which hosts
  do not have; that would be a lifecycle change, not seeding.
- **Instance ids and reproducibility:** the host's tree instance id is the process-wide counter
  (1, 2, … per host bootstrap in the domain, any program). Two agents with the same tree and seed
  therefore differ. A run is reproducible when the same hosts bootstrap in the same order after a
  domain reload (the counter is a static field, reset by the reload that Play Mode does when Enter
  Play Mode Options do not disable it); otherwise the same seed gives other sequences. Measured
  below.

Found while reading, to be checked before relying on it: the scheduler's group dispatch
(`GeneratedDispatchGroupExecutorV2` and `GeneratedDispatchGroupWorkspaceV2`, used under
`BatchedJobsSameFrame` and `PipelinedJobs` when two or more hosts of one catalog dispatch in one
round) copies each participant's random words into a temporary batch and commits only memory and
binding values back (`GeneratedTreeDispatchAdapterV2.TryCommitFromGroup`). If so, a seeded stream
there would never advance.

### Identity dumps before the change

`Tests/Integration/NativeRuntime/P11002Stage3IdentityDump.cs` (temporary, not committed; copy in
`stage3\scripts\`) writes, for trees without random nodes:

- `hosts.txt`: the 70 stage 1 library cases, the 29 stage 2 timing cases, the deterministic
  simulation trees, the three `JobsPolicyEquivalenceTests` trees and the two `NativeTreeAgentTests`
  trees, each on its own `ProductionTreeHost` (catalog `NativeLibraryCatalog`, clock 33 333 µs per
  update, 30 updates, effect capacity 8): per update the update count, root result, failure, active
  path, every Tree-scope byte, the random words of every node, every generated slot's per-instance
  bytes except its transaction record (memory, random words, binding values, completions, commands,
  operations, published commands, state), every trace record without the instance id, and the
  drained effects;
- `host-lifecycle-proof.txt`: the same for the `GeneratedDispatchLifecycleProofTests` fixture tree on
  its own catalog;
- `agents-jobs-policy.txt`: the `JobsPolicyEquivalenceTests` population of `NativeTreeAgent`s
  (12 agents, facts between frames, restarts) under the default profile, forced Immediate, forced
  BatchedJobsSameFrame and forced JobsAgentFrame: every frame's trace records, results, Tree bytes
  and delivered effects;
- `simulation.txt`: every tree above plus the container's own random trees (patrol, errand, coin) as
  `TreeSimulation` groups of three agents, seed `0xC0FFEE`, in place and with Jobs: per tick and
  group the digest of `SimulationTestPrograms.AppendDigest` and every agent's stream words.

Taken twice at `d1218b1` (runs `05`, `06`): the two dumps are byte-identical, so the dump itself is
deterministic. Kept in `stage3\before\`.

Correction to the list above: `hosts.txt` holds the **52** stage 1 library cases (`## library`
headers in the dump), not 70; the dump itself is unchanged.

### Part A: what changed under `Runtime/` (commit `ba7c944`)

Only the plan's lines:

- `Runtime/Integration/ProductionTreeHost.cs:98-112` — `DefaultRandomSeed` (0) and the read-only
  `RandomSeed`; `:247-262` — the existing generated `TryBootstrap` calls a new overload with
  `ulong randomSeed`; `:274-276` — after `TryBootstrapGenerated` succeeds, before the first update:
  `_core.GeneratedDispatch.SeedRandomStreams(randomSeed, semantic hash)`, then `RandomSeed = randomSeed`.
- `Runtime/Integration/ProductionTreeProgram.cs:27-34` — the overload with `randomSeed`; `:40` passes it on.
- `Runtime/Integration/NativeTreeAgent.cs:43-44` — the serialized `_randomSeed` (default 0, with a
  tooltip); `:76-84` — `RandomSeed`; `:246` — `StartHost` passes it.
- `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:591-593` — the
  `SeedRandomStreams` comment only ("Hosts never call this" became who calls it).

`9808498` regenerated `Documentation~/api-reference-runtime.md` for the six new public members
(`McpDocumentationGeneratorsTests` failed on the stale file in run `14`).

### Identity proof: trees without random nodes

The dumps described above, taken again after each step, compared by SHA-256 with `before/`:

| Dump | After seeding (`ba7c944`) | After the nodes (`bb059c4`) | After the group fix (`04ba623`) |
|---|---|---|---|
| `hosts.txt` | identical | identical | identical |
| `host-lifecycle-proof.txt` | identical | identical | identical |
| `agents-jobs-policy.txt` (default, Immediate, forced BatchedJobsSameFrame, JobsAgentFrame) | identical | identical | identical |
| `simulation.txt` | identical | identical | identical |

Folders `after-seeding/`, `after-nodes/`, `after-group-fix/`; runs `09`, `13`, `19` (the compare
is `runs/19-dump-after-group-fix-compare.txt`). Nothing under `Runtime/` changed after `04ba623`.

### Group-commit fix (owner decision 8, 2026-11-20)

**Defect.** With the streams seeded, a random node in a group wave did not advance. Group waves are
the scheduler's grouped dispatch: a profile that forces `BatchedJobsSameFrame` or `PipelinedJobs`,
two or more adjacent hosts of one catalog (`ProductionTreeScheduler.DriveForcedJobsGroup`). Default
profiles never take that path. The executor and the workspace copied each participant's stream word
into the batch, but the commit (`TryCommitGroupParticipant` → `DispatchSlot.TryCommitFromGroup`)
wrote back only memory, binding values and tree values. Every update rolled the first value of the
stream again. Reported before any change (`P11-002-stage3-blocker-group-dispatch-random.md`,
`proposed-group-commit-random-fix.diff`); the owner answered «Да, чинить» with exactly that scope.

**Change** (`04ba623`, `applied-group-commit-random-fix.diff`, 3 files, +24/−3, all `internal`):

- `GeneratedTreeDispatchAdapterV2.cs:452-474` — `TryCommitGroupParticipant` takes
  `(hasRandomState, committedRandomState)` and passes them on; `:1127-1155` —
  `DispatchSlot.TryCommitFromGroup` refuses (`InvalidHandle`) when `hasRandomState` does not match
  whether the slot has a stream word, and writes `_randomStates[0] = committedRandomState` when it has one.
- `GeneratedDispatchGroupExecutorV2.cs:243-244`, `:276-281` — in the read loop, before any
  participant commits, `owner.TryReadCommittedRandomState(index)` for a participant with a stream;
  `:292-293` — passed to the commit.
- `GeneratedDispatchGroupWorkspaceV2.cs:242-243`, `:248-249` — passes
  `_randomStates[request.RandomStateIndex]` (the workspace's own word, which the batch advanced).

**Validation order.** The new guard cannot fail after another participant has committed:
`participant.RandomStates` is the slot's own `_randomStates` (`TryPrepareForGroup` hands it out), so
`hasRandomState` is computed from the same array the guard checks — an invariant after a successful
prepare, not a new failure. The workspace validates every participant (`TryValidateCommits` →
`TryValidateGroupParticipantCommit` → `TryValidateGroupCommit`) before the first commit; the
executor has no separate validation pass, but every read that can fail (memory, binding values,
status and now the random state) happens in its first loop, before any commit. So the validation
pass does not need the check, and none was added.

**Red before, green after** (the final shape of the tests):

| Run | Code | `NativeTreeAgentRandomSeedTests` |
|---|---|---|
| `16-group-fix-red-before` | the tests of `04ba623` on the code without the fix (the three files reverted, then re-applied from the diff) | 5/8: `UnderEveryPolicy_…("batched-jobs")` and `("pipelined-jobs")` 0 rolls; `OneTreeSeedAndInstanceIds_…` fails on batched-jobs (no roll) |
| `17-group-fix-green-after` | with the fix | 8/8 |

`16a`/`17a` are the first shape of the tests, which counted a fixed 120 frames: with the fix,
batched-jobs passed and pipelined-jobs reached 4 rolls. Diagnosed with a temporary per-frame log
(`diag/`): one pipelined update spans about 10–30 scheduler frames (the driver advances one stage
per frame), and each update still makes exactly one draw; immediate and batched made one update and
one draw per frame, frame for frame identical. That is the pipelined policy's pace, not a stream
fault, so both tests now count rolls (12 per agent, at most 4000 frames) instead of frames.

**Four-policy comparison.** `OneTreeSeedAndInstanceIds_RollTheSameSequenceUnderEveryPolicy`: four
agents of the roller tree, seed `0x5EED7`, the same four tree instance ids under each policy (the
host's private id counter is set by reflection before each run and restored after). For every
agent, the committed stream states in order are the same under Immediate, JobsAgentFrame, forced
BatchedJobsSameFrame and forced PipelinedJobs, and each new state is exactly one draw after the last
(no roll lost or repeated). `UnderEveryPolicy_EachAgentsStreamAdvancesOnItsOwnDerivedSequence`
checks under each policy that every agent stays on its own derived stream, only moves forward, at
most one roll per frame, and rolls at least 12 times.

**Group waves without random streams are unchanged.** Run `18`: every test class that drives forced
BatchedJobsSameFrame or PipelinedJobs, with no changed expectation — GeneratedDispatchLifecycleProof
14/14, JobsPolicyEquivalence 29/29, NativeHotReloadGoldenEquivalence 4/4, NativeTreeAgent 16/16,
ProductionTreeWorldEffect 6/6, SchedulingPolicyDriver 5/5, NativeExecutionAllocation 3/3,
NativeAutoAdaptiveRealisticFeedback 2/2, NativeAutoAdaptiveSelection 7/7, NativeAutoSelection 25/25,
ProductionTreeScheduler 25/25, SchedulingProfile 18/18, NativeExecutionLargePopulationStress 1/1,
NativeTreeAgentRandomSeed 8/8. Swarm Arena has no test class. The identity dumps (forced
BatchedJobsSameFrame included) are byte-identical after the fix (table above).

### The 5 nodes (`bb059c4`)

`Authoring/Registry/Generated/StandardNative/StandardNativeRandomNodes.cs`, category
`Native/Random`, node version 1, declared deterministic, each with `[AibtRandomStream]`:

| ID | Kind | Draws per tick | Invalid configuration (fails, no write, no draw) |
|---|---|---|---|
| `aibt.native.random-chance` | Condition | 1 float | `chance` outside 0..1 or not finite |
| `aibt.native.random-float` | Action, writes Float32 | 1 float | a bound not finite, `min` > `max` |
| `aibt.native.random-int` | Action, writes Int32 | 1 bounded (2 on a rejected sample) | `min` > `max`, `max` > 2147483647 |
| `aibt.native.wait-random-microseconds` | Action, stateful | 1 at Enter, 0 while waiting or aborted | `min` > `max` |
| `aibt.native.random-float3-in-radius` | Action, writes Float3 | 2 (flat) or 3 | a negative or non-finite radius, `min-radius` > `radius` |

Contract choices (not fixed by the ADR): `random-float` never writes `max` (a sample that rounds up
to `max` is clamped to the next float below); `random-int` uses UInt32 parameters capped at
`int.MaxValue`; the wait's full span `[0, uint.MaxValue]` takes one unbounded draw; the point is
uniform by area (flat, square-root radius) or by volume (cube root by 64-step bisection), angle by
a 16-term series, computed in double; a non-finite result fails after drawing. A Failure status
still commits the drawn state (the overflow test checks the replay after it).

The 28 existing nodes are unchanged: manifest and descriptor SHA-256 of each are identical before
and after (`existing28-unchanged.txt`, "ALL 28 EXISTING NODES UNCHANGED: True"; shard dump in
`shard-after-nodes/`).

### Tests (`fb89797`, `04ba623`)

- `NativeLibraryRandomTests` (34): every node against a replay of
  `NativeRandomStreamDerivationV1` (value and exact number of draws per tick), edge cases (chance 0
  and 1, `min` = `max`, full ranges, the largest sample and a rejected sample set by overwriting the
  slot's state word), invalid configurations, the wait's abort, 40-seed runs where the outcome
  depends on the draw. Statistical checks use 6σ margins (2000 rolls of chance 0.3: hits; 2000
  floats in [-2, 3): mean; 2500 ints in [3, 7]: each count).
- `HostRandomSeedingTests` (+3): the five nodes give the same per-tick digests in place and with
  Jobs in `TreeSimulation` (16 agents, 240 ticks); a host and a container agent with the same
  derivation inputs run them alike, in place and with Jobs.
- `NativeTreeAgentRandomSeedTests` (8): above.

### Changed expectations of existing tests (old → new)

| Test | Old | New |
|---|---|---|
| `TreeSimulationHostEquivalenceTests.Hosts_HaveZeroedRandomStreams_AndARandomNodeCannotRun` | hosts leave stream words zero; a random node is refused | replaced by `Hosts_SeedRandomStreams_AndARandomNodeRuns` (owner decision 4) |
| `NativeLibraryTimingTests` node list, `StandardNativeShardTests` (`StandardNativeLibraryIds.All`), `StandardNodeCatalogTests.StandardNativeTypeIds` | 28 IDs | 33 IDs |
| `NativeLibraryNodeTests` category check | Blackboard, Flow, Timing, Signals | also `Native/Random` |
| `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` | known failure: 95 expected, 100 found | known failure: 100 expected, 105 found (the list comes from `StandardNativeTypeIds`; the extra five are the sample nodes, as before) |

### Public API

`public-api-diff.txt`: additive. Runtime: `ProductionTreeHost.DefaultRandomSeed`,
`ProductionTreeHost.RandomSeed`, the generated `ProductionTreeHost.TryBootstrap(..., randomSeed, out failure)`,
`ProductionTreeProgram.TryBootstrap(host, randomSeed, ...)` and `NativeTreeAgent.RandomSeed` (six
entries in the API reference). `AIBT.StandardNodes`: the node, config and memory structs of the five
nodes. Removed lines outside `Tests/` are comments, the two bootstrap calls that now pass a seed, and
the sample. The group fix changes only `internal` methods.

### Guide, README, CHANGELOG, sample

- `Documentation~/native-node-library.md`: five rows in the mapping table (managed counterparts
  `aibt.unity.random-chance`, `random-float`, `random-int`, `wait-random`, `random-point`); the
  "Randomness" recipe (sample JSON, where the seed lives, instance ids and reproducibility, draws per
  tick, every dispatch mode); the sentences that said random nodes do not run on hosts are gone.
- `Documentation~/production-host.md`: the seed on the host; the stream advances under every policy.
- `CHANGELOG.md`: stage 3 entry, including the group-dispatch fix in plain words.
- `Samples~/NativeGuard` (`b3b3660`): the patrol picks one of three spots (`random-int` →
  `sweep-spot`, then `int-equals` and a signal per spot), whistles with `random-chance` 0.4 and rests
  1–3 s (`wait-random-microseconds`). Three spot markers light up; the HUD prints the seed, the tree
  instance and the picks. The README says when one seed repeats the picks.

### Screenshots and clips

- `palette-actions-native-random.png` (Random Float, Random Float3 In Radius, Random Int, Wait Random
  Microseconds) and `palette-conditions-native-random.png` (Random Chance): the Create Node palette
  under `Native/Random`.
- `graph-edit.png`: the sample tree, Valid, 5 AIBT2043 notes (`sweep-spot` is new, open question 1).
- `graph-live-1.png` … `graph-live-7.png`: Play Mode, the tower selected, "Live: Guard Tower (native)
  — running, run N". Run 1: guard → patrol → "Wait until the gate is open", then guard → watch →
  "Watch" (intruder seen); run 3: watch again; run 4: patrol → wait-gate. The run counter moving
  1 → 4 is the agent restarting after each finished patrol. The blackboard panel shows "live: —"
  for the native agent's keys (known, not in scope).
- `clips/seed0-a`, `seed7-a`, `seed0-b`, `seed7-b`: 30 s each of the Game view, the scene's seed set
  before each (`scripts/Record-SeedClips.ps1`, `SetDemoSeed.cs`), stills every 3 s. At 27 s:

  | Clip | Tree instance | Spots (+w = whistle) | Patrols | Whistles |
  |---|---|---|---|---|
  | seed0-a | 3 | A A+w B A A | 5 | 1 |
  | seed0-b | 4 | A A+w B A A+w | 5 | 2 |
  | seed7-a | 4 | A A B+w B C | 5 | 1 |
  | seed7-b | 4 | A A B+w B C | 5 | 1 |

  Different seeds differ from the second patrol on (seed 0 whistles at the second, seed 7 at the
  third; only seed 7 reaches spot C). The two seed 7 recordings match at the stills read (15 s:
  instance 3, "A A B+w", 3 patrols, 1 whistle and the same signal log in both; 27 s: as in the
  table). The two seed 0 recordings match in the spots and the first four patrols; the fifth whistle
  differs because the runs ended at different times (instance 3 vs 4 at 27 s): the intruder moves
  with frame time, so it interrupted a patrol at another moment, and from there the later instances
  draw from other streams. 6 s of seed0-a: instance 1, "A A+w", spot A lit cyan, the tower green from
  the whistle. The README and the guide describe exactly this.
- The Console after the live session holds only Editor-level messages (invalid GC handle,
  VirtualArtifacts), none from AIBT or the demo.

### Runs

All in `runs/`, through `Tools/Unity/Invoke-UnityTests.ps1` unless stated.

| Run | Result |
|---|---|
| `07-part-a-first` | first Part A run: HostRandomSeeding 6/9 and NativeTreeAgentRandomSeed 3/7 — the new tests' trees never reached their random node (0 rolls under every policy); the tests got trees that roll (the roller tree). TreeSimulationHostEquivalence 4/4 |
| `08-part-a-roller` | NativeTreeAgentRandomSeed 5/7: batched-jobs and pipelined-jobs 0 rolls — the group defect |
| `09-dump-after-seeding`, `13-dump-after-nodes`, `19-dump-after-group-fix` | identity dumps, 4/4 each; compared above |
| `10-part-a-host-classes` | 19 host/agent classes green, the two group probes red (the defect) |
| `11-part-a-playmode` | AIBT.PlayMode.Tests 2/2 |
| `12-part-bc-first` | NativeLibraryRandom 34/34, HostRandomSeeding 12/12, NativeLibraryTiming 30/30, NativeLibraryNode 55/55 |
| `14-editor-and-contracts` | AIBT.Editor.Tests 790/793: the known case and two stale API-reference checks (fixed by `9808498`) |
| `15-seed-clips` | the four recordings |
| `16`, `17` (`16a`, `17a`) | group fix red before / green after (above) |
| `18-group-fix-forced-policy-classes` | 14 classes green (above) |
| `20-final-host-agent-simulation-classes` | after the last code change: 26 filters, all green — AgentConsumerTools 14, AibtLinkXmlProcessor 3, ExternalTreeBlackboardWrite 9, GeneratedDispatchFieldOrdinalRegression 7, GeneratedDispatchLifecycleProof 14, GraphConsumerWorkflow 4, HostRandomSeeding 12, JobsPolicyEquivalence 28, McpSimulationTools 4, NativeLibrary* 138, NativeTreeAgent* 26, ProductionConsumerWorkflow 12, ProductionTreeHost 20, ProductionTreeScheduler 25, ProductionTreeWorldEffect 6, SchedulingProfile 18, StandardNativeShard 3, SubtreeNative 7, TreeSimulation* 102, NativeHotReloadGoldenEquivalence 4, SchedulingPolicyDriver 5, NativeExecutionAllocation 3, NativeAutoAdaptiveRealisticFeedback 2, NativeAutoAdaptiveSelection 7, NativeAutoSelection 25, NativeExecutionLargePopulationStress 1 |
| `21-final-playmode` | AIBT.PlayMode.Tests 2/2 (`-Mode playmode`) |
| `22-final-editor-and-contracts` | AIBT.Editor.Tests 792/793 (only the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`, 105 vs 100; UserGuideSnippet and McpDocumentationGenerators green); `AIBT.CodeGen.ContractTests` 6/6 (ABI v2 unchanged) |
| `23-verify-static` | `Tools~/Verification/Verify-Static.ps1` after the evidence and task card edits |

JobsPolicyEquivalence counts 29 in runs `10` and `18` and 28 in `20`: the temporary dump added one
test to that class (`P11002Stage3Dump_AgentPopulations`, a partial class) and was deleted before `20`.

### Parent-project files (scratch, not committed)

- `Assets/Samples/AIBT/Native Guard/`: `README.md`, `native-guard.aibt.json` and
  `Runtime/NativeGuard.cs` updated to the new sample (identical to `Samples~/NativeGuard`, `.meta`
  files aside; `diff -r`).
- `Assets/AibtNativeGuardDemo/`: re-created through **AIBT > Samples > Native Guard > Create Demo**
  (`scripts/CreateDemoViaMenu.cs`): `NativeGuard.runtime.asset`, `NativeGuard.unity` and the three
  materials. The recordings set the tower's Random Seed (0 / 7) before each clip; the scene was saved
  back with seed 0 afterwards.

Both stay untracked in the parent repository, as in stage 2. Nothing in the package refers to them.

### Open questions for the owner

1. **AIBT2043** now also shows for `sweep-spot`, which the sample writes with `random-int` through a
   native binding (same cause as stage 1 question 2 and stage 2 question 4).
2. **No tree-key read on `NativeTreeAgent`.** A game that wants a random value (a picked spot, a
   random point) reads it only through branches and signals, as the sample does; there is no public
   read of a Tree-scope key on the agent. The guide shows the signal pattern.
3. **Reproducibility of `NativeTreeAgent` runs** depends on the process-wide instance counter and on
   when runs end (the seed 0 recordings). A per-agent instance id, or a seed per run, would make one
   seed repeat regardless of timing; that would be a host lifecycle change and is not proposed here.

### Not covered by stage 3

- Stage 4 (quickstart, MCP recipes), observer re-evaluation, the AIBT2043 hint, `live: —`,
  `aibt.stdlib.random-condition` (unchanged), performance, a Player or IL2CPP build.
- A full nine-assembly run (the classes that boot a host, an agent or a simulation, the PlayMode
  assembly, the Editor and contract assemblies only).
- The Package Manager import itself: the sample was copied into `Assets/Samples/AIBT/Native Guard/`
  by hand.

### Acceptance run (orchestrator, 2026-11-20, stage 3)

Read by the orchestrator on `e944c5c` (clean working tree): the whole `Runtime/` diff
`d1218b1..e944c5c` line by line, the node source, the diffs of every existing test file, the ADR
diff, this section, the screenshots and the clip stills.

- **`Runtime/`.** Six files, +82/−7, in two commits. Seeding (`ba7c944`): the new
  `ProductionTreeHost.TryBootstrap` overload calls `SeedRandomStreams` after `TryBootstrapGenerated`;
  the old overload passes seed 0; `ProductionTreeProgram.TryBootstrap` and `NativeTreeAgent` pass the
  seed on. `DispatchSlot.SeedRandomStream` returns at once for a slot without a stream word, so a
  tree without random nodes is not written to. Group commit (`04ba623`): the lines of the diff the
  owner allowed (decision 8), internal methods only. Nothing else under `Runtime/` changed.
- **Other protected paths.** Nothing changed in `CodeGen~`, `Schemas~`, `Authoring/Validation`,
  `Authoring/Compilation`, `Agents`, `Documentation~/specifications/`, `package.json` or
  `ExpectedPublicAbiV2.txt`.
- **Existing tests.** The changed expectations are those in the table above: the pin test replaced,
  the node lists 28 → 33 and the category check. `SimulationTestPrograms.cs` gained two trees and a
  comment; no existing tree changed.
- **Identity dumps.** The SHA-256 of the four dumps in `before/`, `after-seeding/`, `after-nodes/` and
  `after-group-fix/` were computed again by the orchestrator: all equal. The dumps were not
  generated again. They cover forced BatchedJobsSameFrame; forced PipelinedJobs is covered only by
  the existing test classes of run `18`.
- **The sample copy** in the parent project is identical to `Samples~/NativeGuard` (`diff -r`).
- **Clips.** The 27 s stills of `seed7-a` and `seed7-b` show the same picks (A A B+w B C, 5 patrols,
  1 whistle, tree instance 4); `seed0-a` shows A A+w B A A.
- **Full run of all nine assemblies**, 01:35–01:48; Editor-log counters did not move during the run
  (`No command named` 0, main-thread timeouts 3 at the start and at the end). Raw output:
  `C:\UnityProjects\_aibt_acceptance\P11-002\stage3-acceptance\full-run-e944c5c.txt`.

| Assembly | `bad2892` | `e944c5c` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | — |
| `AIBT.Integration.Tests` | 349/349 | 403/403 | `NativeLibraryRandomTests` 34, `HostRandomSeedingTests` 12, `NativeTreeAgentRandomSeedTests` 8 |
| `AIBT.Editor.Tests` | 792/793 | 792/793 | — |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | — |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | — |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | — |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | — |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | — |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | — |

The one failure is the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (105 found,
100 expected).

Not done by the orchestrator: the group-fix tests were not run again on the code without the fix
(the red state is the session's run `16`); the clips were not recorded again; no Player or IL2CPP
build; no performance measurement of the new nodes or of the group commit.

To know: with host seeding, `aibt.stdlib.random-condition` runs on hosts too (it declares a stream);
a host's tree instance id is a process-wide counter, so one seed repeats a sequence only when the
hosts start in the same order after a domain reload (open question 3).

## Stage 4 — documentation pass (2026-11-20)

Approved by the owner 2026-11-20 (`23534c8`). Documentation and recipe source only: no change under
`Runtime/`, `CodeGen~`, `Schemas~`, `Authoring/`, `Agents`, `Editor/`, `package.json`,
`Documentation~/specifications/` or `ExpectedPublicAbiV2.txt`, and no MCP tool or CLI command
behaves differently. Raw transcripts, scripts and screenshots are in
`C:\UnityProjects\_aibt_acceptance\P11-002\stage4\`.

A commit that is not part of this stage, `3e00372` ("docs(P11-001): closed by the owner", Planning
files of P11-001, `work-items.json`, `MASTER_PLAN.md`), landed on the branch after `23534c8` and
before the stage 4 commits.

### Commits

| Commit | Content |
|---|---|
| `b5439d2` | `MCP/Documentation/McpRecipesDocumentGenerator.cs`: three native-library recipes; `Documentation~/generated/recipes.md` regenerated |
| `2f39af3` | `consumer-quickstart.md` section "Without writing a Burst node"; `UserGuideSnippetTests.QuickstartNativeLibraryOptInIsQuotedFromTheNativeGuardSample` |
| `b62f8d6` | `ai-and-mcp.md` "Native-only nodes", `cli.md`, `native-node-library.md`, `README.md` (sweep) |
| `029888b` | `CHANGELOG.md`: stage 4 entry, stage 3 parameter names |

### What was added where

- **Recipes** (`generated/recipes.md`, from `McpRecipesDocumentGenerator.AppendNativeLibraryRecipes`):
  - "find native library nodes and their bindings": `search_nodes`, `get_node_contract` (bindings
    are in `behaviorCaseContract.inputs`), `runtime_catalogs`;
  - "build a native tree from library nodes": nine MCP steps from `create_tree` (`formatVersion` 2,
    Tree keys of types Bool, Int32, Float32, Float3 and Int64) through `add_node` with `parameters`
    and `bindings`, one `apply_domain_patch` of 26 operations, `validate`, `compile` (AIBT3012
    expected), `inspect_tree`, `prepare_runtime` dry run and write. The tree is a guard
    (`float3-within-distance`) under `reactive-sequence`, `reactive-sequence-retaining` above the
    waits, a shared cooldown (`set-deadline` / `deadline-passed`), a `signal`, a `random-int` pick, a
    `wait-random-microseconds` and `idle` holds; plus the same steps as CLI commands. This first
    version lost its cooldown and counters at the end of every patrol; the rework below replaced it;
  - "change a native tree and prepare it again": `configure_node`, dry run for `existingAssetHash`,
    `prepare_runtime` with `overwrite` and `expectedAssetHash`; how bindings survive edits.
- **Quickstart** (`consumer-quickstart.md:11`): the opt-in line quoted from
  `Samples~/NativeGuard/Catalog/NativeGuardCatalog.cs` (new snippet test), the five categories, the
  hosts, the Native Guard sample, links to the guide and the recipes.
- **AI clients** (`ai-and-mcp.md:31`, "Native-only nodes"): discovery, format version 2, which tools
  run the nodes (`prepare_runtime` and the native hosts) and which do not (`compile` AIBT3012,
  `simulate` AIBT2021, `run_tests` FactoryFault), `runtime_hosts` / `read_trace` not seeing a
  `NativeTreeAgent` host, tools not tried. `cli.md:103` and `cli.md:133` the same for the CLI.
- **CHANGELOG** (`CHANGELOG.md:7`): stage 4 entry. Stages 1 and 2 already named their nodes and the
  re-prepare note; stage 3 had one wrong parameter name (below).

### Run live

Every recipe step was run against the Modules Editor (bridge port from `Library/AibtMcp.json`,
Pipeline CLI port 7810) before it was written down; the recipe text follows the transcripts.

| Transcript | What it shows |
|---|---|
| `cli/01-status-ensure` | bridge status and `mcp ensure` |
| `cli/02-runtime-catalogs` | catalog sets; `NativeGuardCatalog` lists `StandardNativeShard` (33 leaves) |
| `cli/03-nodes-search`, `04`, `05` | all 33 `aibt.native.*` nodes found; contracts of `set-deadline` and others; binding members only in `behaviorCaseContract.inputs` (id, type, no direction); manifest `reads`/`writes` empty |
| `cli/10-recipe-build` | the build recipe through `call`: create (v2), add, patch, validate (valid, policy applied, 8 AIBT2043 infos), compile (AIBT3012 on the first native node), inspect |
| `cli/11-recipe-prepare` | prepare against `SwarmArenaCatalog` → AIBT3012 naming the shard; against `NativeGuardCatalog` (full type name) → dry run and write, backend `native-generated-v2`; `simulate` (run at the end of the same script) → AIBT2021 for every native node |
| `cli/12-edit-bound-node-probe` | `configure_node` and `replace_node` keep bindings; `configure_node` with `parameterName` `bindings` → AIBT2025; remove + add rebinds (the v1 probe was not recorded here; see `rework/cli/E1`) |
| `cli/13-binding-errors-probe` | missing, undeclared and wrongly typed bindings → AIBT3018 + AIBT3015; `set_blackboard_keys` keeps v2 |
| `cli/15-runtime-hosts-native-agent` | `runtime hosts` = `[]` and `trace read 1` = AIBT9018 while the agent runs (finding 5) |
| `cli/16-recipe-reprepare` | write without overwrite → AIBT9018; dry run gives `existingAssetHash`; overwrite keeps the GUID |
| `cli/17-run-tests-and-simulations` | `run_tests` FactoryFault; no runtime simulations registered |
| `mcp/20-mcp-recipe`, `21-mcp-reprepare` | the same recipes through `mcp serve` (stdio JSON-RPC, `aibt_*` tools, nested JSON as strings), including the remove + add rebind as a dry run |
| `runs/30`–`34` | scratch scene `Assets/AibtRecipeScratch/NativeSentry.unity`, one GameObject with `NativeTreeAgent`, `_tree` set to the prepared asset (no code); agent state samples while running |
| `runs/40`–`43` | recompile and recipe regeneration (`scripts/RegenerateRecipes.cs` through `eval_file`) |

`cli/first-attempt-no-hold/` keeps the first attempt. Without the `idle` holds the root succeeded at
every update (5086 completed runs in one sample, `runs/31`) and the agent's host was replaced between two
queries. That run showed that keys written by nodes start every `NativeTreeAgent` run from their
defaults (every run is a new host; `NativeTreeAgent.cs:402` re-applies only facts). The trees were
deleted and the recipe was run again with `strike_hold` and `warn_hold`; the guide and the
quickstart now say it. (The holds kept a strike's run going but not a patrol's; the rework below
replaced them with a repeater at the root.)

### Screenshots

The first screenshots showed the first, superseded tree and were moved to
`screenshots/superseded-first-tree/`; the files below were taken again from the reworked tree (see
the rework section).

- `screenshots/graph-edit-native-sentry.png`: the recipe tree in the AIBT Graph (Edit Mode).
- `screenshots/graph-live-1.png` … `graph-live-3.png`: Play Mode, "Live: Sentry (native) — running,
  run 1". The blackboard shows `live: —` and the tree list "Needs native compile" (stage 2 and 3
  open questions).

No new code was written for the screenshots or the live run.

### Consistency sweep (old → new)

| File:line | Old | New |
|---|---|---|
| `README.md:283` | "seeded per tree instance from `NativeTreeAgent.RandomSeed`" | "seeded per tree instance from the host's seed (`NativeTreeAgent.RandomSeed`, default 0)" |
| `README.md:286` | "`aibt.native.*` nodes only in `NativeTreeAgent`" | "`aibt.native.*` nodes only on native hosts (`NativeTreeAgent`, `ProductionTreeHost`, `TreeSimulation`)" |
| `consumer-quickstart.md:68` | "Facts are kept across restarts." | "Facts are kept across restarts; keys that nodes write start each run from their defaults, because every run is a new host." |
| `CHANGELOG.md:42` | "`wait-random-microseconds` (a duration in [`min`, `max`] µs drawn once at start)" | "… [`min-microseconds`, `max-microseconds`] drawn once at start" |
| `native-node-library.md:128` | — (missing fact) | paragraph: written keys live as long as the run, so `zap-hold` keeps the run going |
| `native-node-library.md:253` | — | "See also" link to the recipes and to `ai-and-mcp.md#native-only-nodes` |
| `cli.md:133` | — | the `NativeTreeAgent` host is not listed by `runtime hosts`; watch it in the AIBT Graph |

Checked and correct: counts (33 native nodes: Blackboard 20, Flow 4, Timing 3, Signals 1, Random 5;
the package `node-catalog.md` lists 100 node types, Modules 105 with its samples), the five
categories in README, guide, quickstart and CHANGELOG, node documentation texts. Link check
(`scripts/check_links.py`, `runs/50-link-check-before-fixes.txt`) over README, `Documentation~/*.md`,
`generated/recipes.md` and the sample READMEs: 118 links, 0 broken; run again on the committed
docs (`runs/51-link-check-after-fixes.txt`): 118 links, 0 broken.

### Findings about tools (reported, not fixed)

1. **`create_tree` defaults to format version 1.** A tree created without `formatVersion` rejects a
   node with `bindings` (AIBT2042). The recipe passes `"formatVersion": 2`.
2. **No in-place rebind.** `configure_node` with `parameterName` `bindings` → AIBT2025; a binding is
   changed by removing and adding the node in one patch.
3. **`compile`, `simulate` and `run_tests` do not execute native-only nodes** (AIBT3012, AIBT2021,
   FactoryFault). Only `prepare_runtime` and the native hosts do.
4. **`run_tests` diagnostic is unclear**: "Executor factory failed: InvalidOperationException", with
   no node or reason, where `simulate` names each native node (AIBT2021).
5. **`runtime_hosts` / `read_trace` do not see a `NativeTreeAgent` host.** The agent's
   `ProductionTreeHost` has `HideInInspector, DontSave` and a trace channel, but
   `FindObjectsByType<ProductionTreeHost>` (`McpRuntimeTools.cs:206`) returns 0 for it, so
   `trace read` answers AIBT9018. Documented in `ai-and-mcp.md` and `cli.md`.
6. **Manifest `reads` / `writes` are empty for native nodes**; bindings show only in
   `get_node_contract` `behaviorCaseContract.inputs`, without direction.
7. **AIBT2043** shows for keys that native nodes write (stage 1 question 2).
8. **`tree validate samples.native-guard` → AIBT9022 "No tree with id"** in Modules, probably
   because the parent project has two copies of the sample tree (`Assets/Samples/AIBT/Native Guard/`
   and `Assets/AibtNativeGuardDemo/`). Not investigated further.

### Tests

After `Invoke-UnityRecompile.ps1` (`runs/42-recompile.txt`), `runs/60-doc-and-mcp-tests.txt`:

| Class | Result |
|---|---|
| `UserGuideSnippetTests` | 5/5 (one new case) |
| `McpDocumentationGeneratorsTests` | 14/14 |
| `McpToolDispatcherTests` | 7/8, the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (105 vs 100) |

Changed expectations of existing tests: none. The regenerated `generated/recipes.md` is the only
generated-doc snapshot that changed.

### Parent-project files

Created and removed (`delete_asset`, copies in `scratch-copy/`, `runs/70`, `runs/71`):
`Assets/AibtRecipeScratch/` with `NativeSentry.unity`, `native-sentry.aibt`,
`native-sentry.runtime.asset`, `native-sentry-mcp.aibt`, `native-sentry-mcp.runtime.asset`,
`native-sentry.aibtcase.json`, `probe-edit.aibt`, `probe-v1.aibt` and their `.meta` files. The scene
Native Guard was reopened first. Nothing in `ProjectSettings/` or the parent `git status` points to
them. `Assets/Samples/AIBT/Native Guard/` and `Assets/AibtNativeGuardDemo/` (earlier stages) were
left as they were. `Library/AibtMcp.json` was written by `mcp ensure`.

### Not covered by stage 4

- `runtime_simulations`, `describe_agent` and `read_agent_trace` with library nodes (none registered
  in Modules); `ai-and-mcp.md` says they were not tried.
- The Package Manager import of the sample, a full nine-assembly run, a Player or IL2CPP build,
  performance.

### Open questions for the owner (stages 1–4)

1. Observers are not re-evaluated on native hosts (stage 1).
2. AIBT2043 ignores writes through native bindings (stage 1).
3. The AIBT Graph shows `live: —` for native agent keys and "Needs native compile" for prepared trees
   (stages 2–4).
4. No public read of a Tree-scope key on `NativeTreeAgent` (stage 3).
5. Reproducibility of `NativeTreeAgent` runs depends on the process-wide instance counter (stage 3).
6. The Package Manager import of the Native Guard sample has not been exercised (stages 2–4).
7. `BehaviorTreeAgent.Restart(true)` on a tree still running `idle` faults with AIBT4001 (stage 1).
8. New in stage 4: tool findings 1, 2, 4, 5 and 8 above; whether `create_tree` should default to
   version 2, whether `runtime_hosts` should list hidden agent hosts, and whether bindings should
   appear in the node manifest.

## Stage 4 — rework after the acceptance run (2026-11-20)

The orchestrator's acceptance run on `fe28a5e` (all nine assemblies green except the known failure)
left three points: the recipe tree promised cooldowns and counters that do not last, the Edit Mode
screenshot showed the first tree, and the AIBT2042 claim had no transcript. Base `3cd9fbb`. Raw
files: `C:\UnityProjects\_aibt_acceptance\P11-002\stage4\rework\`. Same limits as stage 4: no code
outside the recipe generator, no tool or host behaviour changed, the Native Guard sample unchanged.

| Commit | Content |
|---|---|
| `cb20509` | recipe tree loops inside one run: generator, `generated/recipes.md`, CHANGELOG |
| `b94ca96` | `native-node-library.md`: a finished patrol ends a Native Guard run too |
| this commit | this section, corrected rows above, task card note |

### Point 1: the recipe tree lost its cooldown and counters

The orchestrator's reading was checked live first, on the first tree rebuilt from the stage 4
argument files (`cli/A1`, `cli/A2`, scene `runs/B0`). Facts at t = 29.271 s: target 1 m away, seen,
gate open. Signal log `runs/B4`, summary `runs/C10`:

- 12 strikes (signal 1) in 20.6 s; gaps 1.623, 1.920, 1.863, 1.966, 1.825, 1.844, 1.990, 1.986,
  1.857, 1.508, 1.413 s: none reaches the 2 s cooldown;
- every strike in a new host (12 hosts), `completedRuns` 0 → 11 (`runs/B3`).

The reading is right: `patrol` succeeding is the root succeeding, the run ends, `NativeTreeAgent`
starts a new host from the key defaults, and `ready_at`, `shots` and `fatigue` start again from 0;
`fatigue` never exceeds 0.25, and `strike` fires at once after a patrol shorter than the cooldown.

**The tree was changed** (`rework/scripts/tree_v2.py` is the one source of the CLI arguments, the MCP
run and the recipe text):

- the root is `sentry_loop`, `aibt.core.repeater` (`count` 1000000, `stopOnFailure` false) over
  `sentry`: one run lasts up to 1000000 rounds (a strike, a warning or a patrol each), so the keys
  that nodes write last across rounds; a failed round does not stop the loop. This is the pattern
  the README already gives ("loop inside the tree (a repeater at the root)");
- the `idle` holds are gone: a strike or warning ends its round, and the next rounds patrol until
  the cooldown is over;
- `fatigue` is read: after `tire`, `report_tired` (`succeeder`) → `tired` (`memory-sequence`) →
  `is_tired` (`float-at-least`, threshold 1) and `signal_tired` (signal 20);
- descriptions now say what happens: `shots` "Strikes in this run", `fatigue` "Grows by 0.25 with
  every finished patrol in this run", `wait_gate` "…if it stays closed, the patrol fails and the next
  round starts", and the "What each step relies on" list explains the repeater, the round, the
  counters and the short-name refusal;
- `create_tree` refuses a decorator root without its child (AIBT2023, `cli/C1a`, and the MCP dry run
  in `mcp/F1`), so the root is created as a `memory-sequence` and the first patch operation
  (`replace`) turns it into the repeater once `sentry` is its child; `replace` keeps the id, the
  description and the children (`cli/C1` inspect). The recipe has 10 steps, the patch 29
  operations, the tree 32 nodes.

**Run again live:**

| Transcript | What it shows |
|---|---|
| `cli/C1-recipe-build-and-prepare` | create, add ×3, patch, validate (valid, policy applied, 8 AIBT2043), compile AIBT3012 on `target_close`, inspect (root `aibt.core.repeater`, 32 nodes), prepare dry run and write (`native-generated-v2`) |
| `cli/C11-recipe-reprepare` | configure `strike_cooldown`; write without overwrite → AIBT9018; dry run → `existingAssetHash`; overwrite; GUID before = after |
| `cli/E3-prepare-catalog-without-shard` | against `SwarmArenaCatalog` → AIBT3012 "…does not declare node 'aibt.native.int-equals'…StandardNativeShard…" (the recipe's quoted message) |
| `mcp/F1-mcp-recipe-v2` (+ `-summary`) | the whole build recipe, the change recipe, `configure_node` `bindings` → AIBT2025, remove + add rebind (dry run, `insertIndex`), `simulate` AIBT2021, both probes below |
| `runs/C2`–`C9` | the reworked asset on the scratch agent, Play Mode, facts set by `eval_file` scripts (`rework/scripts/ProbeCooldown.cs`, `ProbeFarClosedGate.cs`), signal log |

Live result (`runs/C9`, `runs/C10`), facts at t = 0.929 s (target 1 m, seen, gate open):

- 11 strikes, gaps 2.002, 2.004, 2.013, 2.009, 2.003, 2.006, 2.009, 2.011, 2.004, 2.007 s;
- one host for the whole run, `completedRuns` 0 at 21.7 s (`runs/C6`);
- signal 20 first at 9.491 s, when the fourth patrol finished (patrols finished at 3.674, 5.943,
  7.274 and 9.491 s; patrols cut by a strike did not count), then after every finished patrol.

Then target 20 m away, not seen, gate closed (t = 22.327 s): the patrol fails every 3.0 s (gaps
3.008, 3.001, 3.003 s, the `wait_gate` timeout), the loop goes on, `completedRuns` still 0, same host
at 35.96 s (`runs/C8`).

**Native Guard sample and the guide's `zap-hold` paragraph** (read, not run). The sample has the
same mechanism: its root succeeds when a patrol finishes, which ends the run and resets
`weapon-ready-at`. While the intruder is seen, `watch` (`idle`) holds the run, so the cooldown is
kept in the usual case. A patrol normally lasts longer than the 1.5 s cooldown (the gate takes about
1 s to open, the rest 1–3 s). If a patrol was cut after the gate started to open, the gate stays
open (it closes only on signal 4), the next patrol passes `wait-gate` at once and can end after
about 1 s; an intruder close again within 1.5 s of the zap is then zapped at once. The zap pushes
the intruder 4 m back, so this is rare. The sample was not changed (finding 10); the guide paragraph
got the sentence "A finished patrol ends the run too…" (`native-node-library.md:134`), pointing to
the repeater of the recipe.

### Point 2: screenshots

The first screenshots (first tree: three children under `strike_fire`, two under `warn_fire`) were
moved to `screenshots/superseded-first-tree/`. Taken again from the reworked tree, no new code:

- `screenshots/graph-edit-native-sentry.png`: Edit Mode, repeater root, 32 nodes (`runs/D4`);
- `screenshots/graph-live-1.png` … `graph-live-3.png`: Play Mode after `SetCloseFacts.cs`,
  "Live: Sentry (native) — running, run 1", path Repeater → Reactive Selector Retaining → patrol →
  rest highlighted; `completedRuns` 0 and the same host afterwards (`runs/D3`).

### Point 3: AIBT2042 and the "Run live" table

AIBT2042 had no transcript (the stage 4 v1 probe was run without saving its output). Run live now:

- CLI `cli/E1-v1-tree-rejects-bound-node`: `create_tree` without `formatVersion` is accepted and
  writes `"formatVersion": 1`; `add_node` of `bool-is-true` with `bindings` → accepted false, AIBT2042
  "Generated bindings and Agent/Shared scopes require tree format version 2." and AIBT2042
  "Generated compiled format v2 requires tree format version 2.";
- MCP `mcp/F1`: the same (inspect reports format version 1, the same two AIBT2042).

Every row of the stage 4 "Run live" table was checked by `rework/scripts/check_run_live_table.py`
(`runs/G1`): 32 claims, 4 not found in the named transcript.

| Claim | Found | Correction (old → new) |
|---|---|---|
| contract of `set-deadline` in `cli/03`, `04`, `05` | in `05` | none: the row names the three files together |
| `cli/11`: `NativeGuardCatalog` "(short type name accepted)" | nowhere; run live in `cli/E2`: `--catalog NativeGuardCatalog` → AIBT9018 "Select one exact catalog from runtime_catalogs." | "(short type name accepted)" → "(full type name)"; the recipe now says a short name is refused |
| `cli/12`: "a v1 tree rejects a bound node (AIBT2042)" | nowhere | → "(the v1 probe was not recorded here; see `rework/cli/E1`)" |
| `cli/17`: "`simulate` AIBT2021 for every native node" | in `cli/11` (end of the prepare script) and `mcp/20` | moved from the `cli/17` row to the `cli/11` row |

Other lines of the stage 4 section that became wrong were corrected in place: the recipes bullet of
"What was added where" (marked as the first version), the `first-attempt-no-hold` paragraph (the
holds were replaced) and "Screenshots".

### Other corrections (old → new)

| File:line | Old | New |
|---|---|---|
| `generated/recipes.md` (build recipe) | 9 steps, root `reactive-selector-retaining`, `idle` holds, 26 operations, "Grows with every patrol", "Shots fired." | 10 steps, repeater root, 29 operations, counters and descriptions as above, AIBT2023 and short-name notes |
| `native-node-library.md:134` | — | "A finished patrol ends the run too…" |
| `CHANGELOG.md:11` | "(Tree-scope keys of five types, a guard under a reactive composite, a cooldown shared by key, signals, …)" | "(…, a repeater at the root that keeps one run and its keys going, a guard …, counters, signals, …)" |
| `CHANGELOG.md:25` | "…explains why the Native Guard zap ends in `idle`." | "…ends in `idle`, and that a finished patrol also ends the run." |

### Tests and checks

After `Invoke-UnityRecompile.ps1` (`runs/H1`) and regeneration through the generator (`runs/H2`;
the generated recipes are identical to `rework/scripts/native-recipes-v2.md`), `runs/H3`:
`UserGuideSnippetTests` 5/5, `McpDocumentationGeneratorsTests` 14/14, `McpToolDispatcherTests` 7/8
(the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`, 105 vs 100). Changed expectations:
none; the regenerated `generated/recipes.md` is the only generated snapshot that changed. Link check
(`runs/H4`): 119 links, 0 broken. `Verify-Static.ps1`: `runs/H6`.

### Parent-project files

Created and removed again (`runs/H5`, copies in `rework/scratch-copy/`): `Assets/AibtRecipeScratch/`
with `NativeSentry.unity`, `native-sentry.aibt`, `native-sentry.runtime.asset`,
`native-sentry-mcp.aibt`, `native-sentry-mcp.runtime.asset`, `probe-v1.aibt`, `probe-v1-mcp.aibt` and
their `.meta` files. The Native Guard scene was reopened first. No trace in `ProjectSettings/`.

### New findings (reported, not fixed)

9. **`create_tree` refuses a decorator root without its child** (AIBT2023), and `rootNodeJson` takes
   no children, so a tree with a repeater at the root needs a composite root and a `replace`.
10. **Native Guard:** a finished patrol ends the run and resets `weapon-ready-at`; the cooldown can
    be lost in the narrow case above (by reading; not run). Sample unchanged.
11. **The stage 4 evidence claimed a short catalog name was accepted**; it is refused (AIBT9018).
    Not a tool defect: the text was wrong and is corrected.

### Acceptance run (orchestrator, 2026-11-20, stage 4)

Two rounds. Round 1 on `fe28a5e`: the diff `23534c8..fe28a5e` was read in full and all nine
assemblies ran (10:41–10:59; 815/815, 403/403, 793/794, 65/65, 79/79, 33/33, 36/36, 6/6, 2/2; raw
output `C:\UnityProjects\_aibt_acceptance\P11-002\stage4-acceptance\full-run-fe28a5e.txt`). Three points went back to the session: the recipe tree
promised a cooldown and counters that end with every patrol (derived by reading, then confirmed
live by the session), the Edit Mode screenshot showed the first tree, and the AIBT2042 claim had no
transcript. Round 2 on `0485779` (clean working tree) is the acceptance.

- **Protected paths.** `23534c8..0485779` changes no file under `Runtime/`, `CodeGen~`, `Schemas~`,
  `Authoring/`, `Agents`, `Editor/`, `Samples~`, `MCP~`, `Documentation~/specifications/`, and not
  `package.json` or `ExpectedPublicAbiV2.txt`. Code: `MCP/Documentation/McpRecipesDocumentGenerator.cs`
  (recipe text only) and one new test in `Tests/Editor/Documentation/UserGuideSnippetTests.cs`. No
  existing test expectation changed.
- **Recipe against the transcripts.** The quoted messages (AIBT3012 with the shard hint, AIBT9018,
  AIBT2025, AIBT2023, both AIBT2042), `totalCount` 33 and the operation count were found in the named
  files of `stage4\` and `stage4\rework\`.
- **Signal logs recomputed** from `rework\runs\B4-signal-log.txt` and `C9-signal-log.txt` by the
  orchestrator's own script. First tree: 12 strikes, gaps 1.413–1.990 s (none reaches 2 s), 12 hosts,
  `completedRuns` 0 → 11. Reworked tree: 11 strikes, gaps 2.002–2.013 s, one host, `completedRuns` 0,
  signal 20 first at 9.491 s and 7 times in the segment. They match the rework section.
- **Counts.** `generated/node-catalog.md`: 100 node types, 33 `aibt.native.*`, 20 in
  `Native/Blackboard`; `Native/Flow` is `fail`, `idle`, `succeed`, `wait-microseconds`. The quickstart's
  opt-in line equals `Samples~/NativeGuard/Catalog/NativeGuardCatalog.cs`.
- **Screenshots.** `screenshots\graph-edit-native-sentry.png` and `graph-live-2.png` show the reworked
  tree (repeater root); the live one shows "running, run 1" with the patrol path highlighted,
  `live: —` for every key and "Needs native compile" in the tree list (open question 3).
- **Parent project.** `Assets/AibtRecipeScratch` does not exist; the two scratch folders of the
  earlier stages are as before.
- **Full run of all nine assemblies** on `0485779`, 18:16–18:32; Editor-log counters did not move
  (`No command named` 0, main-thread timeouts 0 at the start and at the end). Raw output:
  `C:\UnityProjects\_aibt_acceptance\P11-002\stage4-acceptance\full-run-0485779.txt`. A first attempt at 11:20 was cut after the Runtime assembly
  (815/815) when the computer was restarted at 11:34
  (`C:\UnityProjects\_aibt_acceptance\P11-002\stage4-acceptance\full-run-0485779-interrupted-by-reboot.txt`); the Editor was started again at 18:15 and
  the run repeated from the beginning.

| Assembly | `e944c5c` | `0485779` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | — |
| `AIBT.Integration.Tests` | 403/403 | 403/403 | — |
| `AIBT.Editor.Tests` | 792/793 | 793/794 | `UserGuideSnippetTests.QuickstartNativeLibraryOptInIsQuotedFromTheNativeGuardSample` |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | — |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | — |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | — |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | — |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | — |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | — |

The one failure is the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (105 found,
100 expected).

Not done by the orchestrator: no recipe step was run again (the transcripts were read); the MCP and
CLI transcripts were not read line by line; the Native Guard cooldown case of finding 10 was not
run; no Player or IL2CPP build; the Package Manager import of the sample was not exercised.

To know: on `NativeTreeAgent` a key that nodes write lasts only as long as the run (every run is a
new host), so cooldowns and counters need a tree that does not finish, as the recipe's repeater
root; the Native Guard sample can lose its cooldown when a patrol ends early (finding 10, by
reading).
