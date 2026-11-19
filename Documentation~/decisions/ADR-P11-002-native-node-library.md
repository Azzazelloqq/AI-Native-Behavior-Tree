# ADR P11-002: A library of ready-made native nodes

- Status: Accepted (2026-11-19). Proposed the same day; the owner accepted it with all six
  recommendations of §8 (see "Owner decisions" below) and approved stage 1 of §7. Stages 2–4 each
  need their own go-ahead.
- Decision ID: `AIBT-050`
- Task: `Planning~/Tasks/P11/P11-002-native-node-library.md`
- Origin: item 2 of the Phase 11 feature list; "F4 native node library" in
  `Documentation~/decisions/ADR-P10-008-simulation-container.md:733-734`.

File references are relative to the package root (`Assets/AIBT`) at commit `9ff7480`.

Words used here:

- **native node** — a Burst leaf declared with `[AibtBurstNode]` (or the short `[AibtAction]` /
  `[AibtCondition]`), run by the native executor through a generated catalog.
- **shard** — one assembly of native nodes with one `[AibtCatalogShard(id, version)]`; every
  `[AibtBurstNode]` type in that assembly belongs to it (`CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs:1897-1899`).
- **catalog set** — a project's `[AibtCatalogSet(id, version, typeof(ShardA), …)]`; it decides
  which shards a native tree can dispatch.
- **managed library** — the 52 `aibt.unity.*` nodes for `BehaviorTreeAgent` (`Agents/Nodes/Standard/`).
- **library node** — a node this ADR proposes to ship in the package's native shard.

## 0. Starting point: the library already exists in seed form

This ADR does not create a native library from nothing. P9-002 shipped a package shard of native
nodes, and P9-004 renamed it:

- Shard `aibt.native.standard`, version 1, assembly `AIBT.StandardNodes`
  (`Authoring/Registry/Generated/StandardNative/StandardNativeNodes.cs:11`,
  `AIBT.StandardNodes.asmdef`, `autoReferenced: true`). Four nodes:
  `aibt.native.bool-is-true` (`:29-48`), `aibt.native.float-at-least` (`:66-86`),
  `aibt.native.set-float` (`:104-124`), `aibt.native.wait-microseconds` (`:144-168`).
- It is native-only and opt-in: a project lists `typeof(StandardNativeShard)` in its own catalog set
  (comment at `StandardNativeNodes.cs:5-8`; verdict in `Planning~/Evidence/P9-002/README.md:73-108`).

Two existing rules are the starting point and this ADR keeps both:

- **Naming rule (owner, P9-004).** `aibt.native.*` is for native-only nodes; `aibt.stdlib.*` is
  reserved for nodes whose manifest is byte-identical on both execution paths
  (`StandardNativeNodes.cs:9-10`; `Planning~/Evidence/P9-004/README.md:29-31`; the parity test is
  `BuiltInLeafManifestsTests`, `Planning~/Evidence/P7-028/README.md:82-95`).
- **Promotion rule (P7-036).** A *sample* node becomes a library node only with "evidence from at
  least two independent tree usages", through its own implementation card
  (`Planning~/Tasks/P7/P7-036-swarm-arena-ux-and-node-review.md:43-47`). P7-036 applied it and
  promoted nothing (`Planning~/Evidence/P7-036/verification.md:43-53`). This ADR justifies its first
  set differently — by an existing managed counterpart, not by sample reuse — so that departure is
  owner question Q3.

So the work is: **grow `aibt.native.standard` into a library that lets an author build useful
native trees without writing Burst nodes.**

## 1. Goal and user stories

**Tree author (graph editor, `.aibt.json`, MCP).**

1. Builds a guard tree for `NativeTreeAgent` from package nodes only: "if `enemy-position` is within
   8 m of `self-position`, set `alert` and send signal 2; otherwise count patrol steps and wait
   1.5 s". Finds every node in the palette under `Native/…` and in MCP `search_nodes`.
2. Keeps a shared cooldown in a key (`set-deadline` / `deadline-passed`) so two branches respect
   the same cooldown, which the per-node `aibt.core.cooldown` cannot do.
3. Reads in the node catalog which managed node each native node replaces (`aibt.unity.compare-int`
   → `aibt.native.int-at-least`, …), so a tree prototyped on `BehaviorTreeAgent` can be moved to
   the native layer node by node.

**Game programmer.** Adds one line — `typeof(StandardNativeShard)` — to the project's catalog set,
feeds facts with `NativeTreeAgent.SetFact` and reacts to `OnEffect("aibt.native.signal", (int code) => …)`.
No Burst code for blackboard logic, timers, counters, distances or (after Q4) random choices. Nodes
that move, animate or query physics stay game code — the native layer never touches the scene
(`README.md:15`).

## 2. What exists today

### 2.1 Every shipped node, by namespace

