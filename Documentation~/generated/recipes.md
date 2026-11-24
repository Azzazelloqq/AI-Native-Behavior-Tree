# AIBT MCP recipes (generated)

Verification evidence is recorded under `Planning~/Evidence/P6-011/`, for the updated node-compilation protocol `Planning~/Evidence/P7-031/`, and for the native node library recipes `Planning~/Evidence/P11-002/` (in the AIBT repository only, not in the package). Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command.

## Recipe: create and validate a tree

Goal: create a new tree with one root node, add a child, then validate it.

```json
1) aibt_create_tree {"treeId": "tree.my-tree", "name": "My Tree", "path": "MyTree.aibt.json",
   "rootNodeJson": "{\"id\":\"root\",\"typeId\":\"aibt.core.memory-sequence\",\"typeVersion\":1}"}
   -> {"accepted": true, "contentHash": "<hash>", "path": "MyTree.aibt.json", "diagnostics": []}

2) aibt_add_node {"treeId": "tree.my-tree", "expectedHash": "<hash from step 1>", "parentId": "root",
   "nodeJson": "{\"id\":\"child-1\",\"typeId\":\"aibt.core.memory-sequence\",\"typeVersion\":1}"}
   -> {"accepted": true, "contentHash": "<new hash>", ...}

3) aibt_validate {"treeId": "tree.my-tree"}
   -> {"valid": true, "policyApplied": <bool>, "diagnostics": []}
```

Always use the `contentHash` the *previous accepted call* returned as the next call's `expectedHash` -- never assume a fixed increment (`ADR-P6-002`).

## Recipe: generate, compile, and apply a custom node

Goal: scaffold a new Burst condition node, compile it for real, and persist it into the project. The full P6-009 gate.

```json
1) aibt_generate_node {"kind": "condition", "typeId": "aibt.myproject.my-condition", "ns": "MyProject.Nodes",
   "summary": "...", "category": "MyProject/Conditions", "whenToUse": "...", "whenNotToUse": "...",
   "blackboardReadKey": "someKey", "blackboardReadType": "Bool"}
   -> stages the node into the single reserved staging slot; overwrites any prior pending generation.

2) aibt_preview_node_diff {}
   -> returns the exact staged file content -- never mutates or compiles anything.

3) aibt_generate_node_tests_and_manifest {}
   -> stages threshold-contract success/failure cases consumed by native test_node; custom cases may be supplied explicitly.

4) aibt_analyze_and_compile_node {"mode": "start"}
   -> {"status": "pending", "attemptId": "<id>"}; requests a new compilation of the captured staged content.

5) aibt_analyze_and_compile_node {"mode": "check", "attemptId": "<id from step 4>"}
   -> repeat while status is 'pending'/'still-compiling', including across domain reload; 'compiled' returns contentHash, 'failed' returns diagnostics. Changed staging or an expired attempt requires a fresh start.

6) aibt_test_node {"expectedContentHash": "<contentHash from step 5>"}
   -> checks metadata and dispatchProven separately from behaviorProven; any supplied behavior-case mismatch makes valid=false.

7) aibt_apply_node {"expectedContentHash": "<contentHash from step 5>", "destinationPath": "MyProject/GeneratedNodes/MyCondition"}
   -> the only step that persists into the real project; re-verifies the hash and re-runs the checks itself first.
```

## Recipe: run a scheduling benchmark

Goal: measure the fixed per-tick overhead of the smallest possible tree under the `immediate` policy.

```json
aibt_run_benchmark {"scenario": "scheduling-baseline-empty-job", "agentCount": 4, "policy": "immediate"}
-> {"scenario": "scheduling-baseline-empty-job", "agentCount": 4, "policy": "immediate",
    "totalSteps": <n>, "elapsedMicroseconds": <raw measured number>, ...}
```

