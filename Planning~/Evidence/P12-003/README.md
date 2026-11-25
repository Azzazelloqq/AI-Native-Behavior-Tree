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
