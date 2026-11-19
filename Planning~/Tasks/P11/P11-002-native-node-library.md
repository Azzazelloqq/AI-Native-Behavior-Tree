# P11-002 — Library of ready-made native nodes (feature F4)

Status: `In Progress` — ADR accepted 2026-11-19 (AIBT-050) with all six recommendations of its §8;
stage 1 (blackboard and flow nodes) approved the same day and accepted after the full run of all
nine assemblies on `4af5571`. Stages 2–4 each need the owner's go-ahead.

## Objective

A tree author can build useful native trees (`NativeTreeAgent`, `ProductionTreeHost`,
`TreeSimulation`) from nodes shipped with the package — blackboard compare/set/copy/arithmetic,
Float3 distance, flow leaves, timing and shared deadlines, a signal to the game and (with the owner's
answer) random values — without writing Burst nodes. The existing `aibt.native.standard` shard (P9-002,
four nodes) grows into that library. Execution semantics, ABI v2, the compiled program format and the
trace format do not change.

## Decision record

`Documentation~/decisions/ADR-P11-002-native-node-library.md` (Accepted 2026-11-19, AIBT-050; the owner's answers
are in its "Owner decisions" section). Decided:
native-only nodes under `aibt.native.*` (P9-004 naming rule), added to the existing shard
`aibt.native.standard` in `AIBT.StandardNodes`, opt-in through the project's `[AibtCatalogSet]` as
today; one node per value type; blackboard keys, time, random and one package effect channel
`aibt.native.signal` only (no scene access). First set: 20 blackboard and flow nodes (stage 1), 4
timing and signal nodes (stage 2), 5 random nodes (stage 3; the owner allowed hosts to seed random streams).
Rejected: dual-path `aibt.stdlib.*` nodes as the default (the reference executor and managed agent have
no binding, time or random support for leaves — P7-028 wall 1 still stands), an importable sample,
an always-on shard.

## Depends on

- Owner's answers to ADR §8 (given 2026-11-19, all as recommended).
- P9-002 (the shard this grows). No dependency on P11-001.

## Required reading

- `Documentation~/burst-node-authoring.md`, `Documentation~/specifications/burst-node-abi-v2.md`,
  `time-and-random-v1.md`, `determinism-v1.md`, `blackboard-v1.md` (external writes),
  `ADR-P7-041-production-world-integration.md` (effects).
- `Planning~/Evidence/P7-028/README.md` (wall 1, `aibt.stdlib.*`), `Planning~/Evidence/P9-002/README.md`,
  `Planning~/Evidence/P9-004/README.md` (naming rule), `Planning~/Evidence/P10-009/README.md` §3
  (unseeded host random streams), `Planning~/Tasks/P7/P7-036-swarm-arena-ux-and-node-review.md`
  (promotion rule).

## Scope (after approval, by stage; see ADR §5 and §7)

- Stage 1: `Authoring/Registry/Generated/StandardNative/` (20 nodes); `MCP/Documentation/McpNodeCatalogDocumentGenerator.cs`
  (include the package native shard); `Documentation~/generated/`; `README.md` node table; bake error
  text in `Editor/Graph/ProductionTreeAssetBaker.cs`; tests under `Tests/Integration/NativeRuntime/`,
  `Tests/Integration/Simulation/`, `Tests/Editor/Agents/`, the exact-set tests
  `StandardNativeShardTests.cs` and `Tests/Editor/Mcp/Discovery/McpToolDispatcherTests.cs`.
- Stage 2: four timing/signal nodes; demo tree and scene for evidence; user guide page.
- Stage 3 (only with Q4 = yes): five random nodes; host random-stream seeding in
  `Runtime/Integration/GeneratedDispatch/` and the host bootstrap path; replace the P10-009 pin test.
- Stage 4: `CHANGELOG.md`, `consumer-quickstart.md`, MCP recipes.

## Forbidden changes

