# AIBT MCP anti-patterns (generated)

Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command.

## Don't expect `aibt_simulate` to inject events, completions, or drive resume/abort/step-budget.

The driver assigns `updateId`/`snapshotRevision` itself sequentially starting at 1 and exposes no injection API at all -- only plain `update` steps are supported. A step requesting anything else is rejected with a structured diagnostic (`P6-007`; widening this is `P6-013`, still `Draft`).

## Don't confuse reference simulation with live native trace.

Use runtime_hosts and read_trace for a specific native instance/program. Trace reads never complete jobs; available=false means retry later, complete=false means dropped/faulted history. Reference simulation is a separate backend. No compare-trace operation is exposed. For simulation-container agents use runtime_simulations, describe_agent and read_agent_trace; they see only containers the game registered and agents it traces.

## Don't assume a fixed revision increment for a domain patch.

`TreeDocument.Revision` is never persisted to `*.aibt.json`; every MCP call reloads a tree fresh from disk. Mutating calls are checked against a computed content hash instead (`ADR-P6-002`). Always use the `contentHash`/`expectedHash` the last *accepted* call actually returned, never a value you computed or assumed.

## Don't assume scope support bypasses contracts and policy.

Blackboard tools accept built-in scalar keys; Agent/Shared require v2, canonical defaults, matching contracts and enabled project policy. Enum32/Registered authoring is not exposed by these tools. Phase1 scope defaults remain false.

