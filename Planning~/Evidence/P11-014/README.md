# P11-014 evidence — Finishing batch

Branch `feature/aibt-consumer-workflow`, base `824a378`. Raw files: `C:\UnityProjects\_aibt_acceptance\P11-014\raw\`
(running notes in `..\progress-notes.md`). Unity 6000.6.9f1, the Modules Editor (Pipeline port 7810). The
machine is loaded; no conclusion is drawn from timings. Only the touched test classes were run, not the
ten assemblies (one deviation, below). Status: `in_progress`, waiting for the orchestrator's acceptance run.

## Commits

| Commit | Item | Content |
| --- | --- | --- |
| `8cde249` | 10 | fix(scheduler): a pipelined group with no eligible host is not a failure |
| `109b6c3` | 11 | test(agent-memory): missing and unfailable P11-013 tests |
| `b7494b3` | 12 | docs(random): the combine function's description; which bootstraps take an id |
| `fe95c67` | 13 | docs(agent-memory): failed starts, a host stopped by user code, handle-like values, `RunIndex` 0 |
| `c3d765f` | 14 | fix(inspector): the random-stream hint follows an asset prepared again in place |
| `2584046` | 15 | test(agent-memory): pin an Agent range at an odd offset (record) |
| `108c56c` | 1-3 | fix(validation): one observer rule for MCP, the tree list, the graph and the bake |
| `dcee3a9` | 4 (W10) | feat(mcp): `create_tree` writes format 2 unless `formatVersion` 1 is given |
| `55097bf` | 6 (W6) | feat(host): `ProductionTreeHost.IsExecuting` is public |
| `7501ae1` | 5 (W3) | fix(agents): a failed subtree import reports its own errors before AIBT3020 |
| `052f445` | 7, U6 | fix(host): the value-session refusal names the node and the rule; fixture shard; U6 |
| `33eb750` | 8 | docs(comments): two wrong comments |
| `da428af` | 9 | test(native-library): aliased copy and compare cases that see a lost write |
| `72aeec6` | U3, U4, U10 | test(confirmations): the three hold; tests kept |
| `f37f44e` | C | docs: N1, N5, N8, Known limitations |
| `3391ea3` | U9 | fix(tree-list): read registered blackboard types |
| `3d790d9` | CHANGELOG | P11-014 section; the P11-008 sentence corrected |
| this commit | form | task card, work item, MASTER_PLAN, this evidence |

## Per item: what changes for a user, and the proof

**1-3. One observer rule.** MCP `validate` reports `AIBT2051` (error, `/nodes/guard/observer`) and `valid:
false` for a managed tree with an observer, and `compile` no longer reports success; before, both passed
(`raw/items123-before.txt`: `valid: true`, `success: true`). The bake warns `AIBT2054` for an observer inside
a subtree (before: no warning). A tree of built-in nodes only gets `AIBT2054` whose text says what each host
does (BehaviorTreeAgent refuses with AIBT2051, a native host ignores the block). After:
`NativeObserverWarningTests` 10/10, `ManagedObserverRefusalTests` 3/3.

**4 (W10). `create_tree` defaults to format 2.** Before: "the default Expected: 2 But was: 1"
(`raw/item4-before.txt`). The recipes generator text and `generated/recipes.md` follow. After:
`McpAuthoringToolDispatcherTests` 20/20, `McpDocumentationGeneratorsTests` 14/14.

**5 (W3). A failed import reports its own errors first.** Before: only the two `AIBT3020` lines ("no
subtree resolver") (`raw/item5-before.txt`); after, the import's `AIBT3023` comes first.
`SubtreeManagedTests` 5/5, `SubtreeImportAndBakeTests` 6/6.

**6 (W6). `ProductionTreeHost.IsExecuting` is public** (doc comment; generated API reference). Test: true
while a `PipelinedJobs` round is in flight. 1/1.

**7. The value-session limit.** Kept, as decided. A node that reads one Read binding twice in one callback
under a catalog whose largest node has one binding stops the host with `NativeLifetimeStateInvalid`
(unchanged). The text was only `Callback rejected: CapacityExceeded` (`raw/item7-before-and-integration-assembly.txt`);
it is now:

> AIBT ProductionTreeHost: NativeLifetimeStateInvalid: Callback rejected: CapacityExceeded by node
> 'reader'. The usual cause is the value-session limit: the catalog sizes one callback's value sessions for
> its largest node (one per Read or Write binding, two per ReadWrite binding), so a node that reads or
> writes one binding more often than that in one callback is refused. Read a value once into a local and
> reuse it (burst-node-authoring.md, 'Read a binding once per callback').

The node is named on both main-thread `CallbackRejected` sites (`CompletePendingDispatch`, the dispatch
loop), which are also the batched paths (U6's message names the node too). The agent-frame path does not
record the node index (`AgentFrameCoreV1`); there the text says "a native node (not known on this path)".
No Burst ABI or generated code changed. Rule written in `burst-node-authoring.md` ("Read a binding once per
callback": the local, the symptom, why a larger node hides it). Fixture: `Tests/Editor/CodeGen/GenerationP11014`
(`read-twice`, `read-once`, own assembly). Tests: `ValueSessionLimit_*` 2/2 (the message and code; a catalog
that also holds `mixed-report`, four bindings, hides the fault and the tree succeeds).

**8. Two comments.** The adapter's alias comment now says that a read after a write of the same target in
one callback is served the staged write (`TryAllocateBlackboardReader`). The compiler's comment no longer
says the host aligns the Agent base: the host puts the Agent range right after the Tree range
(`TreeValueByteCount`), the projection aligns it. Comments only.

**9. Aliased-key tests that see a lost write.** Six cases (`Host_AfterAWrite_*`): an earlier node raises,
then drops, the key in the same run, then the aliased node runs. A self-copy that read or wrote a stale
value undoes the raise or drop; a self-compare whose left handle saw the value from before the raise, or
whose right handle saw it from before the drop, fails; a lost write leaves another final value. A self-copy's
own write still cannot be observed (it stores the value the key already holds, and an unchanged write does
not bump the version, `CanCommitTreeValues`); the cases check what it must not undo. `NativeLibraryAliasedKeyTests`
20/20.

**10. A finished host under forced `PipelinedJobs`.** Before (`raw/item10-before.txt`): `NativeCapacityPlanInvalid`,
`LastError` null, 30/30 frame entries Failed, `TryGetValue` false, Agent memory 40 (default) after
`Restart()` instead of 41. Now such a group is recorded as executed with no step. The same-frame batched path
(an empty open list runs 0 steps and is Executed) and the agent-frame path (a host not included is skipped)
do not have the defect. On Swarm Arena's 1/32: not explained by this defect (agents there restart
automatically, so a finished host is replaced before its next due frame); the rate entry in Known
limitations gives the measured mechanism. After: MixedScope 40/40 (skip removed), Scheduler 25/25,
JobsPolicyEquivalence 28/28, SchedulingProfile 18/18, NativeTreeAgent 16/16, NativeAutoSelection 25/25,
WorldEffect 6/6.

**11. Missing and unfailable tests.** (a) same Agent contract id and version with another layout starts from
the defaults (an extra key; a Float32 key of the same size, caught only by the hashes: a mutation without
them fails 0/7, `raw/item11a-mutation.txt`). (b) `forced-agent-frame` now runs the job
(`AgentFrameMinimumBatchSize` 1); the seven kinds are four mechanisms: in place on the main thread
(Immediate, Budgeted), same-frame batched job, pipelined job, agent-frame job. (c) bootstrap failure keeps
the run index and the memory; a `RandomStreamId` change applies next run; Agent-only tree; several sizes;
`Restart(true)` and a Tree change while stopped. (d) restart allocations, below. (e) constant arithmetic
removed. MixedScope 76/76, RandomStreamSeeding 12/12, RandomSeed 21/21.

**11d. Restart allocations (recorded, no code change).** A warmed `Restart()` makes **73 GC.Alloc samples
on average (1460 over 20)**; the capture plus restore of the Agent range alone makes **0** (GC.Alloc
recorder on the main thread, `raw/item11d-restart-allocations.txt`). A restart builds a new host.

**12. The combine function.** Spec, doc comment and a dated ADR-P11-008 amendment: injective for one id
except that the run index whose mix is 0 shares its value with one other; collisions across ids exist and
are not adjacent. A bootstrap refused before the counter (already bootstrapped, disposed, driving) takes no
id; failures after it (catalog, capacity, agents, trace) consume one. A test pins the collisions.

**13. ADR-P11-013 and guide text.** A failed bootstrap leaves the memory; a refused world registration
re-captures it unchanged; `agent.Host.Stop()` then `Restart()` gives the defaults; handle-like values are
copied as bytes; `RunIndex` is 0 before any run. Test: refused world keeps 42; `Host.Stop()` + `Restart()`
gives 40.

**14. Inspector hint cache.** Keyed also by the payload hash (new internal `ProductionTreeAsset.PayloadHash`).
Before: "Expected: True But was: False" (`raw/item14-before.txt`); after 2/2.

**15. Unaligned Agent read (record).** Native nodes read Agent slots byte-wise (`BurstBindingBridgeCoreV2`
`TryRead` composes bytes), at the same offsets since P11-009: nothing new for them. The host's typed read
(`TryReadExternalTreeValue`, `ReinterpretLoad<T>` at `_agentBaseOffset + slot.Offset`) loads at the unpadded
Tree end; a test pins a tree whose Agent range starts at offset 1 (Int32 and Float64 at odd addresses),
green on x64 in all seven kinds. Platforms: x64 and ARM64 (Windows, macOS, iOS, Android arm64) tolerate
unaligned ordinary loads; 32-bit ARMv7 Android can fault on 8-byte `LDRD`/`VLDR` at addresses not
4-aligned; WebGL (wasm) tolerates them. Not verified on a device. The `default` seed literal: CHANGELOG.

## Confirmations (section B)

| Item | Test | Outcome | Action |
| --- | --- | --- | --- |
| U3 | `NativeLibraryBurstParityTests` (replaced, see "Review follow-up") | `random-float3-in-radius` under `BatchedJobsSameFrame`, one agent, seed and stream id fixed, 240 frames: **240 points flat and 240 in 3D, 0 differ bit for bit** between Burst compilation off and on. **Corrected at the review follow-up:** under `BatchedJobsSameFrame` node bodies run managed whatever the Burst setting, so both runs were managed; the test showed reproducibility, not Burst-versus-managed parity | test replaced by `BurstExecutionPathTests`; claim now verified on the agent-frame job |
| U4 | `MixedScopeNativeTreeTests.U4_*` | `PipelinedJobs` agent, looping tree, 100 frames: `TryGetValue` succeeded **5/100 before** the scheduler's Update and **4/100 after** it (LateUpdate); the host was executing after 96/100 updates | above zero: `production-host.md` "mostly unreadable" holds; shares recorded here; test pinned 1..20, now the relations (review follow-up item 4) |
| U6 | `MixedScopeNativeTreeTests.U6_*` | two agents on one catalog under forced `BatchedJobsSameFrame`: only the over-reading agent stops (`NativeLifetimeStateInvalid`); the neighbour completed **12 runs in 12 frames**, world failure None | one stops: test kept, no doc change |
| U9 | `TreeCatalogRegisteredTypeTests` | before: the tree list showed **"Errors"** (`AIBT1008: Registered blackboard type 'aibt.tests.p10010.sense' requires an explicit accepted catalog`) for a tree with a registered-type key (`raw/u9-before-u3-u4.txt`) | fixed: `TreeCatalogEntry.ReadAll` takes the catalog's registered types and passes them to the parse, the expansion and the validation; the graph window passes its catalog's; after 1/1 |
| U10 | `BehaviorTreeAgentComponentTests.U10_*` | `Restart(true)` on an idle root after one tick, and `Stop()` then `Restart(true)`: **no fault**, blackboard back to 3, running again | test kept, no fix |

U3 limits (corrected at the review follow-up): the second run set `EnableBurstCompilation` and
`EnableBurstCompileSynchronously`, but the node bodies did not run Burst-compiled in either run. Burst patches
direct calls only inside types that carry `[BurstCompile]` (Burst 1.8.29, `ILPostProcessing.cs:169`), and the
generated catalog facade does not, so `BatchedJobsSameFrame` calls the bodies managed. What U3 established:
the same seed and stream id give the same 240 points in two managed runs. What it did not: Burst-versus-managed
parity. The replacement is under "Review follow-up", item 1. U9: the parse failed
before any expansion, so the test uses a tree without references; the fix also passes the types to
`ProjectTreeIndex.Expand` (the review's `TreeCatalogEntry.cs:61`), which no test exercises with a subtree.
U4: "before" stands for a script whose Update runs before the scheduler's; EditMode drives the scheduler
by reflection, so this is a model of the frame order, not a Player measurement.

## Section C (documentation)

N1 in `native-node-library.md` ("Guarding a branch"); N5 in `subtrees.md` ("Limits", `AIBT2034`); N8 in
`Samples~/NativeGuard/README.md` (points to the table in "What a restart keeps"); new
`Documentation~/known-limitations.md`, linked from the README, with W1, W2, W4, W13, W15, the F6 remainder,
Shared bindings, Tree-only library nodes, observers, the binding-once rule and the `PipelinedJobs` update
rate (one logical update per lifecycle steps of the slowest tree + 1 frames: 3 for one waiting leaf, about
32 for Swarm Arena's tree). The two sentences the orchestrator reserved for a later item
(`performance.md:111-113`, `execution-and-scheduling.md:19`) are untouched and not linked.

## Section D: record of every second-tier finding

| Finding | Class | Reason / what would make it urgent | Checked against the code |
| --- | --- | --- | --- |
| W3 | fixed | item 5 | test |
| W6 | fixed | item 6 | test |
| W10 | fixed | item 4 | test |
| U3, U4, U6, U9, U10 | confirmed | section B | tests |
| N1, N5, N8, session limit, Known limitations | documented | sections A7 and C | — |
| W1 | deferred | urgent when projects nest large subtrees or reuse one many times (import time, error floods) | `SubtreeExpander` has no size cap |
| W2 | deferred | urgent when a game uses world coordinates beyond about 1e19 | `StandardNativeMath.SquaredDistance` squares in Float32 |
| W4 | deferred | urgent when users debug errors inside subtrees often | expanded IDs `r:n` in diagnostics |
| W7 | deferred | urgent if the legacy group executor becomes reachable (it is not: see N7) | `TryCommitFromGroup` copies before `CommitTreeValues` |
| W8 | deferred | urgent if extract-on-save collisions are reported | `BehaviorTreeGraphWindow.Subtrees.cs` writes pending files first |
| W9 | deferred | urgent when MCP clients send wrong-typed arguments in practice | `Value<long>()`, `Value<bool>()` in `McpAuthoringToolDispatcher.Subtrees.cs` |
| W11 | deferred | urgent when imports of many subtree users get slow | `BehaviorTreeImporter.cs:66-67` `Scan()` + `Discover()` per tree |
| W13 | deferred | urgent when game code must read a pipelined agent every frame (U4: 4-5 %) | `CanReadTreeValues` |
| W15 | deferred | urgent when AI authoring relies on `get_node_contract` for bindings | `GeneratedMetadataEmitter.cs:171` writes empty `reads`/`writes` |
| N6 | deferred | urgent if two windows on one file lose edits (`WriteIfUnchanged` prevents a silent overwrite) | `BehaviorTreeGraphWindow.Subtrees.cs:513-515` |
| U1 | deferred | urgent when native hot reload gets a runtime caller | `NativeHotReloadStateMigration.TryMigrate` is called from tests only |
| U2 | deferred | urgent when the graph shows `Enum32` keys of native trees | `TryFindBuiltInDescriptor` compares type id, version and size only |
| F6 remainder | deferred | urgent when users keep tree copies in `~` folders | `TreeSourcePaths.FindFiles` scans every folder |
| Session reuse in the bridge | deferred | urgent when the binding-once rule blocks real nodes (item 7 keeps the limit) | `BurstBindingBridgeCoreV2` allocates one session per call |
| `NativeCompiledProgramV2Verifier` on Agent bindings | deferred | urgent if the `NativeProgramImageOwner` V2 path is used by a host | only `NativeProgramImageOwner.cs:670` calls it; the production host does not |
| W12 | dropped | the single-document rules run anyway: `AIBT2047`-`2050` are emitted by `TreeValidator` (`:1034-1051`), which `validate` runs on the unexpanded document | read |
| N2 | dropped | the host faults on a non-monotonic clock and says so: "Clock must return nonnegative, nondecreasing microseconds." (`ProductionTreeAgentCore.cs:668`) | read |
| N3 | dropped | documented: `subtrees.md` "Limits" (`blackboardNaming` applies to the part after the last `:`) | read |
| N4 | dropped | documented: `DirectorySubtreeResolver` "indexed by tree ID on the first lookup"; unreadable files "named in a not-found diagnostic" (doc comment, API reference) | read |
| N7, U5 | dropped | the legacy executor is unreachable from the scheduler: the scheduler passes a null workspace only for a one-host group (`ProductionTreeScheduler.cs:612-625`), and `ProductionDispatchResolverV1` sends a one-member group to `ResolveSingle` | read |
| U7 | dropped | integers are accepted for Float32: `CanonicalTreeJson.FiniteFloat` takes `Integer` tokens (`:908`, the brief's `:909`) | read |
| U8 | dropped | covered by `SubtreePlayModeTests`: `aibt.unity.increment-int` (a `BbKey` parameter) inside a subtree writes `a:count`, read back at run time | read |

No "dropped" or "deferred" claim was found wrong.

## Side observations, open

- A world without a scheduler refuses registration with `SchedulerRegistrationError.AlreadyRegisteredWithThisScheduler`,
  a misleading reason (`ProductionTreeWorld.cs:73-76`).
- `NativeTreeAgent.Restart()` keeps the previous `LastError` (its `Restart` does not clear it; pinned as
  unchanged behaviour in `AFaultedRun_ResetsAgentKeys`).
- From the independent review (recorded, no code change): under `PipelinedJobs`, a group whose members all
  failed inside `TryBeginBatchedRound` takes the `started && driver == null` branch
  (`ProductionTreeScheduler.cs:682-710`), so those members are recorded `Executed` while their `LastFailure`
  is set. The same-frame path does the same (`ProductionBatchedGroupDriverV1.cs:84-87`); `AddDirectEntry`
  (`:1254-1255`) differs.
- From the independent review (recorded, no code change): a finished host left registered under forced
  `PipelinedJobs` goes through group formation and `TryStart` on every due frame (`:664`, `:675`),
  allocations included.

## Review follow-up (2026-11-24)

Brief: `C:\UnityProjects\_aibt_acceptance\P11-014\brief-review-fixes.md`; review:
`C:\UnityProjects\_aibt_acceptance\P11-015\acceptance\independent-review-824a378-87ef22a.md`. Base `87ef22a`.
Raw: `C:\UnityProjects\_aibt_acceptance\P11-014\raw\review-followup\`. Items 5 and 6 concern P11-015 and are in
its evidence. Only the touched test classes were run.

| Commit | Item | Content |
| --- | --- | --- |
| `407cf1f` | 1 | the U3 parity test replaced by a probe of where node bodies run |
| `77966db` | 2 | `CapacityExceeded` names the node and the cause at each site |
| `8bd2d51` | 3 | registered blackboard types in subtree files and at import; W3 message order |
| `2e70915` | 3 | fix-up: `api-reference-editor.md` regenerated (the importer's summary changed) |
| `d433a1a` | 4 | tests that could not fail or asserted less than the evidence |

**1. U3 replaced (finding: the parity test compared two managed runs).** `BurstExecutionPathTests` (new; fixture
shard `BurstProbeFixtureShard` with a node that adds 1 when its body runs managed and 1000 under Burst, through a
`[BurstDiscard]` marker). `EveryPath_ReportsFromInsideTheNodeBody_WhetherItRunsBurstCompiled` pins this table
(Editor, Burst enabled, raw `item1-burst-paths.txt`):

| Path | Node bodies | Notes |
| --- | --- | --- |
| `JobsAgentFrame`, the job (batch at or above the minimum; the test sets the minimum to 1) | **Burst** | Burst compiles asynchronously: the first Burst frame was 618 after a domain reload (31.5 s); an earlier run did not reach it within 8 s |
| `JobsAgentFrame` in place (below the default minimum batch of 64) | managed | |
| `Immediate` | managed | |
| `BatchedJobsSameFrame`, 1 agent | managed | |
| `BatchedJobsSameFrame`, 4 agents | managed | |
| `PipelinedJobs`, 1 agent | managed | |
| `PipelinedJobs`, 4 agents | managed | |
| `CatalogJob` (`Executor.Schedule`) | not probed | reachable only through `GeneratedTreeDispatchAdapterV2.DispatchScheduledForTests` |
| Simulation `TreeGroup` (agent-frame job) | not probed | |

The two parity tests `RandomFloat3InRadius_Flat_IsBitIdentical_BurstCompiledAndManaged` and `_3D_` warm the
agent-frame job up until its bodies run under Burst, then record 240 frames there and 240 under `Immediate`
(managed) with the same seed and stream id: **240 distinct points, 0 differ bit for bit**, flat and 3D. 3/3.
`NativeLibraryBurstParityTests` was deleted. Finding for the docs (not changed here): `README.md:14, 19, 21, 334`
and `performance.md:79` promise Burst execution of the native layer; under the default `Immediate` policy and
under `BatchedJobsSameFrame` and `PipelinedJobs` the node bodies run managed. `performance.md:198`
(`JobsAgentFrame`) is right.

**2. `CapacityExceeded` blamed the value-session limit at every site.** The message now reads
`Callback rejected: CapacityExceeded at node '<id>'.` followed by the cause the site knows: the effect queue or
the simulation command quota (reserved before the callback; the node was not called), the capture of the
published effects or commands (after the callback), the group's batched dispatch (every member stops; the
group does not record which), or, inside a callback, the list of its limits (value sessions and staging bytes,
command, command payload and operation capacity) with the "read once" advice. The agent-frame job records the
rejecting node and site. `CapacityExceededMessageTests.AFullEffectQueue_IsNamedAsTheCause_NotTheValueSessionLimit`
(an earlier shape of it failed before with the value-session text; its final shape ran after the fix only) 1/1;
`MixedScopeNativeTreeTests.SessionLimit` with the new
`ValueSessionLimit_OnTheAgentFrameJob_TheMessageNamesTheNode` 4/4. `burst-node-authoring.md` quotes the new text.
Item 7's quote above is the text before this change.

**3. Registered types in subtree files and at import (finding: U9's fix stopped at the tree list).**
`ProjectTreeIndex.Scan` reads files with the project catalog's registered types (cached per types); the
importer (version 4) parses, expands and inspects with them when the tree needs the project catalog, and depends
on the registry hash including the registered types; `ProductionTreeAssetBaker.CurrentSourceHash` parses with
them. W3: `BehaviorTreeSource` puts the import's message first for "Needs registry" as for "Errors". Tests:
`TreeCatalogRegisteredTypeTests.ProjectTreeIndex_ASubtreeWithARegisteredTypeKey_IsFound_AndItsParentIsNotListedAsErrors`
(before: not found), `ImporterRegisteredTypeAndStatusTests` 3/3 (new; the importer hook did not exist before, so
the two registered-type cases were written with the fix; the W3 case failed before). Regression: TreeCatalogRegisteredType 2/2,
SubtreeImportAndBake 6/6, SubtreeManaged 5/5, TreeCatalogNativeStatus 5/5, TreeListScan 2/2,
ProjectTreeIndexPackage 4/4, ProjectTreeIndexReload 2/2, ProductionTreeAssetStaleness 6/6, McpSubtreeTools 29/29,
ProductionTreeAssetInspectorState 1/1, SubtreeGraphEditor 13/13, McpDocumentationGenerators 14/14 (after the
fix-up). Observation: AIBT2021 does not name the unknown node type.

**4. Weak tests.** `NativeLibraryAliasedKeyTests`: copy-bool now writes true then false (start false), with a
check after the first write, so a lost or undone second write changes the end; in the other cases the final
value differs from the start and the first write. 20/20. `MixedScopeNativeTreeTests`: a new kind
`forced-agent-frame-in-place` (default minimum batch, asserted) restores the in-place path the class lost when
`forced-agent-frame` moved to the job (`JobsPolicyEquivalenceTests` still covered in-place for other trees);
U4 pins readable exactly when the host is not executing, `after == frames - executing`, `before` from `after`,
and `after` in 1..34 instead of 1..20 (measured 5/100 and 4/100 again, 96 executing). 94/94. The P11-015 tests of
this item are in that evidence.

Not verified: a Player (IL2CPP) for any of the four; the `CatalogJob` and simulation paths of the table.
The docs' Burst promise was corrected later (README, `performance.md`): text only.

**Open finding for the owner: node bodies off the agent-frame job run managed.** Not changed. Why: the
generated catalog facade (`CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs`, `EmitFacade`, around lines 1413-1476) is
a `public static partial class` without `[BurstCompile]`. Its `ExecuteImmediate` carries `[BurstCompile]`, but
Burst 1.8 redirects a direct call to a compiled method only when the declaring type carries the attribute too
(`ILPostProcessing.cs:169`), and the runtime calls it through `IGeneratedBurstCatalogExecutorV2.ExecuteImmediate`
(`GeneratedTreeDispatchAdapterV2.cs:1348` for `Immediate`, `GeneratedDispatchGroupExecutorV2.cs:219` and
`GeneratedDispatchGroupWorkspaceV2.cs:220` for the batched and pipelined groups). Only the jobs
(`AibtAgentFrameJob`, `CatalogJob`) are Burst-compiled. What it would take, one of:
- `[BurstCompile]` on the generated facade, so the existing direct call from `GeneratedExecutor` to the static
  `ExecuteImmediate` is redirected; the signature (`ref BurstExecutionBatch`, blittable return) has to qualify
  for a direct call.
- A cached `BurstCompiler.CompileFunctionPointer` per catalog, called by the executor.
- Running the `Immediate`, same-frame batched and pipelined node dispatch inside a Burst job, as the agent-frame
  path does (a scheduling change).
Each changes `CodeGen~` and every generated catalog (protected; a recorded decision first) or execution paths.
Each also needs the evidence the agent-frame row has:
- `BurstExecutionPathTests` rows flipping to Burst;
- bit parity with the managed run per path (Float32 results can differ under Burst's float mode);
- zero GC per update;
- the first frames before asynchronous compilation finishes;
- managed exceptions and `[BurstDiscard]` code inside bodies;
- an IL2CPP Player run.

## Deviations and stops

- No stop. Usage limit interrupted the work once (before items 4-9); resumed from `progress-notes.md`.
- One run passed `-Assemblies AIBT.Integration.Tests` together with `-Filter`, so the whole Integration
  assembly ran once: 560/561, the one failure being item 7's intended fail-before (`raw/item7-before-and-integration-assembly.txt`).
- U3's first version used a one-tick wait inside a repeater, which completes within one update and hit
  the 1 000 000-step limit; the test uses a two-tick wait.
- Section B's U9 test builds its catalog from the P10-010 fixture shard (the project catalog lists player
  assemblies only).

## Not verified

- The full ten assemblies (not run, by instruction).
- U3 on a device or with an IL2CPP Player. Burst execution is now observed in the Editor on the agent-frame job
  (review follow-up item 1), not in a Player.
- Item 15's platform statement on a 32-bit ARM device.
- U9 with a subtree-using tree that has registered-type keys.
- Swarm Arena's 1/32 rate in a Player (not changed here).
