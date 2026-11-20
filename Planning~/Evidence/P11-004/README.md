# P11-004 evidence — reading a Tree-scope value back from a running native tree

Branch `feature/aibt-consumer-workflow`, base `0038a3b`. Raw files: `C:\UnityProjects\_aibt_acceptance\P11-004\`.
Unity 6000.6.9f1, the Modules Editor (Pipeline port 7810). Status: accepted by the orchestrator on `8d03467` ("Acceptance run" at the end); the item is in `Review`.

## Commits

| Commit | What |
| --- | --- |
| `2ae7e06` | ADR-P11-004 proposed |
| `0ca665c` | ADR accepted with the orchestrator's three changes; `AIBT-051` row in `decisions.md` |
| `c8ca9f3` | code: host, agent, adapter (additions only) |
| `75eab34` | tests: host and agent reads |
| `aded2f9` | docs: quickstart section, production-host paragraph, CHANGELOG, API reference |
| this commit | task card, work item, MASTER_PLAN, this evidence |

## What changed (`git diff --numstat 0038a3b aded2f9`: 1287 insertions, 0 deletions)

| File | + | − |
| --- | --- | --- |
| `Runtime/Integration/ProductionTreeHost.cs` | 125 | 0 |
| `Runtime/Integration/NativeTreeAgent.cs` | 119 | 0 |
| `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs` | 125 | 0 |
| `Tests/Integration/NativeRuntime/ExternalTreeBlackboardReadTests.cs` (+ .meta) | 356 (+2) | 0 |
| `Tests/Integration/NativeRuntime/NativeTreeAgentValueReadTests.cs` (+ .meta) | 258 (+2) | 0 |
| `Tests/Integration/NativeRuntime/UserGuideValueReadSnippet.cs` (+ .meta) | 33 (+2) | 0 |
| `Tests/Editor/Documentation/UserGuideSnippetTests.cs` | 11 | 0 |
| `Documentation~/decisions/ADR-P11-004-native-tree-value-read.md` | 187 | 0 |
| `Documentation~/decisions.md` | 1 | 0 |
| `Documentation~/consumer-quickstart.md` | 33 | 0 |
| `Documentation~/production-host.md` | 11 | 0 |
| `Documentation~/generated/api-reference-runtime.md` | 5 | 0 |
| `CHANGELOG.md` | 17 | 0 |

No line was deleted from any existing file (`git diff 0038a3b HEAD -- Runtime | grep -c '^-[^-]'` = 0).

Public additions: `ProductionTreeHost.ExternalTreeReadHandle<T>`, `CanReadTreeValues`,
`TryResolveExternalTreeRead<T>`, `TryReadExternalTreeValue<T>`; `NativeTreeAgent.TryGetValue<T>`,
`TryGetVector3`. Internal: `ProductionTreeHost.TryReadTreeValue` (D7) and `IsExecuting`;
`GeneratedTreeDispatchAdapterV2.TryResolveExternalTreeRead<T>`, `TryReadExternalTreeValue<T>`,
`TryReadTreeValue`; `NativeTreeAgent.FactSlot<T>.Supported` / `BlackboardType`.

Small departures from the letter of ADR D9 (no semantic change): the adapter has three internal
methods, not two (the type-erased read is its own method); the host has one more internal member,
`IsExecuting`, so the agent can tell "executing" from "no run"; `FactSlot<T>` exposes its existing
type check to the read slots instead of duplicating it. The type-erased read can also return
`BlackboardInvalidValue` when stored bytes do not decode (an existing code, not in the D6 table).

The regenerated `api-reference-runtime.md` gained exactly 5 lines, the new public members; it is
pinned by `McpDocumentationGeneratorsTests.CommittedGeneratedFilesMatchAFreshRegeneration`, the one
expected change to a pinned file.

## Protected paths not touched

`git diff --stat 0038a3b HEAD -- Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt CodeGen~
Documentation~/specifications package.json Runtime/Compiled Runtime/Scheduling Schemas~ Samples~` is
empty. No existing member's body changed (0 deleted lines). No existing test expectation changed.
`Editor/Graph/` is untouched.

## Tests (EditMode, `AIBT.Integration.Tests` and `AIBT.Editor.Tests`, on `aded2f9`)

Raw results, one JSON per class: `C:\UnityProjects\_aibt_acceptance\P11-004\tests\<Class>.json`.

| Class | Result | New |
| --- | --- | --- |
| `ExternalTreeBlackboardReadTests` | 10/10 | yes |
| `NativeTreeAgentValueReadTests` | 8/8 | yes |
| `UserGuideSnippetTests` | 6/6 | 1 new case |
| `ExternalTreeBlackboardWriteTests` | 9/9 | no |
| `NativeTreeAgentTests` | 16/16 | no |
| `NativeTreeAgentRandomSeedTests` | 8/8 | no |
| `ProductionTreeWorldEffectTests` | 6/6 | no |
| `McpDocumentationGeneratorsTests` | 14/14 | no |

77 passed, 0 failed. New tests and the ADR rule each covers:

- Host: `BeforeTheFirstUpdate_EveryKeyReadsItsCompiledDefault` (D4; Int32, Float32, Bool, Float3);
  `NodeWrittenAndExternallyWrittenKeys_ReadTheirCurrentValues_AlsoAfterTheRootFinished` (D3, D4);
  `ResolveRefusals_UseTheExistingCodes` (D6: unknown key, type, size of `T`, empty key);
  `ResolveIsLegalWhileExecuting_ButTheReadIsRefusedAndTouchesNothing` (D5, held drive);
  `HandleOfAnotherHost_AndTheDefaultHandle_AreRefused`; `AfterStop_ReadsAreRefused`;
  `AfterAFault_TheStorageStaysReadable` (D4); `DelegateBasedHost_IsRefusedStructurally`;
  `HandleRead_RepeatedAfterWarmup_AllocatesNoManagedMemory` (D8, 1000 reads, `AllocatingGCMemory`);
  `TypeErasedRead_DecodesBuiltInValues_AndReportsFailures` (D7).
- Agent: `FactAndNodeWrittenKeys_ReadBack_AndAFinishedRunKeepsItsValuesWithRestartNever`;
  `WithRestartAlways_FinalValuesAreReadInCompleted_AndEachNewRunStartsFromDefaults` (3 runs, the Q1
  window asserted); `NoRun_ReturnsFalseWithoutLogging`; `AFaultedAgent_ReturnsFalse`;
  `WrongKeyOrType_IsLoggedOncePerKeyAndType_InAnyCallOrder` (orchestrator change 1);
  `ArgumentAndUnsupportedType_Throw`; `PipelinedHostBetweenFrames_IsRefused_WithOneWarningPerAgent`
  (orchestrator change 2; a real `PipelinedJobs` host through the scheduler, host and agent API);
  `TryGetValue_RepeatedAfterWarmup_AllocatesNoManagedMemory` (2000 reads: 0 bytes by
  `GC.GetAllocatedBytesForCurrentThread` and `AllocatingGCMemory`).
- Docs: `QuickstartValueReadIsQuotedFromACompiledFile`.

Found while testing: the first call of a freshly created measuring delegate allocated once inside
the agent test (first-call runtime cost); a diagnostic measured 0 bytes per call over 1000 calls for
every agent read, so the test warms the delegate once before measuring.

Console after the last recompile: no compile errors; no error from AIBT runtime code. The buffer
holds Editor-internal errors since the Editor started this session ("Release of invalid GC handle",
"Failed to load 'VirtualArtifacts/...'") and the expected logs of the tests above.

## Live check (Play Mode, Native Guard scratch scene)

A scratch `NativeValueProbe` (folder `Assets/AibtP11004Scratch/`, deleted afterwards; source kept as
`NativeValueProbe.cs.txt`) attached itself to the Native Guard agent and read, in `LateUpdate` of
every frame, `weapon-ready-at` (Int64, written by `aibt.native.set-deadline`), `sweep-spot` (Int32,
written by `aibt.native.random-int`, min 0, max 2; default -1), and the facts `intruder-seen`, `intruder-close`.

| Run | Frames | Reads ok | Refused | `weapon-ready-at` changes | `sweep-spot` changes | Frames `intruder-seen` true |
| --- | --- | --- | --- | --- | --- | --- |
| 1 (camera clip, 20 s) | 4172 | 16688 | 0 | 11 | 5 | 2355 |
| 2 (Game view clip, 15 s) | 2718 | 10872 | 0 | 9 | 5 | 1291 |

From `live-read-log.txt` (run 2): the deadline values are `0, 2790728, 4298345, 5810847, 0, 10658808,
12159262, 13662285, ...` (µs): steps of about 1.5 s, the node's `duration-microseconds` 1500000, and
back to 0 when a new run started. `sweep-spot` took -1, 0 and 1. The still
`native-guard-read-gameview.mp4.stills\t010.0s.png` shows the probe overlay ("weapon-ready-at (node)
= 12159262 us", "intruder-seen (fact) = True") next to the sample's own line "Facts:
intruder-seen=True". No error was logged during either run.

Files: `live-read-log.txt` (run 2), `live-read-log-run1-camera.txt`, `native-guard-read.mp4` and
`native-guard-read-gameview.mp4` with `.stills/` (camera capture does not include IMGUI, hence run 2).

## Not done / not verified

- The full run of all test assemblies (the orchestrator's).
- `BlackboardUnsupportedScope` is implemented and documented but not covered by a test: the generated
  v2 compiler refuses a tree with an Agent-scope key ("AIBT3017 ... Tree-scope blackboard memory
  ranges overlap", seen with a fixture that declared one), so no generated host can hold an
  Agent-scope slot today. That compiler refusal looks like a separate defect or limit; not
  investigated.
- PlayMode test assemblies were not run (the new tests are EditMode, driven through the real scheduler).
- The editor graph does not use `TryReadTreeValue` yet (next item).
- Reads from a worker thread are not guarded (main thread only, as every host member).
- Registered (non-built-in) value types are refused (D3); not exercised beyond the code path.
- Environment notes, not changed: `Tools/Unity/Start-UnityEditor.ps1` exits 1 under Windows PowerShell
  5.1 when `unity open` writes its plugin hint to stderr, although the Editor starts (a second call
  then waits for readiness); the `AIBT/MCP/Regenerate Documentation` menu writes into
  `Assets/AIBT-Documentation/` of the parent project (removed after copying `api-reference-runtime.md`),
  and its `node-catalog.md` differs from the committed one because it uses the project registry.

## Fix after the orchestrator's review (`ac4201c`)

The orchestrator's full run on `1eb2359` was green (815/815, 422/422, 813/814 with the known failure,
65/65, 79/79, 33/33, 36/36, 6/6, PlayMode 2/2). Its review found one defect: `ReadSlot<T>.TryRead`
built the refusal message (string concatenation and the code's `ToString`) on every refused call,
although `TryGetValue` logs it once per key, type and program. Fix: `TryRead` returns the refusal
code and `TryGetValue` formats the same text inside the log-once branch. The log text is unchanged.

New test `RefusedReads_AfterTheOneLog_AllocateNoManagedMemory`: after the one logged refusal per key
and type, 1000 × (read of an unknown key as `int` + read of the Int32 key `owned-target` as `float`).

| | GC.Alloc samples for 2000 refused reads | Raw result |
| --- | --- | --- |
| before the fix (`75eab34` code + the new test) | 10000 (5 per refused read), test failed | `tests\RefusedReads-before-fix.json` |
| after the fix (`ac4201c`) | 0, test passed | `tests\NativeTreeAgentValueReadTests-after-review-fix.json` |

After the fix: `NativeTreeAgentValueReadTests` 9/9, `ExternalTreeBlackboardReadTests` 10/10 (raw in
`C:\UnityProjects\_aibt_acceptance\P11-004\tests\*-after-review-fix.json`); recompile without errors.

Correction to "Tests" above: `GC.GetAllocatedBytesForCurrentThread` does not count in this Editor. A
probe measured 0 bytes for 1000 `new object()` where the profiler's GC.Alloc recorder counted 1000
samples (`tests\CounterProbe-GetAllocatedBytes-vs-Recorder.json`). So the "0 bytes" figures for
`TryGetValue_RepeatedAfterWarmup_AllocatesNoManagedMemory` and the "0 bytes per call" diagnostic
proved nothing by themselves. Both agent allocation tests now count GC.Alloc recorder samples (the
counter `AllocatingGCMemory` uses): warmed successful reads 0 samples for 2000 reads. Their
`AllocatingGCMemory` assertions were valid throughout. Why the unwarmed delegate allocated once in
the first version of that test is not known.

## Acceptance run (orchestrator, 2026-11-21)

Read by the orchestrator: the ADR against the code before the go-ahead (phase A), then the whole code
diff `0038a3b..8d03467` under `Runtime/`, the documentation diff, this file, the raw live logs and
one still of the Game view clip.

- **Scope.** Code outside tests: `Runtime/Integration/ProductionTreeHost.cs` (+125),
  `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs` (+125) and
  `Runtime/Integration/NativeTreeAgent.cs` (+119, then the review fix). Up to `1eb2359` no line of an
  existing file was deleted. Nothing changed under `CodeGen~`, `Schemas~`, `Samples~`, `Editor/`,
  `Authoring/`, `Runtime/Scheduling`, `Documentation~/specifications/`, and not `package.json` or
  `ExpectedPublicAbiV2.txt`. The regenerated `api-reference-runtime.md` gained the 5 new public members.
- **ADR.** Accepted by the orchestrator under the owner's approval of the task; the four questions
  were taken as the session recommended and none needs a protected change. The owner was told and
  may revise them.
- **Live logs recounted** by the orchestrator's own script from `C:\UnityProjects\_aibt_acceptance\P11-004\live-read-log.txt` and
  `live-read-log-run1-camera.txt`: 2718 frames, 10872 reads, 0 refused, 9 deadline changes, 5 spot
  changes, 1291 frames with `intruder-seen` true; and 4172 frames, 16688 reads, 0 refused, 11 and 5
  changes, 2355 frames. Steps between consecutive deadlines inside one run: 1500413-1512502 us (the
  node's duration is 1500000). They match the table above.
- **Still.** `native-guard-read-gameview.mp4.stills\t010.0s.png`: the probe overlay shows
  `weapon-ready-at (node) = 12159262 us`, `intruder-seen (fact) = True`, "reads ok 6540 refused 0";
  the sample's own line shows `intruder-seen=True`.
- **Full run of all nine assemblies** on `1eb2359`, 00:24-00:40; Editor-log counters did not move
  during the run (1 and 1 at the start and at the end). Raw output:
  `C:\UnityProjects\_aibt_acceptance\P11-004\acceptance\full-run-1eb2359.txt`.

| Assembly | `5094631` | `1eb2359` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | - |
| `AIBT.Integration.Tests` | 404/404 | 422/422 | `ExternalTreeBlackboardReadTests` 10, `NativeTreeAgentValueReadTests` 8 |
| `AIBT.Editor.Tests` | 812/813 | 813/814 | `UserGuideSnippetTests.QuickstartValueReadIsQuotedFromACompiledFile` |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | - |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | - |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | - |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | - |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | - |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | - |

The one failure is the known `ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog`.

- **Review finding and its fix.** One defect found by reading: a refused agent read built its message
  on every call (section "Fix after the orchestrator's review"). The fix `ac4201c` changes 13 lines of
  `NativeTreeAgent.cs` and the tests. After it the orchestrator ran `AIBT.Integration.Tests` alone on
  `8d03467`: 423/423, 00:45-00:58 (raw output `C:\UnityProjects\_aibt_acceptance\P11-004\acceptance\integration-8d03467.txt`).
  The other eight assemblies were NOT run again on `8d03467`; the next item's acceptance run covers them.

Not done by the orchestrator: the two new test files were not read line by line; the Play Mode check
was not run again (the logs and one still were read); no Player or IL2CPP build; no measurement of
the read's cost in a frame beyond the allocation tests.

To know: a `PipelinedJobs` agent refuses most reads (one warning per agent); a fact key reads its
default for up to one frame after an automatic restart; `BlackboardUnsupportedScope` has no test
because the generated compiler refuses a tree with an Agent-scope key (AIBT3017), which is a separate
limit or defect and is not investigated.
