# P12-005 evidence — compiler warnings every consumer sees

Modules on Unity 7000.0.0a8, 2026-11-25. The edits are as in the card:

- `MCP/McpRuntimeTools.cs`;
- `Agents/Nodes/BtAttributes.cs`;
- `Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs`.

## Compile

- **By the orchestrator, before the P12-004 cold runs.** The working tree with these edits compiled on
  7000.0.0a8 with no errors. After the rebuild of `AIBT.Runtime`, `AIBT.Agents` and `AIBT.Mcp`, the log has
  no CS0618, CS0114 or CS0652 from them. That log was overwritten by the Editor's next start, and the line is
  taken from the orchestrator's message.
- **In this session.** The Editor started at 22:54:41 with these edits already built. Its log has no warning
  from `Assets\AIBT` before 23:05. One recompile after a doc-comment edit in
  `AIBT.Integration.Tests` (23:05, `Tundra build success (11.64 seconds), 5 items updated`) logged exactly
  the 22 known CS0728 lines:
  - 14 in `TreeSimulationIoTests.cs`;
  - 8 in `TreeSimulationActionTests.cs`.

  These are the test-assembly follow-up of the card; nothing else from `Assets\AIBT` was logged. `AIBT.Runtime`,
  `AIBT.Agents` and `AIBT.Mcp` were not rebuilt in this session, so for them the orchestrator's compile above
  is the evidence.
- **6000.x was not compiled.** Modules can no longer open it. The claim for 6000.x rests on:
  - the metadata read of the 6000.6.9f1 assemblies;
  - the `UNITY_6000_6_OR_NEWER` define list.

  The package still declares `"unity": "6000.0"`, and no 6000.0 Editor is installed (see the card).

## Tests

All runs used `Tools/Unity/Invoke-UnityTests.ps1 -Filter <classes>`, one run, 2026-11-25 about 23:01:

```
testName NativePipelinedPhaseControllerTests 17/17 (failed=0 skipped=0)
testName NativeBatchedLifecycleOwnerTests 13/13 (failed=0 skipped=0)
testName NativeExecutionEquivalenceTests 14/14 (failed=0 skipped=0)
testName PipelinedJobsCadenceTests 5/5 (failed=0 skipped=0)
testName McpRuntimeNativeAgentTests 3/3 (failed=0 skipped=0)
testName AgentConsumerToolsTests 14/14 (failed=0 skipped=0)
testName ManagedAgentLayerTests 24/24 (failed=0 skipped=0)
testName BehaviorTreeAgentComponentTests 6/6 (failed=0 skipped=0)
```

The Console errors of this run are the two that
`BehaviorTreeAgentComponentTests.ANodeExceptionRaisesFaultedExactlyOnceAndStopsTheTree` raises on purpose
(`InvalidOperationException: node failure`, `AIBT4003`). That test passed.

## Not verified

- **The compile of `AIBT.Runtime`, `AIBT.Agents` and `AIBT.Mcp` with these edits on 7000.0.0a8 is relayed.**
  It comes from the orchestrator's message; its log was overwritten, and no rebuild of those three assemblies
  was seen in this session.
- **The CS0114 claims in the card have no kept artefact:** the netstandard2.1 build that shows the warning,
  and the two deterministic builds with the same MD5. They were made in scratch folders that were not kept.
- A compile on 6000.0 or 6000.6.
- The test-assembly warnings: CS0618 in `LargeGraphPerformanceTests.cs` 73–74, and CS0728 × 22. They are
  follow-ups, as decided in the card.
- The full suite. The orchestrator runs it after this report.
