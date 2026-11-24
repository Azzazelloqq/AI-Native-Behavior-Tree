# History rewrite of 2026-11-24: old and new commit hashes

On 2026-11-24, before the first push of `feature/aibt-consumer-workflow`, the commits that were not yet on
GitHub were rewritten to remove one file from history:
`Planning~/Evidence/P7-035/swarm-arena-player-20260917-090844.raw.json` (370,292,632 bytes, added in the
old commit `abca014` and deleted from the tree in the old commit `bfadf82`). GitHub refuses files over
100 MB, so the branch could not be pushed with it. The owner chose the rewrite.

- Nothing else changed. Every rewritten commit has the same author, dates and message, and its tree
  differs from the old one by that file only (checked commit by commit); from `bfadf82` on, the trees are
  identical. The head tree is `6c8efc6600561725613c0e1dd5d352c04faddae8` before and after.
- The first commit after `origin/main` (`01e8f5f`) precedes the file and kept its hash. The other 238
  commits have new hashes.
- **Planning records, evidence files, cards and commit messages written before this date cite the old
  hashes.** Use the table to find the commit that is on GitHub. Records written after it cite new hashes.
- The Modules repository's commits made before this date point at old AIBT hashes (the last one at
  `3f04417`); those commits were never on GitHub.
- The raw report itself is not in the repository; its summary is in `Planning~/Evidence/P7-035/`.