- Execution semantics; both executors' lifecycle code (`Runtime/Execution/**`); new `aibt.core.*` types.
- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`; the code generator (`CodeGen~`); compiled
  program format and version; trace format; package version; normative specifications.
- IDs, versions or behaviour of the four existing `aibt.native.*` nodes and of `aibt.stdlib.*`.
- Existing public API signatures (additions only).
- `ProductionTreeHost` / `NativeTreeAgent` / `ProductionTreeScheduler` behaviour, except stage 3
  random-stream seeding if the owner answers Q4 with yes.

## Deliverables

- Per ADR §7, stages 1–4, each with evidence in `Planning~/Evidence/P11-002/`; stage 1 with a palette
  screenshot, stages 2 and 3 with screenshots and a Play Mode clip of the demo tree.

## Acceptance criteria

- The generator accepts the grown shard in a catalog set next to a project shard; no new diagnostics.
- Every new node has a behaviour test on `ProductionTreeHost` and runs identically in a `TreeSimulation`
  group in place and with Jobs.
- Managed/native parity: trees built with `aibt.unity.*` and with the matching `aibt.native.*` nodes give
  the same blackboard values and root results (floats within a stated tolerance).
- `node-catalog.md` lists every `aibt.native.*` node; palette and MCP `search_nodes` show them.
- ABI contract test unchanged; public API diff additive only.
- Stage 3: host stream words follow `time-and-random-v1.md`; trees without random nodes behave and hash
  exactly as before.

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Filter <new library test classes>   (one runner per Editor)
Tools/Unity/Invoke-UnityTests.ps1 -Assemblies AIBT.CodeGen.ContractTests  (ABI v2 unchanged)
Tools~/Verification/Verify-Static.ps1
Save-UnityWindow.ps1 (palette), Save-UnityClip.ps1 (demo tree) for stages 1–3
```

## Handoff notes

- The brief for this item listed `aibt.core/stdlib/unity/samples` and did not mention `aibt.native.*`;
  the seed library (P9-002, renamed in P9-004) was found while reading and the item was reframed as
  "grow it" (orchestrator, 2026-11-19), the owner's goal unchanged.
- Adding nodes to the shard changes the catalog fingerprint of every catalog set that lists it; assets
  prepared against such a set fail bootstrap (`TypeMismatch`) until re-prepared, which the Editor does
  after the next domain reload. In the package only tests and the Phase 10 benchmark list the shard.
- Stage 1 found a host defect: `GeneratedTreeDispatchAdapterV2.cs:840` indexed the global
  configuration-field array with a case-local ordinal, which broke handles that are not field 0. The
  owner allowed the one-line fix on 2026-11-19 (ADR "Owner decisions" item 7). It is the only host-code
  change in stage 1, and `GeneratedDispatchFieldOrdinalRegressionTests` covers it.
- Stage 1 is implemented and accepted by the orchestrator (2026-11-19, `4af5571`); evidence in
  `Planning~/Evidence/P11-002/README.md` ("Stage 1"), raw runs and screenshots in
  `C:\UnityProjects\_aibt_acceptance\P11-002\stage1\`. Deviation from ADR §7 stage 1: native hosts do
  not run observer re-evaluation, so the guard over a memory composite is tested as a library
  condition under a reactive composite, which aborts the memory composite at the next update
  after the key changes (`NativeLibraryReactiveGuardTests`). ADR §7 is unchanged.
- Open question 1 for the owner: native hosts (`ProductionTreeHost`, `NativeTreeAgent`,
  `ProductionTreeScheduler`, `TreeSimulation`) never queue or re-evaluate observers
  (`NativeObserverQueueV1` has no caller in `Runtime/`), unlike `execution-semantics-v1.md:225`, and
  the managed `BehaviorTreeAgent` faults with AIBT4002 on any observer block. This is existing
  behaviour, not caused by P11-002; a possible follow-up work item (not created). Documented in
  `production-host.md` and the README.
- Open question 2: AIBT2043 ("no node in this tree writes it") ignores writes through generated
  native `bindings`, so it shows for keys that native nodes write. It is an Info note only and
  existed before P11-002.
- Stage 1 changed three node-list expectations (`StandardNativeShardTests`,
  `StandardNodeCatalogTests`, `McpToolDispatcherTests`: 4 native IDs → 24). No other existing
  expectation changed. Demo tree: `Tests/Fixtures/P11-002/native-library-demo.aibt.json`.
- ADR §9 lists four unrelated findings (stale Swarm Arena comment, `UInt32` blackboard key in the Public
  Burst Nodes sample, untested managed nodes, unseeded `System.Random` in the stdlib reference twin).
