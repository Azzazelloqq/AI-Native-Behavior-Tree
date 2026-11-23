# P11-012 evidence — reproducible random streams (ADR-P11-008, AIBT-052)

Branch `feature/aibt-consumer-workflow`, base `0aa9a6a`. Raw files: `C:\UnityProjects\_aibt_acceptance\P11-012\`.
Unity 6000.6.9f1, the Modules Editor (Pipeline port 7810). Another project's batch-mode Unity was running on
the machine; no conclusion is drawn from timings. Status: accepted by the orchestrator on `e70dbea`
("Acceptance run" at the end); the item is in `Review`. The review's follow-ups are in P11-014.

## Commits

| Commit | Content |
| --- | --- |
| `3d91662` | feat: `RandomStreamSeeding`, host and program overloads, `ProductionTreeHost.RandomStreamIdentity`, `NativeTreeAgent.RandomStreamId` and `RunIndex`; tests |
| `69eb9d7` | feat: inspector help box (random tree, id 0); its test |
| `7ae6c1b` | docs: `time-and-random-v1.md` "Stream identity", ADR-P11-008 accepted, AIBT-052 in `decisions.md` |
| `513f686` | docs: user guide, production host page, Native Guard sample, generated API reference, CHANGELOG |
| this commit | task card, work item, MASTER_PLAN, this evidence |

## What changes for a user

- `NativeTreeAgent` has a new Inspector field **Random Stream Id** (`RandomStreamId`, default 0) and a
  read-only `RunIndex`. At 0 nothing changes: streams use the host's process-wide instance id, so agents and
  runs differ and nothing repeats across sessions. A nonzero id makes each run's draws depend only on the
  seed, the tree, the id and the run index; the run index is 0 after every enable and +1 for each run that
  starts while the agent stays enabled (automatic restart, `Restart`, a change of `Tree`), and a failed start
  does not count. Two agents with one id and one seed draw the same numbers.
- A bare host takes `new RandomStreamSeeding(seed, identity)` through
  `ProductionTreeHost.TryBootstrap(..., RandomStreamSeeding, out failure)` or
  `ProductionTreeProgram.TryBootstrap(host, RandomStreamSeeding, out failure, ...)`; identity 0 is automatic;
  `RandomStreamSeeding.ForRun(seed, id, run)` applies the agent's rule; `host.RandomStreamIdentity` reports
  the word used. Every existing overload behaves as before.
- The inspector shows a help box when the tree has random nodes and the id is 0.
- The Native Guard demo sets stream id 1; its overlay shows seed, stream id and run (it showed the instance
  id).

## The documentation rule (exact)

`Documentation~/specifications/time-and-random-v1.md`, section "Stream identity" (normative): the derivation
mixes a 64-bit stream identity (formerly named `TreeInstanceId`; bytes, position and the name
`AIBT-PCG-XSH-RR32-v1` unchanged). Policies: `TreeSimulation` — the container-local instance id;
`ProductionTreeHost` default — its process-wide `InstanceId`, consumed by every bootstrap including a failed
one, unique but not repeatable; a nonzero `RandomStreamSeeding` identity — used as given; `NativeTreeAgent` —
id 0 the host default, otherwise `CombineStreamIdentity(RandomStreamId, runIndex)`. The function:

```text
CombineStreamIdentity(streamId, runIndex):
    z = Mix64(Mix64(streamId xor 0x414942545354524d) + runIndex)
    if z == 0: return 0x414942545354524d
    return z
