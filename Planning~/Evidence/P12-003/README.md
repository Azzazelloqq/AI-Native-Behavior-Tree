# P12-003 evidence — read-only game arrays in nodes

Settled 2026-11-25: not built. The card `Planning~/Tasks/P12/P12-003-read-only-game-arrays.md` holds the
outcome, the shape to build if reopened (design E1), the condition, the prototype plan and the code sites.

## Records (copied on 2026-11-25 from `C:\UnityProjects\_aibt_acceptance\SweetKingdom\`)

The texts are unchanged. Their line references point at the code of `8396202` (the 0.2.0 package).

- `readonly-arrays-design-read.md`: the first independent design read.
  - §2: the designs A-G.
  - §3: the semantics to fix.
  - §4: Burst and safety.
  - §5: the prototype plan.
  - §6: the recommendation.
  - §7: the questions for the consumer and the owner.
- `decision-readonly-arrays.md`: the orchestrator's decision. Its sentence "an ADR records the considered
  designs" is replaced by the card: no ADR, because nothing in the architecture changes (see the card,
  "Decision record").
- `question-readonly-arrays-2026-11-25.md`: the recipes and the questions sent to the consumer.
- `answer-readonly-arrays-2026-11-25.md`: the consumer's answer ("0.2.0 is enough"), with the orchestrator's
  note on the second read.
- `readonly-arrays-reopen-read.md`: the second independent read, finished after the answer arrived. Its
  corrections were taken; its proposal to prototype now was not.

## Tests and runs

- Written on 2026-11-25:
  - `TreeSimulationSquadRowsTests`: 13 cases, with the fixture assembly `AIBT.CodeGen.P12003SquadRows.Tests`.
  - The verifier case `ShortForm.SameInputTwice`.
- The verifier case was run alone outside the package (generator and verifier sources copied to a scratch
  folder, the dotnet SDK of 6000.6.9f1, no Editor). It passed, and with two different ids it failed, as it
  should.
- The full P2 matrix fails at baseline before reaching it (the contract compilation lacks the agent-frame
  sources). That failure is recorded in the card and not fixed here.

### Editor runs (Modules on 7000.0.0a8, 2026-11-25, `Tools/Unity/Invoke-UnityTests.ps1`)

**Compile.** The orchestrator compiled the working tree with the fixture assembly and the asmdef reference: no
errors. At 23:05 `AIBT.Integration.Tests` was recompiled after an unrelated doc-comment edit. It logged no
warning from the new files; the only warnings were the 22 old CS0728, a P12-005 follow-up.

**`TreeSimulationSquadRowsTests`, 23:00: 13/13.** In 0.82 s:

```
Passed OneRowsArray_TwoChannels_EachAgentReadsItsOwnAndItsTargetRow(False|True)
Passed OutOfRangeIndexInTheSecondChannel_RefusesTheStep_NothingRuns(False|True)
Passed ReadingBeyondTheCatalogsValueSessions_FaultsTheAgentWithCapacityExceeded(False|True)
Passed ReadingOneInputTwice_WithRoomInTheCatalog_ReadsTheSameRowTwice(False|True)
Passed RebindingOneChannel_LeavesTheOtherAsItWas(False|True)
Passed TwoChannels_LogIsTheSameInPlaceAndInJobs
Passed WakeOnChange_OnOneChannel_WakesOnlyTheReadersThroughThatChannel(False|True)
```

- The jobs cases (`True`) pin, among others:
  - the fault cause `CapacityExceeded`;
  - that the refused callback's command is not published.

  Before this run these two were known only from reading the code.
- The tests do not show whether the jobs cases ran under Burst or managed: they assert values, not the path.

**After the recompile, 23:07–23:11:**

```
testName TreeSimulationIndexedInputTests 44/45 (failed=0 skipped=0)
testName TreeSimulationIoTests 20/20 (failed=0 skipped=0)
testName TreeSimulationSquadRowsTests 13/13 (failed=0 skipped=0)
```

- The 45th case of `TreeSimulationIndexedInputTests` is P12-002's `AgentFrameJob_RunsTheIndexedRoutineBurstCompiled`,
  reported Inconclusive: the recompiled assembly's job was still compiling asynchronously (see
  `Planning~/Evidence/P12-004/README.md`). The rerun at 23:11 passed.
- The one Console error of that run is the refusal that
  `TreeSimulationIoTests.Hosts_StillRefuseSnapshotBindings_AndRegisteredCommandPayloads_AsBefore` provokes on
  purpose (`Callback rejected: TypeMismatch`); that test passed.

**The verifier case was not run again:** the full P2 matrix still fails at baseline.

### After the independent review of `8396202..dccab95` (2026-11-25)

- **`ReadingOneInputTwice_WithRoomInTheCatalog_ReadsTheSameRowTwice` shows only the case with room.** The
  review found that its `Edge == 1` loop compared a value with itself. That loop is removed. The test keeps
  the assertion that each agent's order holds (first read, second read) members, both from the row its index
  selects. Its comment now says where the room comes from: three sessions and 28 staging bytes (8 + 8 + 12),
  given by the compare-squads node of the same catalog. It does not show that a second read always succeeds,
  and the documents no longer say so (below).
- **Documents.** `simulation-container.md`, "Read each input once per callback", and
  `burst-node-authoring.md`, "Read a binding once per callback", now say what the code does:
  - each read takes a value session and staging bytes;
  - the catalog sizes the room for the largest need of any node;
  - a second read fits or not depending on the rest of the catalog and on what the node already did;
  - a host that dispatches members together sizes the room for the group (not traced or tested);
  - so a second read must not be relied on;
  - the verifier case runs once, outside the package; the full verifier matrix stops before it.
- **Run** (recompiled first, 23:35–23:37, with `TreeSimulationIndexedInputTests` and
  `BurstExecutionPathTests`): `testName TreeSimulationSquadRowsTests 13/13 (failed=0 skipped=0)`. No Console
  error.

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