| Namespace | Count | Kind | Executor | Deterministic | Thread | Defined in | Tests |
|---|---|---|---|---|---|---|---|
| `aibt.core.*` | 13 | 7 composites, 6 decorators | both | yes | job-safe (lifecycle machine) | `Authoring/Model/Nodes/BuiltInNodeManifests.cs:8-20`, native kinds `Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs:157-159`, `Runtime/Execution/Native/Decorators/NativeDecoratorPolicyV1.cs:26-38`, `Runtime/Execution/Native/Composites/Reactive/NativeReactivePolicyV1.cs:5-10`; reference handlers under `Runtime/Execution/Reference/Composites/**` and `Decorators/ReferenceDecoratorContracts.cs:62-67` | both (golden cases, equivalence) |
| `aibt.stdlib.*` | 2 | Action, Condition | both | `wait` yes; `random-condition` declared **no** | job-safe | native `Authoring/Registry/Generated/BuiltInLeaves/Runtime/BuiltInLeafNodes.cs:28-63`, `:75-116`; reference `Authoring/Model/Nodes/BuiltInLeafManifests.cs:28-73`, `:75-121` | manifest parity `BuiltInLeafManifestsTests`; native in `Tests/Integration/Simulation/**` |
| `aibt.native.*` | 4 | 2 Conditions, 2 Actions | **native only** | yes | job-safe | `StandardNativeNodes.cs` (§0) | native only: `Tests/Integration/NativeRuntime/StandardNativeShardTests.cs:49-91`; presence `Tests/Editor/Agents/StandardNodeCatalogTests.cs:66-76` |
| `aibt.unity.*` | 52 | 12 Conditions, 40 Actions | **managed only** (`BehaviorTreeAgent`) | no (all) | main thread | `Agents/Nodes/Standard/*.cs`; manifests built by reflection in `Agents/Runtime/ManagedNodeType.cs:34-124` (domain `MainThread`, deterministic false) | managed only, `Tests/Editor/Agents/Standard*Tests.cs` |
| `aibt.samples.*` | 6 Burst + 1 managed | — | sample-owned | — | — | §2.4 | sample tests |

Clocks and random: `aibt.core.timeout` and `aibt.core.cooldown` read the per-update time point in
both executors (native `NativeLifecycleMachineV1.cs:406`, `:568-577`, `:1406-1467`; reference
`Runtime/Execution/Reference/Core/ReferenceExecutionMachine.cs:822-871`, `:1198-1260`). Native nodes
read the same point through `TryGetTimeMicroseconds` (`Runtime/Nodes/Contracts/BurstNodeContracts.cs:459`,
`:500`); there is no tick count or delta API. Random comes only from the per-node PCG stream of
`[AibtRandomStream]` nodes (`time-and-random-v1.md:17-74`). Managed nodes use `Time.deltaTime`
through the agent clock and the global `UnityEngine.Random`.

The four native nodes against their managed counterparts:

| Native | Managed counterpart | Gap |
|---|---|---|
| `bool-is-true` | `is-true` | no `expected` parameter |
| `float-at-least` | `compare-float` | only `>=` |
| `set-float` | `set-float` | equivalent |
| `wait-microseconds` | `wait-seconds` | no `random-extra` |

### 2.2 The 52 managed nodes: which could be native

Classification from reading every node (`Agents/Nodes/Standard/*.cs`, `[BtNode]` lines given):

- **A — blackboard and pure logic (22 nodes), portable as blackboard-only native nodes.**
  `is-true` (`BlackboardNodes.cs:44`), `compare-float` (`:55`), `compare-int` (`:66`), `set-bool`
  (`:77`), `set-int` (`:91`), `set-float` (`:105`), `set-vector3` (`BlackboardValueNodes.cs:5`),
  `copy-bool|int|float|vector3` (`:21-63`), `increment-int` (`:78`), `increment-float` (`:93`),
  `toggle-bool` (`:107`), `compare-int-keys` (`:120`), `compare-float-keys` (`:133`),
  `is-vector3-set` (`:146`), `distance` (`:238`), `succeed`, `fail`, `idle` (`FlowNodes.cs:60-72`),
  `wait-frames` (`WaitNodes.cs:57`).
  Native counterpart today: `is-true` (partial), `compare-float` (`>=` only), `set-float`;
  `wait-frames` ≈ `aibt.stdlib.wait` (ticks).
- **B — needs time or random only (9 nodes), portable with the host clock and the native stream.**
  `random-float` (`BlackboardValueNodes.cs:157`), `random-int` (`:172`), `random-chance`
  (`CheckNodes.cs:67`), `start-cooldown` (`:78`), `is-cooldown-ready` (`:94`), `time-since-changed`
  (`:104`, also needs per-key change times), `wait-seconds` (`FlowNodes.cs:35`), `wait-random`
  (`WaitNodes.cs:5`), `wait-until` (`:33`).
  Native counterpart today: `wait-seconds` (`wait-microseconds`), `random-chance` ≈
  `aibt.stdlib.random-condition` (see §2.6).
- **C — needs Unity world access (21 nodes), not library material on the native side.**
  Animation (3), audio (1), navigation (2), movement (7: `move-towards`, `look-at`,
  `is-within-distance`, `rotate-towards`, `turn-by`, `move-direction`, `keep-distance`), GameObject
  (4), `line-of-sight` (Physics), `raise-event`, `log`, `random-point` (reads `transform.position`
  once when no centre key is bound, `BlackboardValueNodes.cs:210-213`). They touch `Transform`,
  `Animator`, `AudioSource`, `NavMeshAgent`, `Physics`, `Debug.Log` or agent events.

Net: 46 of the 52 have no native counterpart; 31 (A + B) could have one without touching the scene.

### 2.3 World access on the native side (what a library node may assume)

- **Facts in**: Tree-scope keys written from outside — `NativeTreeAgent.SetFact<T>` for bool, int,
  long, float, double, `Float2Value`, `Float3Value`, `QuaternionValue` (`Runtime/Integration/NativeTreeAgent.cs:112-134`),
  through P7-039 external writes (`ProductionTreeHost.cs:263-334`). A key that any node writes is
  refused for external writes (`blackboard-v1.md:80`).
- **Effects out**: command bindings delivered to the game by binding-ID hash
  (`NativeTreeAgent.OnEffect`, `:137-153`; `ProductionTreeWorld.cs:13-18`). Hosts deliver only
  `Int32`, `Float32` and `Float3` payloads (`Runtime/Integration/ProductionTreeEffects.cs:6-12`;
  `ADR-P7-041-production-world-integration.md:52-65`).
- **Container only**: snapshot inputs (`[AibtInput]`), registered payload types and async actions
  work in `TreeSimulation`, not on hosts (`GeneratedTreeDispatchAdapterV2.cs:888-892`).
