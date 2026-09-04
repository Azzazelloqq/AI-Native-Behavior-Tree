# AIBT MCP recipes (generated)

Verification evidence is recorded under `Planning~/Evidence/P6-011/` and, for the updated node-compilation protocol, `Planning~/Evidence/P7-031/`. Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command.

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
   -> stages a paired test scaffold (an honest placeholder pending P6-022).

4) aibt_analyze_and_compile_node {"mode": "start"}
   -> {"status": "pending", "attemptId": "<id>"}; requests a new compilation of the captured staged content.

5) aibt_analyze_and_compile_node {"mode": "check", "attemptId": "<id from step 4>"}
   -> repeat while status is 'pending'/'still-compiling', including across domain reload; 'compiled' returns contentHash, 'failed' returns diagnostics. Changed staging or an expired attempt requires a fresh start.

6) aibt_test_node {"expectedContentHash": "<contentHash from step 5>"}
   -> checks metadata and registry materialization; dispatchProven reports whether native dispatch was exercised for the supported binding types.

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