No threshold or performance claim is attached to the result -- it is a raw measurement (`P4-002`'s own discipline).

## Recipe: run a behavior-case test

Goal: run a `.aibtcase.json` fixture through the real headless behavior-case runner.

```json
aibt_run_tests {"casePath": "AIBT/Tests/Editor/Mcp/Testing/Fixtures/success-then-running.aibtcase.json"}
-> {"success": true, "executedStepCount": 1, "inputDiagnostics": [], "failures": []}
```

## Recipe: find native library nodes and their bindings

Goal: find the ready-made native nodes (`aibt.native.*`, shard `aibt.native.standard`), the blackboard keys each one binds, and a runtime catalog that declares them.

```json
1) aibt_search_nodes {"keyword": "aibt.native", "offset": 0, "count": 50}
   -> {"totalCount": 33, "entries": [{"typeId": "aibt.native.bool-is-true", "category": "Native/Blackboard",
       "kind": "condition", "parameters": {}, "reads": [], "writes": [], "executionDomain": "burst", ...}, ...]}

2) aibt_get_node_contract {"typeId": "aibt.native.set-deadline"}
   -> {"found": true, "manifest": {"parameters": {"duration-microseconds": {"type": "uint32", ...}}, "reads": [], "writes": [], ...},
       "behaviorCaseContract": {"configuration": [{"id": "duration-microseconds", "type": "UInt32"}],
                                "inputs": [{"id": "key", "type": "Int64"}], "effects": []}}

3) aibt_runtime_catalogs {}
   -> {"catalogs": [{"typeName": "AIBT.Samples.NativeGuard.NativeGuardCatalog",
       "leafTypeIds": ["aibt.native.bool-is-true", ..., "aibt.native.wait-until-true"], "problems": []}, ...]}
```

The categories are `Native/Blackboard`, `Native/Flow`, `Native/Timing`, `Native/Signals` and `Native/Random`. A native node's blackboard keys are not in the manifest's `reads`/`writes`: they are its `bindings`, listed in `behaviorCaseContract.inputs` (member id and value type; read and written members alike, without the direction). `effects` names an effect channel (the node `aibt.native.signal` emits on the channel `aibt.native.signal`). These nodes run only on native hosts (`NativeTreeAgent`, `ProductionTreeHost`, `TreeSimulation`), never in `BehaviorTreeAgent`. Preparing a tree needs a catalog whose `leafTypeIds` contain every `aibt.native.*` type the tree uses: a project `[AibtCatalogSet]` that lists `typeof(AIBT.StandardNodes.StandardNativeShard)` (the Native Guard sample's catalog above is one).

## Recipe: build a native tree from library nodes

Goal: from nothing to a prepared runtime asset of a tree that uses only `aibt.core.*` and `aibt.native.*` nodes: Tree-scope keys of five types, a repeater at the root that keeps one run (and the keys its nodes write) going, a guard under a reactive composite, a cooldown shared by two branches, counters, signals, a random pick and waits. The tree also carries descriptions and snake_case key names because the verifying project's policy requires them.

```json
1) aibt_create_tree {"treeId": "recipes.native-sentry", "name": "Native Sentry", "path": "AibtRecipeScratch/native-sentry.aibt",
   "description": "A sentry built only from aibt.core and aibt.native nodes.", "formatVersion": 2,
   "rootNodeJson": "{\"id\":\"sentry_loop\",\"typeId\":\"aibt.core.memory-sequence\",\"typeVersion\":1,\"description\":\"Run the sentry round after round (a strike, a warning or a patrol), up to 1000000 rounds in one run, so the cooldown and the counters stay.\"}",
   "blackboardJson": "[{\"id\":\"target_seen\",\"valueType\":\"Bool\",\"description\":\"Fact: the game sees a target.\"},
                       {\"id\":\"gate_open\",\"valueType\":\"Bool\",\"description\":\"Fact: the gate is open.\"},
                       {\"id\":\"self_position\",\"valueType\":\"Float3\",\"description\":\"Fact: the sentry position.\"},
                       {\"id\":\"target_position\",\"valueType\":\"Float3\",\"description\":\"Fact: the target position.\"},
                       {\"id\":\"ready_at\",\"valueType\":\"Int64\",\"description\":\"Shared cooldown: the host time (microseconds) when the weapon is ready again.\"},
                       {\"id\":\"shots\",\"valueType\":\"Int32\",\"description\":\"Strikes in this run.\"},
                       {\"id\":\"spot\",\"valueType\":\"Int32\",\"description\":\"The patrol spot picked at random (0 to 2).\"},
                       {\"id\":\"fatigue\",\"valueType\":\"Float32\",\"description\":\"Grows by 0.25 with every finished patrol in this run.\"}]"}
   -> {"accepted": true, "contentHash": "<hash>", "path": "<project>/Assets/AibtRecipeScratch/native-sentry.aibt",
       "diagnostics": [<one AIBT2053 info per key: no node binds a key yet>]}

2) aibt_add_node {"treeId": "recipes.native-sentry", "expectedHash": "<hash from step 1>", "parentId": "sentry_loop",
   "nodeJson": "{\"id\":\"sentry\",\"typeId\":\"aibt.core.reactive-selector-retaining\",\"typeVersion\":1,\"description\":\"One round: strike, warn or patrol; the first branch that can run wins.\"}"}
   -> {"accepted": true, "contentHash": "<new hash>", "diff": {"entries": [{"nodeId": "sentry", "kind": "Added"}, {"nodeId": "sentry_loop", "kind": "Changed"}]}, ...}

3) aibt_add_node {"treeId": "recipes.native-sentry", "expectedHash": "<hash from step 2>", "parentId": "sentry",
   "nodeJson": "{\"id\":\"strike\",\"typeId\":\"aibt.core.reactive-sequence\",\"typeVersion\":1,\"description\":\"Guard: strike while the target is close and the weapon is ready.\"}"}
   -> {"accepted": true, "contentHash": "<new hash>", ...}

4) aibt_add_node {"treeId": "recipes.native-sentry", "expectedHash": "<hash from step 3>", "parentId": "strike",
   "nodeJson": "{\"id\":\"target_close\",\"typeId\":\"aibt.native.float3-within-distance\",\"typeVersion\":1,\"description\":\"Is the target within 3 m (ignoring height)?\",\"parameters\":{\"distance\":3.0,\"flat\":true},\"bindings\":{\"a\":\"self_position\",\"b\":\"target_position\"}}"}
   -> {"accepted": true, "contentHash": "<new hash>", ...}

5) aibt_apply_domain_patch {"treeId": "recipes.native-sentry", "expectedHash": "<hash from step 4>",
   "operationsJson": "<the operations array below, as one JSON string>"}
   -> {"accepted": true, "contentHash": "<new hash>", "diff": {"entries": [<31 entries>]}, ...}

6) aibt_validate {"treeId": "recipes.native-sentry"}
   -> {"valid": true, "policyApplied": true, "diagnostics": [<four AIBT2053 infos: gate_open, self_position, target_position, target_seen>]}

7) aibt_compile {"treeId": "recipes.native-sentry"}
   -> {"success": false, "contentHash": null, "diagnostics": [{"code": "AIBT3012", "severity": "error",
       "message": "The selected node type has no Phase 1 reference-handler binding.", "nodeId": "target_close", ...}, ...]}

8) aibt_inspect_tree {"treeId": "recipes.native-sentry"}
   -> {"tree": {...}, "contentHash": "<hash from step 5>", "sourcePath": "...", "registryProblems": [], "referenceAvailable": false}

9) aibt_prepare_runtime {"treeId": "recipes.native-sentry", "expectedHash": "<hash from step 8>",
   "catalog": "AIBT.Samples.NativeGuard.NativeGuardCatalog", "outputPath": "Assets/AibtRecipeScratch/native-sentry.runtime.asset", "dryRun": true}
   -> {"success": true, "backend": "native-generated-v2", "existingAssetHash": null, "dryRun": true, ...}

10) aibt_prepare_runtime {... the same arguments without dryRun ...}
   -> {"success": true, "backend": "native-generated-v2", "outputPath": "Assets/AibtRecipeScratch/native-sentry.runtime.asset", "dryRun": false, ...}
```

The operations of step 5 (in the CLI's argument file they are the `operations` array itself):

```json
[
  {"op": "replace", "nodeId": "sentry_loop", "newTypeId": "aibt.core.repeater", "newParameters": {"count": 1000000, "stopOnFailure": false}},
  {"op": "add", "parentId": "strike", "node": {"id": "strike_ready", "typeId": "aibt.native.deadline-passed", "typeVersion": 1, "description": "Is the shared cooldown over?", "bindings": {"key": "ready_at"}}},
  {"op": "add", "parentId": "strike", "node": {"id": "strike_fire", "typeId": "aibt.core.memory-sequence", "typeVersion": 1, "description": "Start the cooldown, tell the game, count the strike."}},
  {"op": "add", "parentId": "strike_fire", "node": {"id": "strike_cooldown", "typeId": "aibt.native.set-deadline", "typeVersion": 1, "description": "Weapon ready again in 2 s.", "parameters": {"duration-microseconds": 2000000}, "bindings": {"key": "ready_at"}}},
  {"op": "add", "parentId": "strike_fire", "node": {"id": "strike_signal", "typeId": "aibt.native.signal", "typeVersion": 1, "description": "Signal 1: strike.", "parameters": {"code": 1}}},
  {"op": "add", "parentId": "strike_fire", "node": {"id": "strike_count", "typeId": "aibt.native.increment-int", "typeVersion": 1, "description": "One more strike.", "parameters": {"amount": 1}, "bindings": {"key": "shots"}}},
  {"op": "add", "parentId": "sentry", "node": {"id": "warn", "typeId": "aibt.core.reactive-sequence", "typeVersion": 1, "description": "Guard: warn while a target is seen and the weapon is ready."}},
  {"op": "add", "parentId": "warn", "node": {"id": "target_seen_check", "typeId": "aibt.native.bool-is-true", "typeVersion": 1, "description": "Is a target seen?", "bindings": {"key": "target_seen"}}},
  {"op": "add", "parentId": "warn", "node": {"id": "warn_ready", "typeId": "aibt.native.deadline-passed", "typeVersion": 1, "description": "Is the shared cooldown over?", "bindings": {"key": "ready_at"}}},
  {"op": "add", "parentId": "warn", "node": {"id": "warn_fire", "typeId": "aibt.core.memory-sequence", "typeVersion": 1, "description": "Start the shared cooldown, tell the game."}},
  {"op": "add", "parentId": "warn_fire", "node": {"id": "warn_cooldown", "typeId": "aibt.native.set-deadline", "typeVersion": 1, "description": "Shared cooldown: 2 s.", "parameters": {"duration-microseconds": 2000000}, "bindings": {"key": "ready_at"}}},
  {"op": "add", "parentId": "warn_fire", "node": {"id": "warn_signal", "typeId": "aibt.native.signal", "typeVersion": 1, "description": "Signal 2: warning shot.", "parameters": {"code": 2}}},
  {"op": "add", "parentId": "sentry", "node": {"id": "patrol", "typeId": "aibt.core.memory-sequence", "typeVersion": 1, "description": "Pick a spot, report it, wait for the gate, rest, get tired."}},
  {"op": "add", "parentId": "patrol", "node": {"id": "pick_spot", "typeId": "aibt.native.random-int", "typeVersion": 1, "description": "Pick spot 0, 1 or 2.", "parameters": {"min": 0, "max": 2}, "bindings": {"key": "spot"}}},
  {"op": "add", "parentId": "patrol", "node": {"id": "report_spot", "typeId": "aibt.core.memory-selector", "typeVersion": 1, "description": "Tell the game which spot: signal 10, 11 or 12."}},
  {"op": "add", "parentId": "report_spot", "node": {"id": "spot_0", "typeId": "aibt.core.memory-sequence", "typeVersion": 1, "description": "Spot 0."}},
  {"op": "add", "parentId": "spot_0", "node": {"id": "is_spot_0", "typeId": "aibt.native.int-equals", "typeVersion": 1, "description": "Spot 0?", "parameters": {"value": 0}, "bindings": {"key": "spot"}}},
  {"op": "add", "parentId": "spot_0", "node": {"id": "signal_spot_0", "typeId": "aibt.native.signal", "typeVersion": 1, "description": "Signal 10: go to spot 0.", "parameters": {"code": 10}}},
  {"op": "add", "parentId": "report_spot", "node": {"id": "spot_1", "typeId": "aibt.core.memory-sequence", "typeVersion": 1, "description": "Spot 1."}},
  {"op": "add", "parentId": "spot_1", "node": {"id": "is_spot_1", "typeId": "aibt.native.int-equals", "typeVersion": 1, "description": "Spot 1?", "parameters": {"value": 1}, "bindings": {"key": "spot"}}},
  {"op": "add", "parentId": "spot_1", "node": {"id": "signal_spot_1", "typeId": "aibt.native.signal", "typeVersion": 1, "description": "Signal 11: go to spot 1.", "parameters": {"code": 11}}},
  {"op": "add", "parentId": "report_spot", "node": {"id": "signal_spot_2", "typeId": "aibt.native.signal", "typeVersion": 1, "description": "Signal 12: go to spot 2.", "parameters": {"code": 12}}},
  {"op": "add", "parentId": "patrol", "node": {"id": "wait_gate", "typeId": "aibt.native.wait-until-true", "typeVersion": 1, "description": "Wait up to 3 s for the gate; if it stays closed, the patrol fails and the next round starts.", "parameters": {"timeout-microseconds": 3000000}, "bindings": {"key": "gate_open"}}},
  {"op": "add", "parentId": "patrol", "node": {"id": "rest", "typeId": "aibt.native.wait-random-microseconds", "typeVersion": 1, "description": "Rest 1 to 2 s.", "parameters": {"min-microseconds": 1000000, "max-microseconds": 2000000}}},
  {"op": "add", "parentId": "patrol", "node": {"id": "tire", "typeId": "aibt.native.increment-float", "typeVersion": 1, "description": "A little more tired: fatigue + 0.25.", "parameters": {"amount": 0.25}, "bindings": {"key": "fatigue"}}},
  {"op": "add", "parentId": "patrol", "node": {"id": "report_tired", "typeId": "aibt.core.succeeder", "typeVersion": 1, "description": "The patrol succeeds, tired or not."}},
  {"op": "add", "parentId": "report_tired", "node": {"id": "tired", "typeId": "aibt.core.memory-sequence", "typeVersion": 1, "description": "Tell the game when the sentry is tired."}},
  {"op": "add", "parentId": "tired", "node": {"id": "is_tired", "typeId": "aibt.native.float-at-least", "typeVersion": 1, "description": "Is fatigue at least 1 (four finished patrols)?", "parameters": {"threshold": 1.0}, "bindings": {"key": "fatigue"}}},
  {"op": "add", "parentId": "tired", "node": {"id": "signal_tired", "typeId": "aibt.native.signal", "typeVersion": 1, "description": "Signal 20: tired.", "parameters": {"code": 20}}}
]
```

What each step relies on:
- The tree must be version 2. `create_tree` writes version 2 unless it is given `formatVersion: 1` (the recipe states 2 anyway); in a version-1 tree every later `add_node` of a node with `bindings` is rejected with AIBT2042 ("Generated bindings and Agent/Shared scopes require tree format version 2"). `set_blackboard_keys` keeps version 2.
- The repeater at the root keeps the run going. On `NativeTreeAgent` a run ends when the root finishes, and every run is a new host that starts from the tree's key defaults (facts the game set are applied again). Without the loop the root would finish with every patrol, and `ready_at`, `shots` and `fatigue` would start again from 0: the next strike could come at once, before the 2 s are over. The repeater (`count` 1000000, `stopOnFailure` false) runs `sentry` round after round inside one run, so the keys that nodes write last across rounds, and a failed round (the gate stayed closed) does not stop it. After 1000000 rounds the run ends and the agent starts a new one from the defaults.
- `create_tree` refuses a decorator root without its child (AIBT2023 "Node child count violates its manifest child policy"), so the root starts as a `memory-sequence` and the first operation of the patch turns it into the repeater once `sentry` is its child. `replace` keeps the node id, its description and its children.
- `bindings` maps each member of the node's `behaviorCaseContract.inputs` to a declared key of the same value type: Bool, Int32, Float32, Float3, Int64 (a deadline is microseconds of host time). A missing, undeclared or wrongly typed binding is rejected with AIBT3018 and AIBT3015. `parameters` are the manifest's `parameters` (UInt32, Float32, Bool).
- The guard: a condition is the first child of a reactive composite and is checked again at every update. Native hosts do not run observer re-evaluation. A plain (v1) `reactive-sequence`/`reactive-selector` aborts and re-enters its running child at every update, so waits and multi-step branches go under the `-retaining` variants, as `sentry` does with `patrol`.
- The shared cooldown: both branches check `ready_at` with `deadline-passed` and restart it with `set-deadline`. A strike or a warning ends its round; in the next rounds neither guard passes until the 2 s are over, so the sentry patrols, and `sentry` interrupts the patrol with the next strike or warning when the cooldown is over.
- The counters: `shots` counts strikes. `fatigue` grows by 0.25 with every patrol that finishes (an interrupted or failed patrol does not count); from the fourth finished patrol on, `float-at-least` passes and every finished patrol sends signal 20. `succeeder` keeps the patrol a success while the sentry is not tired.
- The random pick: game code cannot read a Tree-scope key from `NativeTreeAgent`, so the tree reports `spot` through `int-equals` branches and signals. Hosts seed random streams (seed setting, default 0).
- Diagnostics: AIBT2053 ("no node in this tree writes it") is an info note on an input key, a key the game feeds: here `target_seen`, `gate_open`, `self_position` and `target_position`. A key a native node writes through a Write or ReadWrite member of `bindings` (here `ready_at`, `shots`, `spot`, `fatigue`) has no note; `NativeTreeAgent.SetFact` refuses exactly those keys.
- `aibt_compile` and `aibt_simulate` are the Phase 1 reference backend: `compile` fails with AIBT3012 on the first native node, and `simulate` refuses every native node with AIBT2021. `aibt_run_tests` (behavior cases) fails with a `FactoryFault`. They do not execute native nodes. The native compile happens inside `aibt_prepare_runtime`.
- `aibt_prepare_runtime` with a catalog that does not declare a node fails with AIBT3012 "Selected native catalog does not declare node 'aibt.native.int-equals'. It is a package node of shard aibt.native.standard: add typeof(AIBT.StandardNodes.StandardNativeShard) to your catalog set ([AibtCatalogSet])." `catalog` is the full type name from `aibt_runtime_catalogs` (a short name is refused with AIBT9018). Create the output folder first.

The same steps with the CLI (`aibt` as in `Documentation~/cli.md`; the argument files hold the bridge JSON objects, with `rootNode`, `blackboard`, `node` and `operations` as nested JSON rather than strings):

```powershell
aibt --permissions SemanticEdit --json call create_tree --args-file create.json
aibt --permissions SemanticEdit --json call add_node --args-file add-sentry.json
aibt --permissions SemanticEdit --json call add_node --args-file add-strike.json
aibt --permissions SemanticEdit --json call add_node --args-file add-close.json
aibt --permissions SemanticEdit --json call apply_domain_patch --args-file patch.json
aibt --permissions Compilation --json tree validate recipes.native-sentry
aibt --json tree inspect recipes.native-sentry
aibt --permissions Compilation --json tree prepare recipes.native-sentry --catalog AIBT.Samples.NativeGuard.NativeGuardCatalog --output Assets/AibtRecipeScratch/native-sentry.runtime.asset --expected-hash "<contentHash>" --dry-run
aibt --permissions Compilation --json tree prepare recipes.native-sentry --catalog AIBT.Samples.NativeGuard.NativeGuardCatalog --output Assets/AibtRecipeScratch/native-sentry.runtime.asset --expected-hash "<contentHash>"
```

To run it: put the asset in the `Tree` field of a `NativeTreeAgent` (no game code is needed for the tree to start); the game feeds `target_seen`, `gate_open`, `self_position` and `target_position` with `NativeTreeAgent.SetFact` and handles codes 1, 2, 10, 11, 12 and 20 with `OnEffect("aibt.native.signal", (int code) => ...)`. With the target close and the gate open, signal 1 comes every 2 s and signal 20 from the fourth finished patrol on, all in one run. In Play Mode the AIBT Graph shows the running branch of a selected agent. `aibt_runtime_hosts` lists the agent's host, and `aibt_read_trace` reads it by that `instanceId`.

## Recipe: change a native tree and prepare it again

Goal: edit a prepared native tree and replace its runtime asset, keeping the asset's GUID.

```json
1) aibt_configure_node {"treeId": "recipes.native-sentry", "expectedHash": "<current hash>", "nodeId": "strike_cooldown",
   "parameterName": "duration-microseconds", "valueJson": "2500000"}
   -> {"accepted": true, "contentHash": "<new hash>", ...}

2) aibt_prepare_runtime {"treeId": "recipes.native-sentry", "expectedHash": "<hash from step 1>", "catalog": "AIBT.Samples.NativeGuard.NativeGuardCatalog",
   "outputPath": "Assets/AibtRecipeScratch/native-sentry.runtime.asset", "dryRun": true}
   -> {"success": true, "existingAssetHash": "<asset hash>", "dryRun": true, ...}

3) aibt_prepare_runtime {... the same arguments without dryRun ..., "overwrite": true, "expectedAssetHash": "<asset hash from step 2>"}
   -> {"success": true, "dryRun": false, ...}
```

Without `overwrite` and the asset hash the call is refused with AIBT9018 "Overwrite requires overwrite=true and the current expectedAssetHash (obtain it with dryRun)."; the asset is not touched. `configure_node` and `replace_node` keep a node's `bindings` (`replace_node` keeps them under the same member names). No tool changes `bindings` in place (`configure_node` with `parameterName` `bindings` is rejected with AIBT2025): rebind a node with one `aibt_apply_domain_patch` that removes it and adds it again with the new `bindings`. A catalog set that lists `StandardNativeShard` changes its fingerprint whenever the shard gains nodes, so after a package update prepare its assets again.