- **Channel identity is per node type, not per node instance.** Blackboard keys are chosen per node
  instance (the `GeneratedHandle` field stores an access ordinal, `GeneratedTreeDispatchAdapterV2.cs:836-847`),
  but command, input and action IDs are fixed strings in node code, hashed with FNV-1a
  (`Runtime/Simulation/SimulationChannels.cs:11-59`). A library node that emits an effect therefore
  imposes its channel name on the game.

Consequence: a library node may assume **only Tree-scope blackboard keys, the update time point, its
random stream, and fixed package-named effect channels with Int32/Float32/Float3 payloads.** Anything
that reads the scene stays game code (a fact in, an effect out).

### 2.4 What samples and fixtures had to write themselves

| Node | Where | Generic? |
|---|---|---|
| `aibt.samples.first-agent.move` | `Samples~/FirstAgent/Nodes/MoveNode.cs:25-37` | partly (move until distance ≤ stop; emits `move-speed`) |
| `aibt.samples.swarm.within-radius` | `Samples~/SwarmArena/Runtime/Nodes/SwarmArenaNodes.cs:33-91` | **yes** — Float3 distance check, used three times in one tree |
| `aibt.samples.swarm.steer` | `:124-192` | partly (seek/flee direction; gameplay effects) |
| `aibt.samples.swarm.attack-with-cooldown` | `:226-271` | no (its cooldown duplicates `aibt.core.cooldown`) |
| `aibt.samples.threshold-condition` | `Samples~/BurstNodes/Runtime/PublicBurstNodeSample.cs:26-71` | yes — threshold compare (an authoring demo) |
| `aibt.samples.async-write-action` | `:114-184` | no (pattern demo) |
| `samples.simple.next-waypoint` (managed) | `Samples~/SimpleAgent/Runtime/GuardNodes.cs:10-26` | no |

Test fixtures and the Phase 10 benchmark repeat the same two generic shapes — a threshold compare
and a constant effect — in their own shards (`Tests/Editor/CodeGen/GenerationP7039/ExternalTreeWriteFixtureNodes.cs:30-84`,
`Tests/Editor/CodeGen/GenerationP10010/SimulationIoFixtureNodes.cs:45-126`,
`Benchmarks~/Phase10/PlayerPerf/Assets/AibtPerf/IoNodes/PerfIoNodes.cs:41-84`). No sample uses
`StandardNativeShard`: FirstAgent lists only its own shard (`Samples~/FirstAgent/Catalog/FirstAgentCatalog.cs:5`),
Swarm Arena likewise (`Samples~/SwarmArena/Catalog/SwarmArenaCatalog.cs:5`).

### 2.5 What common behaviour-tree libraries treat as standard

Categories, not any product's list: blackboard set / copy / compare / arithmetic; vector distance and
"is set"; timing (wait, wait-until, cooldown, timeout); random value and random chance; flow leaves
(succeed, fail, run forever); decorators (invert, force result, repeat, retry / until, limit, cooldown,
timeout); events or messages to the game; debug log; and engine-bound movement, perception,
animation and audio. AIBT covers the decorators natively except retry/until, force-running and limit
(§2.1), and has every category on the managed side. On the native side it has only the four nodes
above.

### 2.6 Facts that limit what a library node can be

1. **No native decorators or composites.** `BurstNodeKind` is `{ Condition, Action }`
   (`BurstNodeContracts.cs:6`); the lifecycle machine treats every non-`aibt.core` type as a
   childless generated leaf (`NativeLifecycleMachineV1.cs:1990-1991`); `aibt.core.*` cannot be
   claimed by a shard (`AIBT5012`, `BurstNodeGenerator.cs:858-862`) and is "reserved for structural
   composites and decorators … adding one needs an ADR" (`decisions.md`, AIBT-048). A library is
   leaves only.
2. **Native parameters are `Bool`, `UInt32`, `UInt64`, `Float32` only** (`BurstNodeGenerator.cs:486-491`,
   `:2282`; `burst-node-authoring.md:3-4`). No `Int32`, enum or string parameter: a native node cannot
   take a negative integer constant or an operator choice. `NodeParameterType.Int32/String/BlackboardKey`
   are managed-only (`Authoring/Model/Nodes/NodeManifestContracts.cs:48-53`).
3. **One value type and one scope per binding field.** A binding attribute fixes the key's type and
   scope (`BurstNodeGenerator.cs:439-483`, `:546-555`). A node cannot be generic over `T`; a Tree-scope
   node cannot bind an Agent key. Hosts resolve Tree and Agent slots only
   (`GeneratedTreeDispatchAdapterV2.cs:1518-1538`).
4. **No log or trace API in a node.** Contexts expose time, random, blackboard, snapshot, effect and
   async calls only (`BurstNodeContracts.cs:433-563`); the analyzer forbids `UnityEngine`
   (`CodeGen~/AIBT.CodeGen/BurstNodeUsageAnalyzer.cs:700-705`).
5. **Random nodes do not run on hosts today.** `ProductionTreeHost` and `NativeTreeAgent` never seed
   random streams (`GeneratedTreeDispatchAdapterV2.cs:588-599`; only `TreeGroup.cs:508` calls
   `SeedRandomStreams`). The dispatch owner requires an odd increment
   (`Runtime/Execution/Burst/Dispatch/NativeBurstDispatchBatchOwnerV2.cs:587-590`), so the first update of
   a tree with a random node fails with `InvalidEncoding`; the test
   `Tests/Integration/Simulation/TreeSimulationHostEquivalenceTests.cs:141`
   (`Hosts_HaveZeroedRandomStreams_AndARandomNodeCannotRun`) pins this
   (`Planning~/Evidence/P10-009/README.md:85-98`). Random nodes declared non-deterministic are also
   refused by the default `Phase1` policy (`AIBT2040`, `Authoring/Validation/TreeValidator.cs:1047`).
   `aibt.stdlib.random-condition` is declared non-deterministic only because its reference twin uses
   `System.Random` (`BuiltInLeafManifests.cs:84`, `:107`); determinism-v1 counts the AIBT-owned seeded
   stream as deterministic (`determinism-v1.md:3-10`).