```

Non-goal stated in the spec, the ADR and the user guide: equal streams are not equal behaviour; hosts update
in start order (the scheduler's last tie-break is `InstanceId`) and nodes draw as frame time lets them run;
lockstep replay is `TreeSimulation`'s job.

## ADR facts verified against the code (at `0aa9a6a`)

| Fact | Where | How verified |
| --- | --- | --- |
| Adapter seeds every slot with its tree instance id | `GeneratedTreeDispatchAdapterV2.cs:795-800`; slot derivation `:1535-1542` | read |
| Every bootstrap increments the process-wide counter before it can fail | `ProductionTreeHost.cs:230` (returns at `:231`), `:271` (returns at `:272-273`) | read; at run time: a host bootstrapped after a failed generated bootstrap has an id two above the previous host (`BootstrapUnrelatedHostsWithAFailureBetween`, asserted in all four policies) |
| Handle checks use `InstanceId` | `ProductionTreeHost.cs:379` (write), `:505` (read) | read |
| Scheduler update order's last tie-break is `InstanceId` | `ProductionTreeScheduler.cs:1008` (`CompareDue`) | read |
| The normative text | `time-and-random-v1.md:22` (rule), `:37` (byte layout), `:41` (zero invalid), `:102-108` (restart, host policy) | read |
| Agent per-run host and seed | `NativeTreeAgent.cs:44`, `:86`, `:238-239`, `:302-303`, `:307-313`; `ProductionTreeProgram.cs:33-41` | read |
| Container-local ids | `TreeSimulation.cs:203-211`, `TreeGroup.cs:506-508` | read |
| Reflection on private counters in tests | `NativeTreeAgentRandomSeedTests.cs:158`, `HostRandomSeedingTests.cs:21`, `:22-23` | read; all three removed |

The ADR's old references (`:759-764`, `:1499-1506`, `:376`, `:502`) were stale; they are replaced.

## Tests

Failing before: the tests were written first and compiled against the unchanged runtime: the compile fails
on the missing API (`before-recompile.txt`: `RandomStreamId`, `HasRandomNodes`, `RandomStreamHint`). The
reproducibility test also carries a control: an agent with id 0 draws other values in the second session,
which is what the test sees for every agent without this change.

| Run | Classes | Result | Raw file |
| --- | --- | --- | --- |
| 1 | `NativeTreeAgentRandomSeedTests` (20), `RandomStreamSeedingTests` (11), `HostRandomSeedingTests` (12), `NativeTreeAgentRandomStreamHintTests` (1), `TreeSimulationHostEquivalenceTests` (4) | 48/48 | `tests-run1.txt` |
| 2 | the reproducibility test alone, 4 policies | 4/4 | `reproducibility-test-status.json` |
| 3 | `McpDocumentationGeneratorsTests` (14), `UserGuideSnippetTests` (6) | 20/20 | `tests-docs.txt` |
| 4 | `NativeTreeAgentTests` (16), `NativeTreeAgentValueReadTests` (10), `NativeLibraryRandomTests` (36), `TreeSimulationTests` (13), `PublicBurstAbiContractTests` (4), `NativeLifecycleMachineTests` (14) | 93/93 | `tests-neighbours.txt` |

New tests:

- `NativeTreeAgentRandomSeedTests.RunsWithAStreamId_DrawTheSameInAnotherSession_InAnyStartOrder_AfterOtherHosts`
  (Immediate, `JobsAgentFrame`, forced `BatchedJobsSameFrame`, forced `PipelinedJobs`): A (id 11) and B (id 22),
  three runs each by automatic restart, values read with `TryGetValue` at completion; destroyed; three
  unrelated hosts with a failed bootstrap between; B started before A: every (id, run) pair equal; run indexes
  0, 1, 2; A and B differ; the id-0 control differs between sessions.
- `...RestartStopAndTreeChangeTakeTheNextRun_DisableEnableRepeatsRunZero` (four policies): compared with a
  reference agent with the same id: first run 0, `Restart()` run 1, `Stop()` then `Restart()` run 2, a change
  of `Tree` (another asset of the same tree) run 3, disable/enable run 0, `Restart()` run 1.
- `...AStartThatFails_DoesNotConsumeARunIndex`: a world without a scheduler refuses the start; with a working
  world `Restart()` starts run 0 and draws the reference's run 0.
- `...TwoAgentsWithOneStreamIdAndSeed_DrawTheSame` (and two id-0 agents differ),
  `...EveryRunsHost_DerivesItsStreamsFromTheCombinedIdentity`,
  `...TheDefaultStreamIdIsZero_ItIsSerialized_AndTheRunIndexStartsAtZero`.
- `RandomStreamSeedingTests`: the five combine vectors, the zero case, a 256 × 256 grid without zero or
  collision (where `+` and `xor` collide), the derived-stream row, the value type, the host and program
  overloads (identity, seed-only, automatic, seedless, direct), two hosts with one identity run alike.
- `NativeTreeAgentRandomStreamHintTests`: the hint for a random tree with id 0, none with an id, a plain tree
  or no tree.
- Changed: `OneTreeSeedAndStreamIds_RollTheSameSequenceUnderEveryPolicy` (was `...InstanceIds...`, set the
  host counter by reflection), `HostRandomSeedingTests.TwoRunsWithTheSameSeedAndStreamIdentity_GiveTheSameResults`
  (same), and the host/container equivalence (`AssertHostMatchesContainer`) now bootstraps the host with
  identity 1 instead of setting the container's counter. The existing derivation tests
  (`EveryRunsHost_IsSeededFromTheAgentsSeed_AndItsOwnInstanceId`, `TwoAgentsWithOneTreeAndOneSeed_HaveDifferentStreams`,
  the policy test, the host derivation tests) are unchanged and green.

Test vectors come from `stream-identity-vectors.py` (written from the specification, checked against its
second published row; output `stream-identity-vectors.txt`).

## Other checks

- Generated reference: `regenerate-docs.cs` through `unity command eval_file` (same generator calls as
  `McpDocumentationGeneratorsTests`); only `api-reference-runtime.md` changed (one new type, five members).
  The eval hits the CLI's 5 s cap and completes; it leaves one "Main thread operation timed out" error in the
  Console (`regenerate-docs-output.txt`).
- The Native Guard sample is not compiled in this project (`Samples~`): `sample-api-check.cs` confirms
  `SerializedProperty.ulongValue`, the field `_randomStreamId` and `RunIndex` exist (`sample-api-check.txt`).
- Console after the runs (`console-after.txt`): only errors the existing tests log on purpose, and Editor
  noise ("Release of invalid GC handle", "VirtualArtifacts") from domain reloads.
- ABI v2 (`PublicBurstAbiContractTests` green), the compiled format, `CodeGen~`, `package.json`, pins and
  fixtures are untouched; existing public signatures unchanged (only additions).

## Not verified

- The full ten assemblies were not run (as instructed); the acceptance run is the orchestrator's.
- The Native Guard sample was not imported and played: its two edited files were checked by reading and by
  the API check above, not compiled.
- The inspector help box was tested through its helper, not by drawing the Inspector.
- Reproducibility across a real domain reload or Editor restart is shown by moving the process-wide counter
  inside one session (other hosts, a failed bootstrap, another start order), not by restarting the Editor.

## Deviations

- The run index of a failed start: a start counts once the host is bootstrapped and registered with its
  world; a start refused at bootstrap or at registration does not consume an index (the next start reuses
  it). `RunIndex` reports the last run that started.
- A change of `Tree` continues the run index (the component stays enabled; decision 2), while
  `CompletedRuns` is still reset by it, as before.
- Besides the brief's list, `Documentation~/production-host.md` described the old seeding rule and was
  updated too.

## Acceptance run (orchestrator, 2026-11-24)

Accepted together with P11-013 on `e70dbea`: the diff read, the protected paths, the recomputed vectors,
the independent review and the table of the ten assemblies (all green) are in
`Planning~/Evidence/P11-013/README.md`, "Acceptance run". For this item:

- The amendment of `time-and-random-v1.md` is the one ADR-P11-008 decided; the combiner's bytes and the
  derivation name are unchanged, and with `RandomStreamId` 0 the seeding is the same word as before
  (`ProductionTreeHost.TryBootstrap`: `randomSeeding.IsAutomatic ? treeInstanceId.Value : ...`, read by the
  orchestrator and confirmed by the review).
- Follow-ups in P11-014: the sentence that calls the combine function a bijection of the run index and also
  says it never yields 0 (false as written; the code is right), "every bootstrap, including one that fails",
  the `RunIndex` doc comment before the first run, tests for a bootstrap failure and for a stream-id change,
  a test that asserts constant arithmetic, and the inspector hint's cache key.
- Not done by the orchestrator: as listed there; the inspector help box was not looked at.