| Old | New | Subject |
|---|---|---|
| `abca014` | `5509b07` | benchmarks: record Swarm Arena Player matrix |
| `3f04417` | `04f1cfe` | docs: complete Swarm Arena UX review |
| `1047104` | `52cbcee` | wip |
| `567a29c` | `f101376` | feat: improve behavior tree authoring and preview |
| `2612389` | `140cf33` | wip: baseline P8-009..P8-012 consumer workflow, CLI, agent tools, patrol research |
| `2be5e56` | `21ec1e8` | planning: open P9 usability phase (managed agent layer, node library, graph editor, native facade) |
| `0f4b4f6` | `91828a9` | agents: P9-001 managed agent layer (BehaviorTreeAgent, C# nodes, named blackboard, condition interrupts) |
| `d973939` | `f6fd084` | agents: P9-001 tail (MCP expectations, focused tests, Preview message, evidence) |
| `4360a10` | `c557e0a` | graph: P9-003 graph editor usability and live debugging |
| `eac65b9` | `7313025` | P9-002: standard node library (38 aibt.unity.* nodes, native stdlib shard) |
| `60f0b71` | `70b3cab` | runtime: P9-004 native tree agent facade, automatic preparation, native live debug |
| `0899e40` | `ae2d06e` | codegen: P9-005 short AIBT.Native node declarations and optional lifecycle callbacks |
| `43dc15e` | `a6b68d4` | docs: P9-006 user guide, stripped-Player fix and clean-consumer acceptance |
| `c54bbaf` | `7c72726` | perf(P10-001): zero-allocation managed tick, O(1) native registration, Interval stagger |
| `4dee8ec` | `82c669d` | perf(P10-002): reuse BatchedJobsSameFrame round storage, one parallel job per round |
| `93e7522` | `ce61156` | perf(native): cheaper dispatch framing around node bodies (P10-003, stage F1) |
| `9686f47` | `00b1844` | perf(native): opt-in JobsAgentFrame policy, one Burst job work item per agent update (P10-004, stage J2) |
| `659a8b1` | `b5544ce` | docs(P10-004): JobsAgentFrame evidence, interleaved IL2CPP numbers, ADR outcome |
| `7de8a69` | `eea90a7` | P10-005: measure native scheduling policies and propose automatic selection |
| `611b8a3` | `ec435c4` | P10-006: compile on Unity 6000.6.9f1 and record its test baseline |
| `8e4e634` | `b4880b4` | P10-006: add AIBT Graph and SimpleAgent Play Mode evidence on 6000.6.9f1 |
| `ea7def6` | `fd7dd2a` | perf(scheduler): run JobsAgentFrame batches shorter than 64 members in place (P10-007, step 1) |
| `1cbfa3f` | `79cbadf` | perf(scheduler): select JobsAgentFrame automatically for runs of 64+ eligible default-profile hosts (P10-007, step 2) |
| `c3759bd` | `68495e1` | docs(design): propose GameObject-free simulation container stepped by the game (P10-008, stage G0) |
| `d436789` | `e6971c5` | refactor(host): extract MonoBehaviour-free per-agent core from ProductionTreeHost (P10-009, step 1) |
| `d181cb0` | `070b829` | feat(simulation): add experimental GameObject-free TreeSimulation stepped by the game (P10-009, ADR-P10-008 stage G1) |
| `14b6799` | `00ab4f2` | docs(evidence): record the P10-009 step 2 commit id |
| `02c0658` | `3cdb6e7` | feat(simulation): container stage G2 - game inputs, commands, wake on changed input (P10-010) |
| `5b4ec02` | `846f62c` | bench(P10-010): sim-io Player set, evidence and task card for simulation container G2 |
| `9736622` | `679d0d7` | feat(simulation): container stage G3 - external long-running actions (P10-011) |
| `15ac74e` | `643d570` | bench(P10-011): sim-actions Player set, evidence and task card for simulation container G3 |
| `0dc23d1` | `150df3f` | feat(simulation): container stage G4 - traces of selected agents, active-path query, opt-in registry, MCP/CLI tools (P10-012) |
| `40e2a06` | `9035140` | chore(simulation): G4 evidence, sim-trace bench, host flag reverted, deterministic registry test (P10-012) |
| `1dd7dd0` | `9261b0e` | fix(simulation): container default MaximumStepsPerThink 10 000 (P10-011 finding F1) |
| `23a465d` | `8c8a2a8` | docs(adr): P10-013 reactive composites that keep their running child (finding F2), proposed |
| `ce783f8` | `9745aa9` | test(P10-013): pin v1 reactive restart of multi-update work |
| `5c4bdfe` | `5557164` | feat(P10-013): retaining reactive composites, stage 1 (reference executor) |
| `d47f948` | `fcd2727` | P10-013 stage 2: retaining reactive composites on the native executor |
| `3e4b09e` | `d98811a` | bench(P10-013): stage 3 IL2CPP measurement of retaining reactive composites |
| `2e6b9c7` | `0776bc9` | fix(native): P10-014 fixes from the P10-013 code review |
| `a8a74e3` | `73e41fc` | docs(adr): P11-001 subtrees (feature F1), proposed |
| `c68df77` | `f773d24` | fix(hot-reload): P10-014 narrow the R1 migration guard to a retaining pass in progress |
| `1aeaaff` | `21f5ec7` | docs(planning): P10-014 acceptance run and the owner's F-S2-2 decision |
| `f02ad04` | `0fce86a` | docs(planning): accept ADR-P11-001 subtrees (AIBT-049); record the owner's P10-014 answers |
| `5238c90` | `c3389bf` | P11-001 stage 1: subtrees (tree format v3, validator, expander, managed path) |
| `fb3717a` | `e7f163e` | P11-001 stage 1: specification edits of ADR-P11-001 4.8 |
| `d1e76ff` | `31efd39` | P11-001 stage 1: evidence, changelog, task card, work item |
| `7e614b8` | `4945f93` | P11-001 stage 1: regenerate API reference; explicit expansion format version |
| `1073d07` | `9c27e4b` | docs(planning): P11-001 stage 1 accepted; owner allows expanded key IDs in generated bindings |
| `053219a` | `b1fdb93` | P11-001 step 0: binding values accept blackboard key IDs (owner decision 9) |
| `73a516f` | `edd69ba` | P11-001 stage 2: generated V2 compiler overload with a subtree resolver |
| `221aefd` | `a5cce95` | P11-001 stage 2: import and baking of trees with subtrees |
| `c02b42b` | `e182923` | P11-001 stage 2: evidence, changelog, task card, work item |
| `fedf5cb` | `688425a` | docs(planning): P11-001 stage 2 accepted |
| `d8b34a8` | `e549083` | docs(planning): P11-001 stage 3 approved by the owner |
| `8fce1aa` | `7ad4dbc` | P11-001 stage 2 follow-ups: Needs registry status and Packages/ in the tree-ID index |
| `d6f073f` | `7524166` | P11-001 stage 3: graph edits keep the subtree interface and references |
| `77c41c7` | `405d00d` | P11-001 stage 3: subtrees in the graph editor |
| `0b1926a` | `c54a855` | P11-001 stage 3: Extract keeps the node's label; demo fixtures for the evidence |
| `c40a6ab` | `03d9a80` | P11-001 stage 3: an undone extraction's file name can be used again |
| `da3d792` | `84dfad1` | P11-001 stage 3: evidence, changelog and planning |
| `7794cb0` | `94d2698` | P11-001 stage 3: restore a stage-1 line in the evidence README |
| `6278956` | `fd923c7` | docs(planning): P11-001 stage 3 accepted |
| `6caf728` | `2eec376` | docs(planning): P11-001 stage 4 approved by the owner |
| `3d2f0a8` | `cf11187` | feat(mcp): subtree reference tools and resolution (P11-001 stage 4) |
| `8415ce6` | `7ed5003` | feat(mcp-server,cli): subtree tools and tree expand/usages/extract/unpack/keys commands |
| `9e9d7ea` | `50c9dfe` | test(mcp): dispatcher tests for subtree tools and describe_agent labels in a subtree |
| `858e4ae` | `427d43c` | fix(graph): make the running glow visible on subtree reference cards |
| `d471498` | `122d45a` | docs: subtrees user guide and subtree MCP/CLI documentation |
| `6b7e141` | `454ba60` | chore(planning): P11-001 stage 4 changelog, evidence and handoff notes |
| `5b3974a` | `22e10af` | docs(planning): P11-001 stage 4 accepted |
| `34e80f0` | `5d3bfb9` | fix(validation): naming policy checks a key's own name, not its subtree qualifier |
| `9ff7480` | `5052923` | docs(planning): P11-001 naming-policy fix accepted; item in Review |
| `579ffcb` | `9b9c4c1` | docs(adr): P11-002 native node library (feature F4), proposed |
| `ccde239` | `36cfa7a` | docs(adr): P11-002 accepted by the owner (AIBT-050); stage 1 approved |
| `442d9aa` | `e73f595` | feat(P11-002): native node library stage 1, 20 blackboard and flow nodes |
| `760a1b3` | `ce5d6a0` | fix(P11-002): resolve generated handles through their own case's configuration field |
| `d7aa900` | `8badc77` | test(P11-002): exact native node sets grow from 4 to 24 |
| `2c8c810` | `bc7a1f1` | docs(P11-002): node catalog includes the package native shard |
| `9b81013` | `2aad2e3` | feat(P11-002): bake error names the shard to add |
| `84f5a76` | `cfe8839` | test(P11-002): library behaviour, simulation, parity and reactive guard tests |
| `20d73aa` | `de42496` | test(P11-002): demo tree from aibt.core and aibt.native nodes only |
| `8ad78fa` | `fd66bf4` | docs(P11-002): README table, native guard paragraph, CHANGELOG stage 1 |
| `4af5571` | `707047b` | docs(P11-002): stage 1 evidence and handoff notes |
| `f2d58d2` | `51e74af` | docs(P11-002): stage 1 accepted by the orchestrator |
| `82eb8ea` | `e163533` | docs(P11-002): stage 2 approved by the owner |
| `3d6f0a7` | `fe1221b` | feat(P11-002): stage 2 timing and signal nodes in aibt.native.standard |
| `c055691` | `933f876` | test(P11-002): stage 2 timing and signal nodes on hosts, NativeTreeAgent and TreeSimulation |
| `00e6045` | `f6d3b93` | docs(P11-002): regenerate node-catalog.md with the stage 2 nodes |
| `53ac259` | `da0cc57` | feat(P11-002): Native Guard sample built only from package nodes |
| `43e6016` | `724f4d0` | docs(P11-002): Native node library guide page |
| `4db2ccb` | `34f3fd5` | docs(P11-002): README rows and CHANGELOG for stage 2 nodes |
| `bad2892` | `7b72bb7` | plan(P11-002): stage 2 evidence and task card notes |
| `e3ac983` | `8d4501d` | docs(P11-002): README guard advice names the retaining reactive composites |
| `0c113e6` | `3982755` | docs(P11-002): stage 2 accepted by the orchestrator |
| `d1218b1` | `420a971` | docs(P11-002): stage 3 approved by the owner |
| `cae6622` | `03910ea` | plan(P11-002): stage 3 runtime seeding plan, written before the change |
| `ba7c944` | `947b394` | feat(P11-002): hosts seed the random streams of nodes that declare one |
| `9808498` | `5cce2e0` | docs(P11-002): regenerate the runtime API reference for host seeding |
| `bb059c4` | `b6d84bd` | feat(P11-002): five random nodes in aibt.native.standard (Native/Random) |
| `fb89797` | `8e996f1` | test(P11-002): random node contracts on the host and in TreeSimulation |
| `e3ffc41` | `2cc46f2` | docs(P11-002): randomness in the native node library guide and the host page |
| `04ba623` | `290d191` | fix(P11-002): group job waves commit the advanced random stream state |
| `b3b3660` | `41fa45f` | feat(P11-002): Native Guard patrol shows seeded randomness |
| `fdc75dc` | `cafb2e9` | docs(P11-002): randomness recipe, group-dispatch fix and stage 3 changelog |
| `e944c5c` | `cdd06fb` | evidence(P11-002): stage 3 random nodes, host seeding and group-commit fix |
| `9f2b8fb` | `89fa7ee` | docs(P11-002): stage 3 accepted by the orchestrator |
| `23534c8` | `e34f163` | docs(P11-002): stage 4 approved by the owner |
| `3e00372` | `4818e1e` | docs(P11-001): closed by the owner |
| `b5439d2` | `31d0b13` | docs(P11-002): MCP and CLI recipes for native trees from library nodes |
| `2f39af3` | `c356666` | docs(P11-002): native quickstart points to the native node library |
| `b62f8d6` | `f4ede40` | docs(P11-002): native-only nodes for AI clients and the consistency sweep |
| `029888b` | `1690008` | docs(P11-002): stage 4 changelog entry and stage 3 parameter names |
| `fe28a5e` | `1f7221a` | evidence(P11-002): stage 4 documentation pass |
| `3cd9fbb` | `2f2fa45` | docs(P11-002): the owner's answers on the open questions |
| `cb20509` | `b3f3438` | docs(P11-002): the native recipe tree loops inside one run |
| `b94ca96` | `a74d003` | docs(P11-002): a finished patrol ends a Native Guard run too |
| `0485779` | `9b5a95d` | evidence(P11-002): stage 4 rework after the acceptance run |
| `360538b` | `135e61a` | docs(P11-002): stage 4 accepted by the orchestrator, item in Review |
| `ce3bd35` | `420babd` | fix(P11-003): AIBT2043 counts Write and ReadWrite members of native bindings |
| `375deca` | `df8be62` | fix(P11-003): drop the semantic projection's AIBT2043 info in the native compiler |
| `f7885bf` | `07a4b0e` | test(P11-003): AIBT2043 with native bindings on every path and the host rule |
| `43f14ac` | `630c140` | docs(P11-003): recipes list only input keys for AIBT2043; changelog |
| `5094631` | `083be2b` | evidence(P11-003): task card, work item, evidence; awaits acceptance |
| `0038a3b` | `224014b` | docs(P11-003): accepted by the orchestrator, item in Review |
| `2ae7e06` | `071b657` | docs(P11-004): ADR proposed for reading a Tree-scope value from a running native tree |
| `0ca665c` | `1620617` | docs(P11-004): ADR accepted with the orchestrator's changes; AIBT-051 row |
| `c8ca9f3` | `462f8f3` | feat(P11-004): read a Tree-scope value of a running native tree |
| `75eab34` | `a32de6a` | test(P11-004): host and agent value reads |
| `aded2f9` | `b3c9e1a` | docs(P11-004): reading a value back; host read; changelog; API reference |
| `1eb2359` | `4cfb88f` | evidence(P11-004): task card, work item, evidence; awaits acceptance |
| `ac4201c` | `e539048` | fix(P11-004): build a refused read's message only when it is logged |
| `8d03467` | `b295155` | evidence(P11-004): fix after the orchestrator's review |
| `2fff7e5` | `db6458b` | docs(P11-004): accepted by the orchestrator, item in Review |
| `a9334c9` | `a3fabb5` | feat(P11-005): live blackboard values of a native agent, truthful tree-list status |
| `7719d02` | `31a97ff` | test(P11-005): native live blackboard and tree-list status rule |
| `282c1c9` | `917e31b` | docs(P11-005): native blackboard values and tree-list status in the graph page |
| `bb56c00` | `c0228fe` | docs(P11-005): task card, work item, MASTER_PLAN sentence, evidence |
| `ad1e581` | `e9da1a6` | docs(P11-005): accepted by the orchestrator, item in Review |
| `88473d9` | `04ef448` | docs(P11-006): production-readiness review of P11-001 to P11-005 |
| `4be2983` | `b570502` | docs(P11-006): the orchestrator's check of the review report |
| `02cf4ec` | `1b434d6` | fix(P11-007): NativeTreeAgent.TryGetValue refuses a faulted run at once (review W5) |
| `c2510cf` | `28e4e43` | P11-007 F1: one key on two handles of a native node shares its live offset |
| `7be4d4b` | `32ae61c` | test(P11-007): stale tests run honestly in the embedded layout (review F12) |
| `aa3bf3d` | `b1ab266` | fix(P11-007): format 3 without subtree features compiles natively (review F2) |
| `7fac1a9` | `f95261d` | fix(P11-007): MCP runtime_hosts and read_trace see NativeTreeAgent hosts (review F7) |
| `c43a1f5` | `58a395d` | fix(P11-007): one project scan per tree-list refresh (review F4) |
| `ff023cd` | `cc7f885` | fix(P11-007): inspectors keep the staleness state between repaints (review F3) |
| `06fd964` | `3ec2b37` | fix(P11-007): no AssetDatabase.Refresh after a reload with nothing changed (review F5) |
| `42253ac` | `025ad71` | docs(P11-007): task card, work item, MASTER_PLAN and evidence for review fixes batch 1 |
| `c02d92f` | `1a189a9` | docs(P11-007): orchestrator's acceptance of review fixes batch 1 |
| `45de034` | `e7b7bcd` | docs(P11-007): the pinned compiled hash is stale since 5c4bdfe |
| `8c4cba2` | `fba1929` | test(P11-008): refresh the stale compiled pin generated-compiled-v2.json |
| `18aca41` | `011e33a` | fix(P11-008): the host read refuses a type argument that is not the managed type of expectedType (review F8) |
| `028c7ce` | `68b27cb` | fix(P11-008): BehaviorTreeAgent refuses an observer block at load with AIBT2051 (review F10, managed half) |
| `6d34b8d` | `5b0de8d` | fix(P11-008): one meaning per diagnostic code; MigrationApplied is AIBT2052, TreeScopeSlotNeverWritten AIBT2053 (review F11) |
| `7773059` | `977f589` | docs(P11-008): ADR-P11-008 proposed (F13), task card, work item, MASTER_PLAN and evidence for review fixes batch 2 |
| `1d081c7` | `beabb77` | docs(P11-008): orchestrator's acceptance of review fixes batch 2 |
| `c38e5bf` | `b52a086` | fix(compiler): place Agent and Shared slots past Tree in the v2 semantic projection (P11-009 F9) |
| `2700107` | `86a89ff` | feat(validation): warn AIBT2054 for observer blocks in native trees (P11-009 F10) |
| `1e54b2f` | `5ce27d8` | fix(host): external write resolve checks T against expectedType (P11-009 F8 write side) |
| `33debb8` | `b7ac1e3` | fix(mcp): resolve subtrees against the Editor's file set (P11-009 F6) |
| `3d16480` | `19e7f07` | docs(P11-009): task card, work item, MASTER_PLAN, CHANGELOG and evidence for review fixes batch 3 |
| `1db104a` | `1392a41` | docs(P11-009): orchestrator's acceptance of review fixes batch 3 |
| `1a5e1cd` | `62ba339` | fix(dispatch): count two value sessions per ReadWrite binding (P11-010) |
| `0c2b541` | `7ea1a27` | docs(P11-010): task card, work item, MASTER_PLAN, CHANGELOG and evidence for ReadWrite binding sessions |
| `0aa9a6a` | `5a8728e` | docs(P11-010): orchestrator's acceptance of the ReadWrite binding session fix |
| `3d91662` | `774ede2` | feat(random): repeatable random streams for native hosts and NativeTreeAgent (P11-012) |
| `69eb9d7` | `5647e7f` | feat(editor): Native Tree Agent inspector hint for random trees without a stream id (P11-012) |
| `7ae6c1b` | `873b8b2` | docs(random): stream identity in time-and-random-v1, ADR-P11-008 accepted as AIBT-052 (P11-012) |
| `513f686` | `e56b702` | docs(random): user guide, production host page, Native Guard sample, API reference and changelog for stream ids (P11-012) |
| `d46fe21` | `5a5a877` | docs(P11-012): task card, work item, MASTER_PLAN and evidence for reproducible random streams |
| `562e54a` | `30709a9` | feat(agent): Agent-scope keys of NativeTreeAgent last across runs (P11-013) |
| `1e5dda6` | `9ae86d4` | feat(graph): live values of Agent-scope keys for a native agent (P11-013) |
| `0c6f38d` | `36bb54a` | docs(agent): ADR-P11-013 accepted as AIBT-053; ADR-P11-004 D3 amended for the agent (P11-013) |
| `daed617` | `42e603c` | docs(agent): what a restart keeps, Agent reads, API reference and changelog (P11-013) |
| `e70dbea` | `31a2862` | docs(P11-013): task card, work item, MASTER_PLAN and evidence for Agent memory of NativeTreeAgent |
| `824a378` | `e5c8aa2` | docs(P11-012, P11-013): accepted after the full run on e70dbea and an independent review |
| `8cde249` | `b0ea73a` | fix(scheduler): a pipelined group with no eligible host is not a failure (P11-014 item 10) |
| `109b6c3` | `6d02c61` | test(agent-memory): missing and unfailable P11-013 tests (P11-014 item 11) |
| `b7494b3` | `fb6c7fa` | docs(random): correct the combine function's description and which bootstraps take an id (P11-014 item 12) |
| `fe95c67` | `b70523c` | docs(agent-memory): failed starts, a host stopped by user code, handle-like values, RunIndex 0 (P11-014 item 13) |
| `c3d765f` | `639ff91` | fix(inspector): the random-stream hint follows an asset prepared again in place (P11-014 item 14) |
| `2584046` | `cf40781` | test(agent-memory): pin an Agent range at an odd offset (P11-014 item 15, record) |
| `108c56c` | `1c378b4` | fix(validation): one observer rule for MCP, the tree list, the graph and the bake (P11-014 items 1-3) |
| `dcee3a9` | `fc50aee` | feat(mcp): create_tree writes format 2 unless formatVersion 1 is given (P11-014 item 4, W10) |
| `55097bf` | `e49a2fd` | feat(host): ProductionTreeHost.IsExecuting is public (P11-014 item 6, W6) |
| `7501ae1` | `a93c988` | fix(agents): a failed subtree import reports its own errors before AIBT3020 (P11-014 item 5, W3) |
| `052f445` | `edfd266` | fix(host): the value-session refusal names the node and the rule (P11-014 item 7) |
| `33eb750` | `f9191d9` | docs(comments): two wrong comments about staged reads and the Agent base (P11-014 item 8) |
| `da428af` | `88c0ac4` | test(native-library): aliased copy and compare cases that tell a lost write from an applied one (P11-014 item 9) |
| `72aeec6` | `476b275` | test(confirmations): U3, U4 and U10 hold; tests kept (P11-014 section B) |
| `f37f44e` | `fc627e0` | docs: random guards, a subtree root's observer, the one-run cooldown, Known limitations (P11-014 section C) |
| `3391ea3` | `db736ba` | fix(tree-list): read the project's registered blackboard types (P11-014 U9) |
| `3d790d9` | `e34197c` | docs(changelog): P11-014 finishing batch; correct the P11-008 MCP sentence |
| `9a395a1` | `1aa78ca` | docs(P11-014): task card, work item, MASTER_PLAN and evidence for the finishing batch |
| `46da0de` | `ca0aa68` | fix(mcp): validate, compile and the manifest use the Editor's defaults without a policy file (P11-015 D1) |
| `fee9937` | `e30998c` | docs(policy): what happens without .aibt/policy.json; ADR-P9-001 amendment (P11-015 D1) |
| `caef926` | `f5c5efb` | fix(tests): fixture assemblies are test assemblies, so a consumer no longer compiles them (P11-015) |
| `f1b5955` | `11b5822` | feat(samples): Swarm Arena works in a consumer project: Create Demo, materials, input and pipelined movement (P11-015 part A) |
| `31ea067` | `84fdebe` | test(scheduling): pin the PipelinedJobs cadence by counting frames and steps (P11-015 D5) |
| `545f520` | `6132659` | docs: PipelinedJobs cadence, importer status, verified Editor and install routes, where the Player compiles (P11-015 D5 D6 D7 D9) |
| `bfadf82` | `df2ce92` | chore(packaging): keep development folders and the 370 MB raw report out of the package (P11-015 D8) |
| `a69c3f9` | `bc3dc5d` | docs(changelog): consumer-install fixes (P11-015) |
| `3a1f061` | `85ad460` | planning: P11-011 (F14 consumer install) and P11-015 (consumer-install fixes) cards, work items, MASTER_PLAN, evidence |
| `baa579e` | `be93301` | docs(readme): the git archive step writes the tar outside the checkout (P11-015 item 7) |
| `9ccabed` | `b464f17` | docs(performance): size and memory of AIBT in a Windows IL2CPP Player (P11-015 item 8) |
| `87ef22a` | `cac85e8` | planning(P11-015): evidence of the fresh-install proof (item 7) and of size and memory (item 8) |
| `407cf1f` | `d81e621` | test(burst): observe which paths run node bodies Burst-compiled; replace the U3 parity test (P11-014 review item 1) |
| `77966db` | `b7afbe1` | fix(host): a CapacityExceeded message names the node and only the limit its site knows (P11-014 review item 2) |
| `8bd2d51` | `f1f3264` | fix(editor): read registered blackboard types in subtree files and at import; W3 also for Needs registry (P11-014 review item 3) |
| `d433a1a` | `1e6d08f` | test: tests that could not fail or asserted less than the evidence (P11-014 review item 4) |
| `5534826` | `3093a17` | P11-015 review item 5: default policy after a failed subtree expansion; prepare_runtime and edit-tool wording |
| `2e70915` | `bf725d2` | P11-014 review item 3 fix-up: regenerate api-reference-editor.md |
| `9901ebf` | `549129f` | P11-015 review item 6: shipped docs and Tools~ no longer point into excluded folders |
| `f019ff7` | `00731fb` | planning: review follow-up of P11-014 and P11-015 (evidence, cards, CHANGELOG) |
| `fe43768` | `6f3dbf9` | docs(ADR-P9-001): second amendment, one default policy for the authoring tools and the graph session |
| `b7378b3` | `e3bda03` | fix(policy): the MCP authoring tools and the graph edit session use the shared default without a policy file |
| `dcb1b2d` | `b9a403f` | docs: one default policy for every tool; limitations that stay; where node bodies run Burst-compiled |
| `3862964` | `3d8582e` | planning: policy default evidence (P11-015) and the Burst open finding (P11-014) |
| `32c8bfa` | `4b2f052` | planning(P11-014, P11-015): accepted by the orchestrator on 3862964 |
| `51dfaed` | `6348c4a` | perf(P11-016 item 1): leaf dispatch workspaces borrow the catalog's metadata arrays |
| `f30a80a` | `c6f07df` | docs(ADR-P7-010): amendment, no trace channel by default in a Player (P11-016 item 2) |
| `a02d2f4` | `c5b4cc5` | perf(P11-016 item 2): no trace channel by default in a Player |
| `6afd082` | `2bffd51` | perf(P11-016 item 3): leaf-only agent-frame slot table, catalog-wide fields once per agent |
| `72db77b` | `cd50ccd` | perf(P11-016 item 3): trim the agent-frame slot table and map to their length |
| `2f1c0d7` | `b5cd19a` | planning(P11-016): stage 1 records and evidence |
| `f8ad6b9` | `bd1a121` | planning(P11-016): stage 1 accepted, decision on the second round of memory |
| `26c803e` | `d187465` | docs(P11-016 follow-up A1): TraceChannelOwner says when it is null |
| `ec9bd3f` | `cab0ab4` | docs(P11-016 follow-up A2): the specification sentence says which hosts default to no channel |
| `0aadd7e` | `df84911` | docs(P11-016 follow-up A4): one measured per-leaf figure for item 1, with its catalog |
| `d6f6d66` | `52a73d8` | test(P11-016 follow-up A5): borrowed metadata identity for all eight arrays, a real mismatch, two hosts in one parallel job |
| `d1b6d1a` | `8ab5667` | fix(P11-016 follow-up A6): no agent-frame trace list without a recorder; trace menu and record cost say what they mean |
| `cbe4a3f` | `7c62d9d` | docs(P11-016 follow-up A7): PlayerPerf runs before and after a02d2f4 are not comparable |
| `24de026` | `5aa749b` | perf(P11-016 item 4): AIBT.Runtime escapes its JSON strings itself and no longer references Newtonsoft |
| `9fe4529` | `86f2c6c` | docs(P11-016 item 5): ADR-P9-004 amendment, managed node rows of link.xml only when the Player uses AIBT.Agents |
| `8b4ff3f` | `c19b8ae` | perf(P11-016 item 5): link.xml keeps the AIBT.Agents rows only when the Player references AIBT.Agents |
| `7df44de` | `f4dcb57` | planning(P11-016): accepted with stages 0-1 and items 4, 5; the rest postponed by the owner to P11-017 |