### 2.7 P7-028 "wall 1": the reference side has no binding support (checked)

A native node reaches keys only through `GeneratedHandle` fields bound by `[AibtBlackboardBinding]`.
The reference side cannot run such a node:

- The reference compiler builds blackboard access tables only from a manifest's fixed `Reads` /
  `Writes` key names (`Authoring/Compilation/ReferenceCompiler.cs:455-470`, `:737-760`). No file under
  `Authoring/Compilation/ReferenceCompiler.cs`, `Runtime/Execution/Reference/**` or `Agents/**` mentions
  `GeneratedHandle` (the type appears only in the generated/native path:
  `Authoring/Compilation/Generated/GeneratedBurstDispatchPrebindingV2.cs:410-456`,
  `Runtime/Execution/Burst/Dispatch/NativeBurstDispatchBindingValidationV2.cs`).
- For the native path the generated compiler hands the reference compiler a *projection* in which
  every generated leaf is replaced by a placeholder `memory-sequence` (or `inverter`) with no
  parameters and every key by an `Int32` Tree key (`Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs:570-598`);
  bindings are rebuilt afterwards from the generated descriptors (`:427-428`, `:503-506`). The
  reference executor therefore never sees a generated leaf.
- A reference leaf (`IReferenceLeafBehavior`) reads and writes keys only by *declared ordinal* of the
  manifest's `Reads`/`Writes`, and its context exposes neither time nor random
  (`Runtime/Execution/Reference/Leaves/Public/ReferenceLeafBehavior.cs:24-44`). The executor has the
  time point internally (`Runtime/Execution/Reference/Core/ReferenceExecutionContracts.cs:69-97`) but
  no random stream at all (no `Random` under `Runtime/Execution/Reference/`).

So **wall 1 still stands**: a library node that reads or writes a key, reads time or draws random
numbers cannot have a reference/managed twin today.

## 3. How a native node is added today, and what a library node reuses

1. **Declare** in the shard assembly (`AIBT.StandardNodes`): config struct with `[AibtConfigField]` /
   `[AibtParam]` and `GeneratedHandle` fields with `[AibtBlackboardBinding]` / `[AibtKey]`, optional
   memory struct, `[AibtNodeDocumentation]`, `[AibtBurstNode]` or `[AibtAction]`/`[AibtCondition]`,
   `Tick` (and optional `Enter`/`Abort`/`Exit`) calling only `<Shard>.BurstAccess`
   (`burst-node-authoring.md:21-57`).
2. **Generate** — the packaged analyzer (`AIBT.StandardNodes.asmdef` analyzers) validates the
   declaration (`AIBT5xxx`) and emits per-shard metadata: `ManifestRegistryJson`, `NodeRegistryHash`,
   `DescriptorHash` (`CodeGen~/AIBT.CodeGen/GeneratedMetadataEmitter.cs:22-49`) and the `BurstAccess`
   helpers (`BurstNodeGenerator.cs:1064-1140`).
3. **Select** — a project's `[AibtCatalogSet]` lists the shard; the generator builds the dispatch,
   the runtime layout blob and the catalog fingerprint over every listed shard's id, version and nodes
   (`BurstNodeGenerator.cs:1881-1926`), and validates the handshake (`:1436-1447`, `AIBT5012`).
4. **Discover** — the Editor finds every `[AibtCatalogShard]` in player assemblies, whether or not a
   set lists it (`Editor/Registry/ProjectNodeCatalog.cs:76-84`), so the palette
   (`Editor/Graph/NodeSearchProvider.cs:56-88`, grouped by category) and MCP `search_nodes` /
   `get_node_contract` (`MCP/McpToolDispatcher.cs:242-279`) already list `aibt.native.*`.
5. **Compile and bake** — `GeneratedCompiledProgramV2` compiles bindings and records the registry
   hash (`GeneratedCompiledProgramV2.cs:298`, `:680`); `ProductionTreeAssetBaker` refuses nodes the
   chosen set does not declare (`Editor/Graph/ProductionTreeAssetBaker.cs:49-57`) and records the
   catalog fingerprint (`Runtime/Integration/ProductionTreeAsset.cs:14-21`).
6. **Run** — bootstrap refuses a catalog whose fingerprint differs (`ProductionTreeAsset.cs:81-85`,
   `TypeMismatch`), then requires a dispatch case per generated leaf by type ID and version
   (`Runtime/Integration/GeneratedDispatch/GeneratedTreeRuntimeDefinitionV2.cs:54-74`).
7. **Test** — a test-assembly catalog set listing the shard, run on `ProductionTreeHost`
   (`StandardNativeShardTests.cs:15`, `:49-81`) and in `TreeSimulation`
   (`Tests/Integration/Simulation/SimulationTestPrograms.cs:16`); Jobs/in-place equivalence already
   includes the shard (`Tests/Integration/NativeRuntime/JobsPolicyEquivalenceTests.cs:18`).
8. **Document** — `Documentation~/generated/node-catalog.md` is generated from the managed registry
   only (`MCP/Documentation/McpNodeCatalogDocumentGenerator.cs:20`): it lists 67 types (13 core,
   2 stdlib, 52 unity) and **none of the `aibt.native.*` nodes**. `README.md:279` lists them by hand.

A library node reuses steps 1–7 unchanged: no generator, ABI, compiler, executor or host change.
Step 8 needs the doc generator to include the package's native shard (§4.6).

