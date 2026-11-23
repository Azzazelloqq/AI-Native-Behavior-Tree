# P11-013 evidence — Agent-scope memory of `NativeTreeAgent` (ADR-P11-013, AIBT-053)

Branch `feature/aibt-consumer-workflow`, base `d46fe21`. Raw files: `C:\UnityProjects\_aibt_acceptance\P11-013\`.
Unity 6000.6.9f1, the Modules Editor (Pipeline port 7810). Another project's batch-mode Unity was running on
the machine; no conclusion is drawn from timings. Status: accepted by the orchestrator on `e70dbea`
("Acceptance run" at the end); the item is in `Review`. The review's follow-ups are in P11-014.

## Commits

| Commit | Content |
| --- | --- |
| `562e54a` | feat: `NativeAgentScopeMemory`, adapter/host capture and restore, Agent-aware internal reads, `NativeTreeAgent.Restart(bool)`, the carry rules; mixed-scope tests |
| `1e5dda6` | feat: the graph's live values of Agent keys; its test |
| `0c6f38d` | docs: ADR-P11-013 accepted, ADR-P11-004 D3 amended for the agent, AIBT-053 in `decisions.md` |
| `daed617` | docs: quickstart ("What a restart keeps", reading Agent keys), library guide, production host page, generated API reference, CHANGELOG |
| this commit | task card, work item, MASTER_PLAN, this evidence |

## What changes for a user

- On a `NativeTreeAgent`, Agent-scope keys now keep their values from run to run: an automatic restart,
  `Restart()`, `Stop()` then `Restart()`, and a new `Tree` with the same Agent contract keep them. They
  return to their defaults on disable, after a run whose host faulted, on a `Tree` with another Agent
  contract, and on the new `Restart(true)`. Tree-scope keys start from their defaults in every run, as
  before. Trees without Agent keys behave as before.
- `TryGetValue` reads Agent keys (it logged `BlackboardUnsupportedScope` before), and the AIBT Graph shows
  their live values for a native agent.
- Unchanged: `ProductionTreeHost`'s public read (Tree-only), `SetFact` (Tree-only), library nodes (bind
  Tree keys only), `TreeSimulation` (a restart is a fresh agent).

## The documentation rule (exact)

`Documentation~/consumer-quickstart.md`, section "What a restart keeps":

> Agent-scope keys of a `NativeTreeAgent` belong to the component, not to a run (ADR-P11-013). They start
> at their defaults when the component is enabled and keep their values across automatic restarts,
> `Restart()`, `Stop()` followed by `Restart()`, and a change of `Tree` to a tree with the same Agent
> contract (contract id, version, schema and layout). They return to their defaults when the component is
> disabled, after a run whose host faulted (its values may be mid-update), when `Tree` changes to another
> Agent contract, and on `Restart(true)`. Tree-scope keys start from their defaults in every run; facts are
> written again.

followed by the table (`BehaviorTreeAgent` / `NativeTreeAgent` Tree keys / `NativeTreeAgent` Agent keys /
`TreeSimulation`) and the note that `SetFact` and library nodes are Tree-only, so Agent keys are used from
project nodes.

## Facts verified (at `d46fe21`)

| Fact | Where | How verified |
| --- | --- | --- |
| The spec gives the Agent context to the host: defaults at creation, exact contract binding, reset is an explicit transaction under quiescence | `agent-shared-blackboard-v1.md:236-247`, `:249-251`, `:253-262`, `:271-272`, `:283-292` | read |
| The standalone adapter keeps Agent slots past `_agentBaseOffset` | `GeneratedTreeDispatchAdapterV2.cs:21`, `:846-873`, `:91-94`, `:875-890` | read; the new tests copy and read the range |
| `NativeTreeAgent` builds a host per run, so Agent values reset each run | `NativeTreeAgent.cs:265-271`, `:341-342`, `:346-377`, `:379-388` | read; run time: the changed mixed-scope test failed 7/7 before (`tests-before.txt`) |
| The mixed-scope test pinned the reset (`agent-counter=41` every run) | `MixedScopeNativeTreeTests.cs:167`, `:219` | read |
| The user guide's workaround | `native-node-library.md:128-138` (the brief cited `:128-133`; the repeater advice is at `:137-138`) | read |
| Leaving the world completes an in-flight pipelined round on the main thread and releases the drive lock | `ProductionTreeWorld.cs:85-95`, `ProductionTreeScheduler.cs:307-318`, `:1172-1195`; `ProductionPipelinedGroupDriverV1.cs:114`, `:152`, `:171`, `:261-275`; `NativePipelinedPhaseControllerV1.cs:218` | read; run time: `ARestartAskedForWhileTheHostExecutes_...("forced-pipelined-jobs")` restarts while `Host.IsExecuting` and the new host carries the values, no fault |
| Teardown cannot change blackboard values (abort context has no blackboard access) | `BurstNodeContracts.cs:514-537`, `ProductionTreeHost.cs:943-958` | read |
| `BehaviorTreeAgent` keeps its blackboard across restarts (`Restart(true)` resets), pauses when disabled, rebuilds on a new tree | `Agents/BehaviorTreeAgent.cs:54`, `:86-96`, `:281-301` (no `OnEnable`/`OnDisable`) | read (for the table) |
| `TreeSimulation` restarts an agent as a fresh instance (Tree and Agent values) | `Runtime/Simulation/TreeGroup.cs:507`, adapter `TryResetInstance` `:776-786` | read (for the table) |

## Decision 3: the restart during execution

No frame-level deferral was needed. `StopHost` already unregisters the agent from its world before it
stops the host; for a host in a `PipelinedJobs` round, `ProductionTreeScheduler.TryUnregister` drains that
round (it completes the job handle and releases every member's drive lock) before it returns. The Agent
copy happens right after the unregister, through `ProductionTreeHost.TryCaptureAgentMemory`, which refuses
while `CanReadTreeValues` is false. Every other policy runs and releases a host inside the scheduler's frame.
If the gate ever refused, the agent would reset the memory and log a warning instead of reading; no test
reaches that branch. The restart stays synchronous, as before.

## Tests

Failing before:

- `before-recompile.txt`: the new tests compiled against the unchanged runtime fail on the missing
  `Restart(bool)` (CS1501). The same output has a second error, a missing `using` in the new test file
  (CS1061 `AllocatingGCMemory`), which I fixed before going on.
- `tests-before.txt` (the new test file set aside, unchanged runtime): the changed
  `NativeTreeAgent_RunsTheMixedTree_InEverySchedulingProfile` fails 0/7 (`agent-counter` cannot be read; it
  was 41 every run); `GraphNativeLiveBlackboardTests` 13/15 (the new Agent-key test shows `live: —`, the
  refusal text test).

| Run | Classes | Result | Raw file |
| --- | --- | --- | --- |
| 1 | `MixedScopeNativeTreeTests`, `GraphNativeLiveBlackboardTests` | 24/33, 15/15 | `tests-run1.txt` (test mistakes: `Restart()` keeps `LastError` after a fault; a pipelined finding below; the loop tree's `wait` not in the catalog) |
| 2-3 | `MixedScopeNativeTreeTests` | 24/33, 32/33 | `tests-run2.txt`, `tests-run3.txt` (missing expected host log; `wait` ticks 1 loops in one update; the pipelined finding) |
| 4 | `MixedScopeNativeTreeTests` (33), `GraphNativeLiveBlackboardTests` (15) | 33/33, 15/15 | `tests-run4.txt` |
| 5 | `NativeTreeAgentTests` (16), `NativeTreeAgentValueReadTests` (10), `ExternalTreeBlackboardReadTests` (11), `ExternalTreeBlackboardWriteTests` (11), `NativeTreeAgentRandomSeedTests` (20), `TreeSimulationHostEquivalenceTests` (4), `PublicBurstAbiContractTests` (4), `ReadWriteBindingSessionTests` (11) | 87/87 | `tests-neighbours.txt` |
| 6 | `McpDocumentationGeneratorsTests` (14), `UserGuideSnippetTests` (6) | 20/20 | `tests-docs.txt` |

New and changed tests (all through `TryGetValue`, `Restart`, `Stop`, `Tree`, enable/disable):

- Changed: `NativeTreeAgent_RunsTheMixedTree_InEverySchedulingProfile` (7 kinds): run n shows
  `agent-counter = 40 + n` and `tree-out = (40 + n) * 1000 + tree-in`, read with `TryGetValue` (was the
  internal byte read and 41 every run).
- `AgentKeys_SurviveRestartStopAndASameContractTree_AndResetOnRequestDisableAndAnotherContract` (7 kinds):
  40 on enable; 41; `Restart()` keeps 41 before the first update (Tree key `tree-out` back to its default);
  42; `Stop` then `Restart` keeps 42; 43; a tree with the same contract and one more Tree key (the Agent
  range starts 8 bytes later) keeps 43; 44; `Restart(true)` 40; disable/enable 40; a tree with contract
  version 2 starts at 40; back to version 1 starts at 40 again.
- `AFaultedRun_ResetsAgentKeys` (7 kinds): after two runs (42 carried) the next run's host faults before its
  first update (internal `FailExternalScheduling`); after `Restart()` the counter is 40.
- `ARestartAskedForWhileTheHostExecutes_CarriesTheValuesTheCompletedJobsLeft` (7 kinds): drives until the
  host executes between frames (only `PipelinedJobs`, asserted) or the run ends, restarts: no fault, the
  new host is idle, the carried value is 41 or 42 under `PipelinedJobs` (42 otherwise) and the next run
  adds one to it.
- `AWarmedUpdate_ThatUsesAgentKeys_AllocatesNoManagedMemory` (forced Immediate): a tree that loops inside one
  run and bumps the Agent key; 200 frames of scheduler update, `ProcessRootResult` and an Agent-key read:
  0 GC.Alloc samples and `Is.Not.AllocatingGCMemory()`; the counter grows; no restart.
- `GraphNativeLiveBlackboardTests.AnAgentScopeKey_ShowsItsValue_WhichARestartKeeps`: `live: 40`,
  `TryFormatValue` gives "40", after a run `live: 41`, after `Restart()` still `live: 41` with `tree-out`
  back to `live: 3`, then `live: 42`. The refusal text case now reads "Only Tree- and Agent-scope keys are
  read from a native agent."

## Other checks

- Generated reference: `regenerate-docs.cs` through `unity command eval_file`; only `api-reference-runtime.md`
  changed, by one line (`Restart(System.Boolean)`). The eval hits the CLI's 5 s cap and completes
  (`regenerate-docs-output.txt`).
- Console after the runs (`console-after.txt`): only errors the tests log on purpose, Editor noise
  ("Release of invalid GC handle", "VirtualArtifacts") from domain reloads, and the eval's "Main thread
  operation timed out".
- ABI v2 (`PublicBurstAbiContractTests` green), the compiled format, `CodeGen~`, `package.json`, pins and
  fixtures are untouched; existing public signatures unchanged (one public addition:
  `NativeTreeAgent.Restart(bool)`).
- `Tools~/Verification/Verify-Static.ps1`: `verify-static.txt` (192 work items).

## Not verified

- The full ten assemblies were not run (as instructed); the acceptance run is the orchestrator's.
- Enable and disable are driven through the agent's internal `Activate`/`Deactivate`, as the existing agent
  tests do, not through Unity's `OnEnable`/`OnDisable` in Play Mode.
- A fault in the middle of an update is not produced (no fixture node can fault, and fixtures are not
  changed); the faulted-run test faults the host before its first update through an internal method. The
  rule does not depend on when the fault happened: any host with a failure is not copied.
- The `BehaviorTreeAgent` and `TreeSimulation` columns of the table come from reading the code, not from
  new tests.
- The Native Guard sample has no Agent keys and was not played.

## Deviations and findings

- **Decision 3 needs no deferral** (section above): the copy happens at the point where the host is idle,
  which the existing world unregister reaches synchronously by completing the pipelined round.
- **Which faults reset:** a run whose host has a failure resets the memory. A start that fails at bootstrap
  leaves it as it was (no host was created to read); a start refused at world registration copies the
  host's values back unchanged. The next successful start carries them.
- `Restart()` after a fault leaves `LastError` set until the next enable: unchanged behaviour (the managed
  agent clears it); the test checks the run instead.
- `Restart(true)` on a disabled agent resets the memory too (enable would reset it anyway).
- A test-only catalog set `MixedLoopCatalog` (built-in leaves + the P11-009 fixture shard), following
  `ReadWriteHoldCatalog`, gives the allocation test a tree that loops inside one run. The fixture nodes are
  unchanged.
- **Finding, outside this task:** under a profile that forces `PipelinedJobs`, a host whose root finished
  and that stays registered (`RestartMode.Never`) faults with `NativeCapacityPlanInvalid` on later frames
  (the scheduler's forced-policy check, `ProductionTreeScheduler.cs:557-620`), and the agent does not report
  it (its result was already reported; `LastError` stays null), so `TryGetValue` returns false. No restart
  and no Agent copy happen on that path; it was seen in this task's test (`tests-run3.txt`) and not
  reproduced against the old code. The lifecycle test skips the "finished run stays readable" check under
  that policy.

## Acceptance run (orchestrator, 2026-11-24)

Read by the orchestrator on `e70dbea` (clean working tree): this file and the P11-012 evidence, the
session's report, the whole diff `0aa9a6a..e70dbea` under `Runtime/` and `Editor/`, the amendment of
`Documentation~/specifications/time-and-random-v1.md`, and `ProductionTreeScheduler.TryUnregister`. The run
covers P11-012 and P11-013 together.

- **Protected paths.** `git diff --stat 0aa9a6a..e70dbea` is empty for `Tests/Editor/CodeGen/Contracts`,
  `Tests/Fixtures`, `CodeGen~` and `package.json`. Under `Documentation~/specifications` one file,
  `time-and-random-v1.md` (+72 -8): the amendment ADR-P11-008 decided. The word `TreeInstanceId` becomes
  "stream identity" in four existing sentences and a section "Stream identity" is added; the combiner's byte
  stream, its order and the name `AIBT-PCG-XSH-RR32-v1` are unchanged.
- **Vectors.** The six `CombineStreamIdentity` vectors and the identity `89076cda0e1cdaaf` of the derived
  row were recomputed by the orchestrator with a separate Python script: all equal the specification.
- **Decision 3 of ADR-P11-013** (a restart while a `PipelinedJobs` job is in flight) is implemented without a
  deferral: `ProductionTreeScheduler.TryUnregister` drains the in-flight run (`DrainPipelinedRunContaining`)
  before `StopHost` captures the values. Read by the orchestrator and, independently, by the review below.
  This is another mechanism than the decision named and reaches the same guarantee; accepted.
- **Independent review.** A reader that was given the code, the ADRs and neutral questions (not the
  orchestrator's view) reviewed the range: no blocker, three should-fix findings, sixteen notes. Full text:
  `C:\UnityProjects\_aibt_acceptance\P11-013\acceptance\independent-review.md`. The orchestrator read the code of S1 and confirms it.
  - S1: under a forced `PipelinedJobs` profile a host whose root finished and that stays registered
    (`RestartMode.Never`) is failed by the scheduler one frame later (`ProductionTreeScheduler.cs:682-690`
    treats a pipelined start that opened no host as a failure). The agent does not report it, `TryGetValue`
    returns false, and the next `Restart()` resets the Agent memory. This is the "finding outside this task"
    above; it is inside ADR-P11-013 decision 1 once a `Restart()` follows. The scheduler code is older than
    this range.
  - S2: the schema-hash, layout-hash, range-size and slot-count comparisons of "same contract" have no test.
  - S3: the `forced-agent-frame` case runs in place (batch below `AgentFrameMinimumBatchSize`), so the seven
    scheduling kinds are about four execution paths.
  - Notes that lead to a change: the combine function cannot be a bijection of the run index and never yield
    0 (the text is wrong, the code is right); "every bootstrap, including one that fails" is not true for a
    bootstrap refused before the counter; `agent.Host.Stop()` then `Restart()` resets the memory and is not in
    the ADR's list; the failed-start rules are in this file and not in the ADR; tests missing for an
    Agent-only tree, several Agent keys, a bootstrap failure and the run index, a stream-id change; the
    allocation test never restarts; the inspector hint cache is keyed by the asset reference.
- **Decision (orchestrator, under the owner's delegation of 2026-11-23).** Both items are accepted into
  `Review`. S1 does not block: no value is corrupted, the cause is an older scheduler defect on one
  configuration (forced `PipelinedJobs`, a finished host that stays registered), and its fix with a
  failing-before test, S2, S3 and the notes above are items 10-15 of P11-014. Neither item is set to `Done`
  before P11-014 is accepted.
- **Full run of ten assemblies** on `e70dbea`, 2026-11-23 23:47 - 2026-11-24 00:04; Editor-log counters did
  not move (unknown-command 0, main-thread-timeout 5 before and after). Raw output:
  `C:\UnityProjects\_aibt_acceptance\P11-013\acceptance\full-run-e70dbea.txt`.

| Assembly | `0c2b541` | `e70dbea` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | - |
| `AIBT.Integration.Tests` | 465/465 | 510/510 | 45 in `RandomStreamSeedingTests`, `NativeTreeAgentRandomSeedTests` (stream id), `HostRandomSeedingTests`, `MixedScopeNativeTreeTests` (Agent memory) |
| `AIBT.Editor.Tests` | 857/857 | 859/859 | 2: `NativeTreeAgentRandomStreamHintTests` and `GraphNativeLiveBlackboardTests` together, net |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | - |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | - |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | - |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | - |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | - |
| `AIBT.CodeGen.GenerationTests` | 14/14 | 14/14 | - |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | - |

Not done by the orchestrator: the test files and the documentation pages outside the specification were
not read line by line (the review read the tests); no probe was run again; the split of the new tests
between the two items and between classes was not counted; nothing was looked at in the Editor window or in
Play Mode; the Native Guard sample with its two edited files was not imported.
