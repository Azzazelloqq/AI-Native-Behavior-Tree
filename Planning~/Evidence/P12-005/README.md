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

## Acceptance (orchestrator, 2026-11-25, Unity 7000.0.0a8)

Accepted on `1bed4a4` as part of the batch `8396202..1bed4a4` (the P12-002 review follow-up and P12-003 to
P12-008); the status stays `Review` as for every item of phases 11 and 12.

- **Diff read.** No file under `CodeGen~`, `Tests/Fixtures` or `Documentation~/specifications` changed, nor
  `ExpectedPublicAbiV2.txt` or `package.json`; no existing public signature changed. Runtime code: one
  comparison that was false for every value removed (`NativePipelinedPhaseControllerV1.cs`, P12-005), a
  version-conditional call with the same result (`McpRuntimeTools.cs`), `new` on `BtNodeAttribute.TypeId`, and
  doc comments.
- **Full run of the ten assemblies on `1bed4a4`, 23:41-23:49:** Runtime 821/821, Integration 792 passed and
  2 skipped (the two opt-in benchmarks), Editor 917/917, BehaviorCases 65/65, NativeBurstDispatch 79/79,
  NativeTreeAgentBlackboard 33/33, NativeSharedBlackboard 36/36, CodeGen.ContractTests 6/6,
  CodeGen.GenerationTests 14/14, PlayMode 2/2; no console noise. Raw:
  `C:\UnityProjects\_aibt_acceptance\P12-batch\full-run-1bed4a4-unity7.txt`. The run before it, on `dccab95`
  (`full-run-dccab95-unity7.txt`), had two failures, both in tests the batch had not touched and both caused by
  the test on CoreCLR, not by the package: P12-007 and P12-008.
- **Independent read** of `8396202..dccab95` (a reader given the commits and neutral questions; it ran
  nothing): `C:\UnityProjects\_aibt_acceptance\P12-batch\independent-review.md`. No blocker; its should-fix
  findings and weak tests were taken in `3caa473`, `892cc6c`, `a1b1c1a` and `1bed4a4`. Those four commits and
  the two test fixes `7595b51` and `45dd89f` were read by the orchestrator, not by a second reader.
- **Cold Burst cache (P12-004)** was run by the orchestrator: the Editor started with `Library/BurstCache/JIT`
  moved aside and `BurstExecutionPathTests` as the first run; old test 1/3, changed test 3/3. Raw:
  `C:\UnityProjects\_aibt_acceptance\P12-004\cold-A-old-test.txt`, `cold-B-fixed-test.txt`.
- **What the acceptance does not cover.** Nothing here was compiled or run on 6000.x: Modules moved to
  7000.0.0a8 on 2026-11-25 and every earlier record is from 6000.6.9, so the two sets of evidence are from
  different Editors. No Player (IL2CPP) run. The tooling changed with the move (Pipeline package 1.0.0, the
  `Tools/Unity` scripts of the parent repository), so this is also the first full run through that tooling.