## 4. Design options

### 4.1 Execution path: native-only vs dual-path

| | (a) Native-only, `aibt.native.*` — recommended | (b) Dual-path, `aibt.stdlib.*` |
|---|---|---|
| Runs in | `NativeTreeAgent`, `ProductionTreeHost`, `TreeSimulation` | also `BehaviorTreeAgent` and the reference executor |
| Naming rule (P9-004) | followed | followed (needs byte-identical manifests, P7-028 discipline per node) |
| Work outside the shard | none (§3) | reference compiler resolves generated `bindings` into its read/write tables; `IReferenceLeafBehavior` context gains time and a seeded PCG random stream; `ManagedTreeProgram` binds such leaves; `NodeRegistryBuilder.AddBuiltInLeaf` accepts binding descriptors; a reference twin and a parity test per node |
| Touches | nothing on the standing list | the **reference executor** (`Runtime/Execution/Reference/**`, new capability), the **managed agent** (`Agents/Runtime/**`), and the meaning of the compiled-program v1 access tables for trees with bindings (spec text in `compiled-program-v1.md`); `time-and-random-v1.md` already requires per-node streams, so the reference side would come into line with it rather than change it |
| Benefit | the library ships in small stages now | one tree runs on both layers without rewriting nodes |
| Size | S–M per stage | L+ (comparable to P10-013's reference + native work), plus every library node twice |

Recommendation: (a). The managed side already has 52 richer nodes; what is missing is the native
side. Because (b) changes the reference executor and the managed agent, it is owner question Q1, not
the default. If the owner later wants (b), it gets its own ADR; nothing in (a) blocks it (a node could
later gain an `aibt.stdlib.*` twin).

### 4.2 Where the library lives

| Option | Effect |
|---|---|
| **(L1) Grow `aibt.native.standard` in `AIBT.StandardNodes` — recommended** | One `typeof(StandardNativeShard)` for users. Adding nodes changes the catalog fingerprint, registry fingerprint and layout blob of every catalog set that lists the shard (`BurstNodeGenerator.cs:1893-1914`), and the registry hash of programs compiled against those sets (`GeneratedCompiledProgramV2.cs:368`, `:680`). Assets baked against such a set then fail bootstrap with `TypeMismatch` until prepared again (`ProductionTreeAsset.cs:81-85`); the Editor marks them `CatalogChanged` and re-prepares them after the next domain reload (`Editor/Graph/ProductionTreeAssetStaleness.cs:20-23`, `:46-54`, `:166-195`). There is no build-time gate (no `IPreprocessBuildWithReport` under `Editor/`), so a Player built without that reload would carry stale assets. Sets that do not list the shard are untouched. In the package only tests and the Phase 10 benchmark list it (§2.4). |
| (L2) New shard(s) in new assemblies, existing shard frozen | Existing sets and assets never change; users list one more `typeof` per shard. Since a shard is an assembly, splitting by category means one asmdef per category. More to explain, no gain for the current users. |
| (L3) Importable sample | Not "shipped with the package": users copy code, copies drift, no catalog/MCP entry until imported. Rejected. |
| (L4) On by default in every catalog set | Through the frozen `aibt.core` authority: impossible — the authority is core-only and gets no dispatch cases (`BurstNodeGenerator.cs:803-810`, `burst-node-abi-v2.md:157-189`). Through the generator adding the shard to every set: changes every set's fingerprint in every project, invalidating all baked assets, and is a generator change. Through `CreateWithBuiltIns()`: needs `aibt.stdlib.*` names and reference twins — that is option 4.1(b). Rejected. |

**Shard version.** No rule requires bumping a shard's version when nodes are added; the version only
feeds the catalog fingerprint, which changes anyway (`BurstNodeGenerator.cs:1899`;
`burst-node-abi-v1.md:183-187`: "a node assembly has exactly one shard"; versions only need to be
positive). Recommendation: keep `("aibt.native.standard", 1u)`; record the node
set per release in `CHANGELOG.md`. Node versions follow the existing rule: a contract change of a node
gives it a new version (`burst-node-abi-v2.md:16`).

**Visibility stays as today**: the shard is discovered in every project (palette, MCP) and dispatched
only where a set lists it. The bake error for an unlisted node (`ProductionTreeAssetBaker.cs:49-57`)
will name the fix ("add `typeof(StandardNativeShard)` to your catalog set").

### 4.3 Naming and categories

- Type IDs `aibt.native.<name>`, version 1. Actions are `<verb>-<type>` (`set-float` style), conditions
  `<type>-<predicate>` (`float-at-least` style) — the pattern of the four existing nodes.
- Categories extend the existing `Native/Blackboard` and `Native/Flow` with `Native/Timing`,
  `Native/Random`, `Native/Signals`; the palette groups by category already.
- `[AibtNodeDocumentation]` "when not to use" names the managed counterpart, as today.

### 4.4 Generic nodes vs one node per type

One node per value type (§2.6, item 3: a binding fixes the type). Types in the first set: `Bool`,
`Int32`, `Float32`, `Float3` (the four types `NativeTreeAgent.SetFact` and the managed library use
most), plus `Int64` for deadlines. Negations and "below" use `aibt.core.inverter` over an "at least"
node (documented recipes, not extra node types). Operator-parameter comparisons and negative integer
constants wait for native `Int32`/enum parameters (§5.2).

### 4.5 World access

Library nodes are blackboard-only plus time, random and one package channel (§2.3). No library node
reads the scene, uses snapshot inputs or starts async actions (container-only, and their channel IDs
would be imposed on the game). The single effect channel is `aibt.native.signal` with an `Int32`
code: hosts deliver it (`ProductionTreeEffects.cs:6-12`), `NativeTreeAgent.OnEffect` takes
`Action<int>` (`NativeTreeAgent.cs:137`), and `TreeSimulation` reads it as `CommandChannel<int>`
(`SimulationChannels.cs:11-59`). This covers what `aibt.unity.raise-event` and, in part, `log` do on the
managed side. Fixing that channel name is owner question Q6.

### 4.6 Palette, MCP and docs

- Palette and MCP: no change needed (§3, step 4); new nodes appear under their categories.
- Node catalog: `McpNodeCatalogDocumentGenerator.PackageRegistry()` also includes the package's
  native shard, so `node-catalog.md` lists every `aibt.native.*` node with its contract.
- User guide: a "Native node library" page with the managed → native mapping table and the recipes
  (negation, shared cooldown, signals); `README.md:279` updated.

## 5. Proposed first set

All nodes: shard `aibt.native.standard`, version 1, Tree-scope bindings, `NodeMemoryLifetime.Activation`,
cost `Trivial`, deterministic. Conditions that only read keys are observer conditions
(`[AibtObserverCondition]`, as `aibt.samples.swarm.within-radius`), so they can guard memory composites.
The four existing nodes stay exactly as they are (IDs, versions, behaviour).

### 5.1 The set

**Group 1 — blackboard conditions (`Native/Blackboard`), stage 1.**

| Node | Contract | Why first |
|---|---|---|
| `int-at-least` | Success when Int32 `key` ≥ `threshold` (UInt32) | counters, ammo, states; `compare-int` |
| `int-equals` | Success when Int32 `key` = `value` (UInt32) | state codes; `compare-int` |
| `int-keys-at-least` | Success when Int32 `left` ≥ `right` | `compare-int-keys`; any sign |
| `float-keys-at-least` | Success when Float32 `left` ≥ `right` | `compare-float-keys` |
| `float3-within-distance` | Success when ‖`a` − `b`‖ ≤ `distance` (Float32); `flat` ignores Y | the one generic node every sample needed (`within-radius`, `is-within-distance` with facts) |
| `float3-is-set` | Success when Float3 `key` ≠ (0,0,0) | `is-vector3-set`; "has a target" facts |

**Group 2 — blackboard actions (`Native/Blackboard`), stage 1.**

| Node | Contract | Why first |
|---|---|---|
| `set-bool` | writes `value` to Bool `key`, Success | `set-bool` |
| `set-int` | writes `value` (UInt32 ≤ 2³¹−1, else Failure) to Int32 `key` | `set-int` |
| `set-float3` | writes (`x`,`y`,`z`) to Float3 `key` | `set-vector3` |
| `copy-bool`, `copy-int`, `copy-float`, `copy-float3` | `to` := `from` | `copy-*`; negative ints via copy |
| `increment-int` | Int32 `key` += `amount` (UInt32, wrapping) | the counter the brief and `increment-int` name |
| `increment-float` | Float32 `key` += `amount` (Float32, may be negative) | `increment-float` |
| `toggle-bool` | Bool `key` := not `key` | `toggle-bool` |
| `float3-distance` | Float32 `result` := ‖`a` − `b`‖ (`flat` ignores Y) | `distance` |

**Group 3 — flow leaves (`Native/Flow`), stage 1.** `succeed` (Success), `fail` (Failure), `idle`
(Running until aborted) — placeholders and "hold" branches; `aibt.unity.succeed|fail|idle`.

**Group 4 — timing and signals (`Native/Timing`, `Native/Signals`), stage 2.**

| Node | Contract | Why |
|---|---|---|
| `wait-until-true` | Running until Bool `key` is true → Success; `timeout-microseconds` (UInt32, 0 = none) → Failure | `wait-until`; waits on a game fact |
| `set-deadline` | Int64 `key` := now + `duration-microseconds` (UInt32), Success | `start-cooldown`; a cooldown shared by key |
| `deadline-passed` | Success when now ≥ Int64 `key` | `is-cooldown-ready` |
| `signal` | emits `code` (UInt32 ≤ 2³¹−1) on channel `aibt.native.signal` as Int32, Success | `raise-event`; the generic way to tell the game "do X" |

**Group 5 — random (`Native/Random`), stage 3, only with Q4 = yes.** Each is an `[AibtRandomStream]`
node declared deterministic (Q5).

| Node | Contract | Why |
|---|---|---|
| `random-chance` | Success with probability `chance` (Float32, 0–1) | `random-chance`; unlike `aibt.stdlib.random-condition` it passes the default policy |
| `random-float` | Float32 `key` := uniform [`min`, `max`) | `random-float` |
| `random-int` | Int32 `key` := uniform [`min`, `max`] (UInt32) | `random-int` |
| `wait-random-microseconds` | Running for a uniform duration in [`min`, `max`], then Success | `wait-random` |
| `random-float3-in-radius` | Float3 `result` := `center` + random offset with `min-radius` ≤ r ≤ `radius`; `flat` | `random-point` with a centre key |

Totals: 20 nodes in stage 1, 4 in stage 2, 5 in stage 3 — 33 with the existing four.

### 5.2 Left for later, with the reason

| Candidate | Why not now |
|---|---|
| Agent-scope (and Shared read) variants of the blackboard nodes | a binding fixes the scope (§2.6.3), so each variant is a new node type; wait for a tree that needs one |
| Operator-parameter compares (`compare-int` with `less-than`, …), negative constants | native parameters have no `Int32` or enum (§2.6.2); adding them changes the generator and the node contract — a separate decision |
| `time-since-changed` | needs a per-key change time in the node context: an ABI v2 addition |
| New decorators: retry / until-success, force-running, limit | not possible as leaves (§2.6.1); each is a new `aibt.core.*` type in both executors and needs its own ADR (AIBT-048) |
| Seek / flee / move-until-distance (`steer`, `first-agent.move`) | world-bound; each imposes effect channel names; one sample each — fails the P7-036 two-tree rule |
| Animation, audio, navigation, physics, GameObject nodes | the native layer never touches the scene (`README.md:15`); the game does this in effect handlers |
| Debug log with text | no log API in node contexts, `UnityEngine` forbidden (§2.6.4); trace covers debugging |
| Snapshot-input and async-action library nodes | container-only (§2.3), and the channel IDs would be imposed on the game |
| `Float2`, `Quaternion`, `Float64`, fixed strings, `EntityId` | no demand yet; one node per type each |
| A dual-path `aibt.stdlib.*` twin of any node | option 4.1(b), Q1 |

## 6. Effect on the standing constraints

| Constraint | Effect |
|---|---|
| Execution semantics | None. Leaves only; no new `aibt.core.*` type; the lifecycle machine is untouched. |
| ABI v2 (`ExpectedPublicAbiV2.txt`) | None. Library nodes use existing attributes and contexts; the contract test file does not change. |
| Compiled program format | None. |
| Trace format | None. |
| Package version | None. |
| Normative specifications | None in stages 1–3. |
| Existing low-level API signatures | Unchanged. Additions only: new public node structs in `AIBT.StandardNodes` (the public API baseline grows); with Q4 one optional seed setting on hosts (additive). |
| `ProductionTreeHost` / `NativeTreeAgent` / `ProductionTreeScheduler` behaviour | Unchanged in stages 1–2. **Stage 3 changes it**: hosts would seed the random streams of nodes that declare one (§2.6.5). Today such a tree fails on its first update; after the change it runs. Trees without random nodes are not affected (no stream exists, `GeneratedTreeDispatchAdapterV2.cs:906-907`). The pin test `Hosts_HaveZeroedRandomStreams_AndARandomNodeCannotRun` is replaced. Hence Q4; **alternative that touches nothing**: leave group 5 out (random stays with `aibt.stdlib.random-condition` in `TreeSimulation` only). |
| Existing nodes and assets | The four `aibt.native.*` nodes keep IDs, versions and behaviour. Projects whose catalog set lists `StandardNativeShard` get a new catalog fingerprint at each stage, and their prepared assets are re-prepared once (automatic in the Editor; §4.2, Q2). |
| P9-004 naming rule | Kept (`aibt.native.*`). |
| P7-036 promotion rule | Kept for sample nodes; the first set is justified by managed counterparts instead (Q3). |

## 7. Implementation plan

Each stage has its own commits and evidence folder `Planning~/Evidence/P11-002/`. Tests are native
(the nodes have no reference twin, §4.1); "both executors" is replaced by "both native hosts":
`ProductionTreeHost`/`NativeTreeAgent` and `TreeSimulation`, in place and with Jobs.

### Stage 1 — blackboard and flow nodes (M)

Deliverables: the 20 nodes of groups 1–3 in `AIBT.StandardNodes`; node catalog generator includes
the package native shard; regenerated `Documentation~/generated/`; README table; the bake error names
the shard; updated exact-set tests (`StandardNativeShardTests.cs:86-91`,
`Tests/Editor/Mcp/Discovery/McpToolDispatcherTests.cs:74-85`).

Acceptance and tests:
- Generator accepts the shard (`IsUsable`) in a test catalog set next to a project shard; no new
  `AIBT5xxx` diagnostics.
- One behaviour test per node on `ProductionTreeHost` (Success/Failure/Running, written values),
  including edge cases (`set-int` above 2³¹−1 → Failure, `increment-int` wrap, `flat` distance, zero
  vector).
- The same trees in a `TreeSimulation` group, in place and with `UseJobs`: identical per-tick digests
  (pattern of `JobsPolicyEquivalenceTests`).
- **Managed/native parity**: for each node with a managed counterpart, a tree written with
  `aibt.unity.*` on `BehaviorTreeAgent` and the same tree with `aibt.native.*` give the same
  blackboard values and root results over a scripted fact sequence (floats within a stated tolerance,
  `determinism-v1.md:12`).
- Observer conditions guard a memory composite (abort on key change) natively.
- ABI contract test unchanged; public API diff additive only.
- Visual: palette screenshot with the `Native/Blackboard` and `Native/Flow` groups
  (`Save-UnityWindow.ps1 -Window 'AIBT Graph'`).

### Stage 2 — timing and signals (S–M)

Deliverables: group 4; a demo tree "guard" built only from package nodes (core + `aibt.native.*`)
running on `NativeTreeAgent` with facts and the `aibt.native.signal` handler; user guide page
"Native node library" (mapping table, recipes).

Acceptance and tests: deadline nodes against a controlled host clock and `TickMicroseconds`;
`wait-until-true` with and without timeout, aborted while waiting; `signal` delivered once per
emission through `NativeTreeAgent.OnEffect` and as `CommandChannel<int>` in `TreeSimulation`; codes
above 2³¹−1 fail. Visual: screenshot of the demo graph with live highlight and a short Play Mode clip
(`Save-UnityClip.ps1`).

### Stage 3 — random (M), only with Q4 = yes

Deliverables: group 5; hosts seed streams per `time-and-random-v1.md` (root seed from a new optional
host setting, the program semantic hash, the host's tree instance ID, node index), the same derivation
`TreeGroup` uses (`GeneratedTreeDispatchAdapterV2.cs:594-599`).

Acceptance and tests: stream words on hosts equal `NativeRandomStreamDerivationV1.TryDerive`; the
P10-009 pin test replaced by "a random node runs on a host"; trees without random nodes give
byte-identical behaviour and digests before and after; distributions (bounds, inclusive/exclusive
ends) over many draws; same seed → same sequence, other seed → different; Jobs equals in place.
Visual: the demo tree with random patrol points, clip.

### Stage 4 — documentation pass (S)

Deliverables: CHANGELOG entry per stage with the node list and the re-prepare note; `consumer-quickstart.md`
mention; MCP recipes for building a native tree from library nodes.

### Risks and mitigations

| Risk | Mitigation |
|---|---|
| Stale prepared assets shipped in a Player after a package update | Editor re-prepares on reload; CHANGELOG names it; the bake error names the shard. A build-time check is a separate idea, not in scope. |
| Float distance/compare results differ slightly between managed and native | parity tests use a stated tolerance; native-native determinism is unaffected |
| Library grows into world-bound nodes by habit | §4.5 rule: blackboard, time, random, `aibt.native.signal` only; anything else needs an owner decision |
| Two random-chance nodes (`aibt.stdlib.random-condition`, `aibt.native.random-chance`) confuse authors | each node's "when not to use" points to the other; the guide explains why |

## 8. Open questions for the owner

1. **Native-only library.** The new nodes run on `NativeTreeAgent`, `ProductionTreeHost` and
   `TreeSimulation`, not in `BehaviorTreeAgent` (which already has 52 managed nodes).
   *Recommendation: yes.* Other answer: dual-path nodes need reference-side binding, time and random
   support in the reference executor and the managed agent (§4.1), a separate L+ ADR, and every node
   written twice.
2. **Grow the existing shard `aibt.native.standard`** rather than add new shards. Projects whose
   catalog set lists it get their prepared assets re-prepared once per stage (automatic in the Editor).
   *Recommendation: yes.* Other answer: a new shard assembly; nothing existing changes, users list one
   more type.
3. **Evidence for the first set.** Nodes are chosen because a managed counterpart already ships, not
   by the P7-036 rule "two independent trees use it" (which stays for promoting sample nodes).
   *Recommendation: yes.* Other answer: no node qualifies today (the best candidate, Swarm Arena's
   `within-radius`, is used three times in one tree), so the library stays at four nodes until
   projects show reuse.
4. **Random nodes on hosts.** Let `ProductionTreeHost`/`NativeTreeAgent` seed random streams (today a
   tree with a random node fails on its first update). Changes host behaviour only for such trees.
   *Recommendation: yes, as stage 3.* Other answer: no random group; random stays in `TreeSimulation`.
5. **Random library nodes count as deterministic** (AIBT-owned seeded stream, `determinism-v1.md`), so
   the default `Phase1` policy accepts them. *Recommendation: yes.* Other answer: they are refused by
   default (`AIBT2040`), as `aibt.stdlib.random-condition` is.
6. **One package effect channel `aibt.native.signal` with an Int32 code** for "tell the game to do X".
   *Recommendation: yes.* Other answer: no signal node; every project writes its own effect node.

## Owner decisions (2026-11-19)

The owner accepted every recommendation of §8:

1. The library is native-only (`aibt.native.*`); dual-path `aibt.stdlib.*` twins are not part of
   this decision.
2. The existing shard `aibt.native.standard` grows; projects whose catalog set lists it re-prepare
   their baked assets once per stage.
3. The first set is justified by an existing managed counterpart; the P7-036 two-tree rule stays for
   promoting sample nodes.
4. `ProductionTreeHost` and `NativeTreeAgent` may seed the random streams of nodes that declare one,
   as stage 3. This is the one permitted change of host behaviour; trees without random nodes must
   behave byte-identically, and the pin test
   `Hosts_HaveZeroedRandomStreams_AndARandomNodeCannotRun` is replaced in that stage.
5. Random library nodes are declared deterministic.
6. One package effect channel `aibt.native.signal` with an `Int32` code.
7. **Host fix found in stage 1 (allowed by the owner on 2026-11-19).**
   `GeneratedTreeDispatchAdapterV2.DispatchSlot.TryCreate`
   (`Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:840`, since P7-037) read
   the configuration field of a generated blackboard handle as `ConfigurationFields[ordinal]`, using
   the binding's case-local field ordinal as an index into the catalog's global field array. A handle
   that is not the node's first configuration field therefore read its access ordinal at another
   field's offset. That broke 10 of the 20 stage-1 nodes on `ProductionTreeHost`, `NativeTreeAgent`
   and `TreeSimulation`: a handle after a parameter failed bootstrap, and a second handle faulted the
   first update. The fix indexes `FirstConfigurationField + ordinal`, as every other site does. This
   is the one permitted change of host code in stage 1. No tree in the package read a wrong key
   silently. Every existing node whose handle is not field 0 read the right ordinal by coincidence,
   and no existing test changes its result.

Stage 1 of §7 was approved on 2026-11-19 and accepted the same day on `4af5571`. Its line "observer
conditions guard a memory composite natively" is met through a reactive composite at the next update:
native hosts do not run observer re-evaluation (existing behaviour, see
`Planning~/Evidence/P11-002/README.md`). Stages 2–4 are not started without the owner's go-ahead.

## 9. Noticed while reading (not part of this feature)

- The Swarm Arena comment that the ABI accepts only `UInt32` scalars is stale; `Float32` parameters
  exist (`Samples~/SwarmArena/Runtime/Nodes/SwarmArenaNodes.cs:19-20`; `burst-node-abi-v2.md:3-16`).
- The Public Burst Nodes sample binds a `UInt32` blackboard key
  (`Samples~/BurstNodes/Runtime/PublicBurstNodeSample.cs:11`), but `UInt32` is not a blackboard type
  (`blackboard-v1.md:5-13`). Not verified whether a real tree can bind it.
- Six managed nodes have no test at all (`compare-int`, `set-int`, `fail`, `idle`, `look-at`,
  `is-within-distance`); eight more are used only incidentally in other tests.
- `aibt.stdlib.random-condition`'s reference twin uses an unseeded `System.Random`
  (`BuiltInLeafManifests.cs:107`), not a seeded stream as `time-and-random-v1.md` describes.
