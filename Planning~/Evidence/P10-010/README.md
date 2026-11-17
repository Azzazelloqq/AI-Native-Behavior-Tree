# P10-010 evidence — Simulation container, stage G2 (inputs, commands, wake on changed input)

Card: `Planning~/Tasks/P10/P10-010-simulation-container-g2.md`. ADR:
`Documentation~/decisions/ADR-P10-008-simulation-container.md`. Unity 6000.6.9f1, branch
`feature/aibt-consumer-workflow`, base `14b6799` (G1).

## 1. What was built

**Public API (additive, `AIBT.Simulation`, experimental until G5):**

- `InputChannel<T>`, `CommandChannel<T>` (`Create(id)`, `Id` = FNV-1a 64 of the binding id, `Name`,
  `IsValid`) and `Command<T>` (`Agent`, `Value`).
- `TreeGroup.TryBindInput<T>(InputChannel<T>, NativeArray<T>)`, `TryUnbindInput<T>`,
  `TrySetWakeOnChange<T>(channel, bool)`, `TryGetCommands<T>(CommandChannel<T>, out ReadOnly)` and
  `GetCommands<T>`.
- `TreeGroupSettings.CommandQuotaPerAgent` (0 = `DefaultCommandQuotaPerAgent` = 4, negative refused).
- `AgentFault.Cause` (`BurstContextResult`).
- `SimulationResult.InputMissing`, `TypeMismatch`, `CatalogTooOld`, `Unsupported`, `UnknownChannel`
  (appended).
- Short form `AIBT.Native.AibtInputAttribute` (`[AibtInput("id")]` on a `SnapshotReadHandle<T>`).

**Generator (additive, D3).** Every catalog's generated executor also implements
`AIBT.AgentFrame.IGeneratedBindingIdentityV1` (`BindingIdCount`,
`TryGetBindingId(ordinal, out id)`): the FNV-1a 64 of each catalog binding's id, in layout-blob
order. The compiled format and the catalog layout blob are unchanged. `Analyzers/AIBT.CodeGen.dll`
was rebuilt twice to the same bytes (sha256 `94a582af…bdbae6`, `Analyzers/AIBT.CodeGen.sha256`).

**Runtime (container only).**

- `SimulationProgramMapV1` maps a program's snapshot and effect bindings to input and command
  channels (by binding id).
- The dispatch adapter, given that map, gives snapshot bindings a live region (target ordinal
  `Slots.Count + channel`). Without the map (hosts) it refuses them exactly as before.
- `SimulationAgentRoutinesV1` (stage a row, reserve quota, capture commands) runs from both the
  in-place dispatch and the agent-frame job (`AgentFrameCoreV1.RunAgent`), so both paths produce the
  same bytes.

**`Step` order (G2 additions in bold).**

1. **Validate every bound input of every active group** (unbound, disposed or shorter than
   `Capacity` → the step is refused, nothing runs).
2. **Copy each bound array's first HighWaterIndex rows into a group-owned buffer.** The copy is made
   once per step, before teardown and think, for three reasons: every agent reads the same values,
   jobs never touch the game's safety handles, and the game may write its arrays again as soon as
   `Step` returns.
3. **Reset the command log.** **Mark agents whose wake-on-change row changed as due.**
4. Teardown.
5. Think. **Each think captures its commands.**
6. **Collect the commands in slot order, then emission order.**

## 2. Tests

New: `Tests/Integration/Simulation/TreeSimulationIoTests.cs`, 19 test cases.

- The input and command nodes are in a fixture shard, `Tests/Editor/CodeGen/GenerationP10010/`
  (assembly `AIBT.CodeGen.P10010SimulationIo.Tests`).
- The catalog `SimulationIoCatalog` is in the Integration assembly.
- The registered input `SenseInput` and command `OrderCommand` declare their fields out of id order,
  so raw layout ≠ canonical layout.

| Test | What it proves | Result |
|---|---|---|
| `Population_InputsAndCommands_DigestsAreIdentical_AcrossPathsWorkerCountsAndRuns` | 2 groups × 260 agents (guard `Spread(5)` with wake on changed `sense`; scout `Spread(3)` reading `sense` and `speed`), 300 ticks, both channels written every tick, 20 agents per group removed at ticks 60/180 and re-added at 61/181. The per-tick digest covers agent state, both command channels with their agents, and fault causes. It is identical across: in place ×2 (same process), jobs with `MinimumJobBatch` 1, default settings, 0 job workers and 1 job worker. Checks: > 2 000 orders, pings = orders, > 500 input wakes, 0 faults, ≥ 300 job batches on the job paths | pass (76 s) |
| `ReactiveInterrupt_RisingInput_PreemptsAtNextThink_OrNextStepWithWake` ×4 | Threat rises at tick 13 for a guard thinking every 10 ticks: pre-empts at tick 20 without wake, at tick 13 with wake; in place and jobs | pass |
| `Step_WithInputsCommandsAndWake_AllocatesNothing_AfterWarmUp` ×2 | 0 managed bytes per `Step` after warm-up with inputs written, commands read and wake on; in place and jobs | pass |
| `CommandQuota_Overflow_FaultsTheAgentWithCapacityExceeded_NeverThrows` ×2 | Quota 1, two orders in one think: the second node does not run and the agent faults with `Cause = CapacityExceeded`; the first order is kept, `Step` succeeds, nothing throws. Quota 2: both orders arrive | pass |
| `CommandQuota_Negative_IsRefused` | `TryAddGroup` with a negative quota → `InvalidArgument`; 0 → default 4 | pass |
| `Inputs_UnboundStaleWrongLengthAndWrongType_AreRefusedWithResultCodes` | Unbound → `InputMissing`; short or default array → `InvalidArgument`; unknown channel → `UnknownChannel`; `float` or a look-alike struct for `sense` → `TypeMismatch`; wrong command type → `TypeMismatch`; disposed array → `InputMissing` with nothing run (collection checks); group grown past the array → `InvalidArgument` until rebound | pass |
| `Inputs_NonCanonicalValue_FaultsOnlyTheReadingAgent` ×2 | A NaN row faults only the agent reading it (`Cause = InvalidEncoding`); the step and the other agents go on | pass |
| `CallsDuringStep_ReturnBusy` | `TryBindInput`, `TryUnbindInput`, `TrySetWakeOnChange` and `TryGetCommands` during a step → `Busy` | pass |
| `ExternalActions_AreRefusedWithUnsupported` | A program with `AsyncOperationHandle`/`CompletionHandle` → `TryAddGroup` returns `Unsupported` | pass |
| `OldCatalog_RunsInPlace_CommandsDiscarded_InputsRefused` | A catalog without binding ids (the executor wrapped so it hides the new interface): blackboard-only and commands-only programs run in place with the same agent states, and commands are discarded as in G1; a program with inputs → `CatalogTooOld` | pass |
| `Hosts_StillRefuseSnapshotBindings_AndRegisteredCommandPayloads_AsBefore` | `ProductionTreeHost`: a snapshot program fails bootstrap with `NativeCapacityPlanInvalid`; a command program runs with effects off; with `effectCapacity` 8 it faults `NativeLifetimeStateInvalid` (callback rejected `TypeMismatch`) and logs that error as before | pass |
| `CanonicalLayout_IsOrderedByFieldId_AndTranslatedBothWays` | Pins `Int32@0x1/Boolean@4x1/Float32@8x1 size 12` (sense) and `Float32@0x2/Float32@8x1/Int32@12x1/Boolean@16x1 size 20` (order), plus an end-to-end decode | pass |
| `NegativeZeroInput_IsNormalized` | −0.0 in a row reaches the node as +0.0, so command bits are 0 | pass |

## 3. Findings on the two "not established" items

### 3.1 Command payload byte format = the canonical value encoding (not raw `T`)

- **Layout.** A registered value is laid out with its fields ordered by stable field id (UTF-8
  ordinal), naturally aligned, nested values flattened, little-endian, and the size rounded up to
  the largest alignment. A C# struct is laid out in declaration order, so its raw bytes equal the
  canonical bytes only by coincidence.
- **Write rules.** Writes refuse NaN and ±Inf and normalise −0.0 to +0.0.
- **Dispatch.** The dispatch publishes one `NativeBurstDispatchCommandV2` per emit (`Kind = Effect`,
  `TargetOrdinal` = catalog binding ordinal, `PayloadSize` = canonical size). At most one command is
  published per effect binding per dispatch.
- **Container log.** The container appends `(channel, canonical payload)` to the agent's log.
- **Decoding.** `TryGetCommands<T>` decodes canonical → raw `T` through a leaf table built from the
  catalog's value fields and the field offsets of `T` (`SimulationValueLayoutV1`). It does this once
  per step per channel, into a cached list.
- **Pinned by** `CanonicalLayout_…` and `NegativeZeroInput_IsNormalized`.

### 3.2 Snapshot read path

- **How the dispatch reads.** `SnapshotReadHandle<T>` resolves like a blackboard read:
  `TryBeginSnapshotRead` copies the bytes from the slot's live-value region at the binding's live
  offset, and the codec reads them leaf by leaf.
- **What request validation requires.** It treats a snapshot as a live binding: a live offset, a
  non-sentinel target ordinal, canonical bytes at request begin, and one shared offset per target
  ordinal.
- **What the container does.**
  - It gives each snapshot binding a live region; bindings of one node on the same channel share it.
  - It uses target ordinals `Slots.Count + channel`, so they never collide with blackboard slots.
  - Before each dispatch it stages the agent's row (raw → canonical, padding zeroed, −0.0 → +0.0)
    into that region, in place and in the job.
- **Faults.** NaN, ±Inf or a bool above 1 in a row make the request invalid, and the reading agent
  faults with `InvalidEncoding`.
- **The observer phase is unreachable in production** (no caller of `TryApplyObserverTransition`).
  Reactive interrupts come from reactive composites re-ticking their condition children; this is what
  the reactive-interrupt test exercises.

## 4. Verification

| Check | Result | Baseline |
|---|---|---|
| `AIBT.Runtime.Tests` | 710/710 | 710/710 |
| `AIBT.Editor.Tests` | 636/637 (1 new link.xml test, deviation 9): the known `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` fails because the project imports the samples | 635/636 |
| `AIBT.Integration.Tests` | 138/138 (119 + 19 new; includes the G1 simulation tests) | 119/119 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 |
| `AIBT.PlayMode.Tests` | 1/1 | 1/1 |
| `AIBT.CodeGen.ContractTests` (ABI v2, includes `ExpectedPublicAbiV2.txt`) | 6/6; the fixture is byte-identical (sha256 `226AE0E8…5C39122`) | 6/6 |
| `Verify-Static.ps1` | passed (work items: 174, schemas: 7) | 173 |
| Public API (`Get-FullPublicApi.ps1` against `perf-data/P10-009/api/public-api-p10009.txt`) | passed: no removals or renames; 25 additions (`perf-data/P10-010/api/added.txt`) | |
| `Documentation~/generated/api-reference-runtime.md` | regenerated (only the new members and the type count change) | |
| `Tools~/Verification/P2/CodeGen/Build-And-Verify.ps1` | **fails, not caused by this card**: its stubs lack `AIBT.AgentFrame` and `Unity.Jobs.IJobFor`, which generated catalogs have referenced since P10-004. Earlier evidence (P10-001, P10-009) records it as not run | |

**How the tests were run.** First run on a fresh Editor: each assembly as its own
`unity command run_tests … --async_tests true` run, polled with `test_status`.

**Pipeline bug found (`com.unity.pipeline` 0.7.0-exp.1).**

- **What happens.** A synchronous `run_tests` (as `Invoke-UnityTests.ps1` issues it) that hits its
  `-TimeoutSeconds` cancels its collector (`PipelineTestRunner.ExecuteSyncMode` → `InvalidatePreviousRun`).
  But the task the command awaits (`tcs` in `ExecuteTestsInternal`) is completed only by the
  collector's `RunFinished`, which a cancelled collector ignores. The command therefore never
  finishes.
- **Effect.** The serial command executor is blocked for good. `/api/status` still says `ready` and
  `/api/progress` says `active`, but every main-thread command (including `eval` and `test_status`)
  queues and times out.
- **Recovery.** A domain reload clears it. Here the Modules Editor had to be closed gracefully and
  reopened (owner's approval).
- **Avoid.** Run long assemblies with `--async_tests true`, or with a sync timeout well above the
  assembly's run time (Integration takes about 270 s now).

## 5. Measurement

**Setup.** IL2CPP Player, Release, High stripping, Unity 6000.6.9f1, Intel Core Ultra 9 275HX
(24 logical), Windows 11.
- **Project.** `C:\UnityProjects\_aibt_acceptance\P9Samples-6069`, with its manifest pointed at a
  copy of this working tree (`aibt-p10010-6069`) for the run; the manifest was restored afterwards.
- **Harness.** `Benchmarks~/Phase10/PlayerPerf`, set `sim-io` (described in that folder's README).
- **Scenarios.** 500 agents, `Spread(5)` (100 thinks per tick), jobs on, 200 warm-up and 1000
  measured ticks.
- **Inputs.** Every tick the game writes all 500 `PerfSense` rows (8 bytes each, 4000 bytes per tick)
  before `Step`.
- **Commands.** After `Step` the game reads the `PerfOrder` commands.
- **Pairs.** Each run measures every pair as ABBA: B is the G1 tree, D is its `io-*` twin with an
  input and a command.
  - `io-reactive16`: the first condition reads the input, and the held leaf gives one order on every
    think (100 per tick).
  - `io-loop16`: patrol's first step reads the input, and its third step gives one order per patrol
    pass, which comes to 5.5 per tick.
- **Runs.** 3 runs with default job workers (23) and 3 with `-JobWorkers 0`. Faults: 0.
- **Raw data.** CSVs and `runs.jsonl` are in `perf-data/P10-010/raw/il2cpp-20261117-110701`
  (23 workers) and `…-110722` (0 workers).
- **Table.** `perf-data/P10-010/sim-io-table.md`, made by `sim_io.py`, which is copied next to it.

Step is the `TreeSimulation.Step` call alone. Values are the median of the per-run values, with
min–max in brackets. "bytes/tick" is managed allocation inside `Step`.

| workers | scenario | Step µs median | Step µs p99 | bytes/tick | frame µs (mean) | commands/tick |
|---:|---|---:|---:|---:|---:|---:|
| 23 | `sim-shared-loop16` (B) | 52.1 (51.1–55.0) | 158.6 (149.4–167.5) | 0 | 70.0 | 0 |
| 23 | `sim-io-loop16` (D) | 53.0 (52.1–54.4) | 170.7 (165.7–186.9) | 0 | 74.2 | 5.5 |
| 23 | `sim-shared-reactive16` (B) | 195.8 (193.4–210.5) | 341.0 (328.3–362.3) | 0 | 223.5 | 0 |
| 23 | `sim-io-reactive16` (D) | 205.2 (203.6–209.6) | 363.1 (348.2–393.7) | 0 | 240.7 | 100 |
| 0 | `sim-shared-loop16` (B) | 82.7 (79.8–84.8) | 1092.1 (1068.9–1136.1) | 0 | 178.2 | 0 |
| 0 | `sim-io-loop16` (D) | 83.8 (82.2–86.7) | 1296.6 (1286.2–1344.8) | 0 | 192.8 | 5.5 |
| 0 | `sim-shared-reactive16` (B) | 2217.6 (2175.2–2255.1) | 2504.4 (2394.0–2701.8) | 0 | 2233.1 | 0 |
| 0 | `sim-io-reactive16` (D) | 2477.4 (2392.3–2507.0) | 2897.9 (2678.3–2990.6) | 0 | 2503.9 | 100 |

ABBA, D/B of the Step medians (first half, second half):

| workers | pair | D/B | D1/B1 | D2/B2 |
|---:|---|---:|---:|---:|
| 23 | loop16 | 1.02 | 1.01 | 1.06 |
| 23 | reactive16 | 1.05 | 1.04 | 1.06 |
| 0 | loop16 | 1.01 | 1.05 | 0.99 |
| 0 | reactive16 | 1.12 | 1.11 | 1.10 |

**Reading.**
- **Cost of inputs and commands.** With jobs on 23 workers they add 2–5 % to Step (about 1 µs for
  loop16 and 9 µs for reactive16, which does 100 input reads and 100 commands per tick). With
  0 workers they add 1 % and 12 %.
- **p99** rises by a similar share.
- **Allocation.** Step allocates nothing in any scenario, and the GC window stayed at 0 bytes.
- **Frame.** The frame mean includes the game's 500 row writes and the command read; that adds
  about 4–17 µs over Step.
- **Against G1.** G1 (P10-009 §5) measured `shared-loop16` at 56.7 µs (p99 221.2) and
  `shared-reactive16` at 316.6 µs (p99 432.5) on 23 workers. The same trees measure 52.1 and
  195.8 µs here.
  - This is a different session and the G2 package, and G1's own reactive16 range was 263.7–335.0.
  - The cross-session gap is therefore not a G2 speed-up claim.
  - The in-run ABBA ratio above is the like-for-like comparison.

**Two Player problems found on the way and fixed** (deviations 9 and 10):
- **Stripped attributes.** The first G2 Player bound its input with `TypeMismatch`: High stripping
  had removed `[AibtBurstValue]`/`[AibtValueField]`.
- **Unreadable scene.** Before that, every build whose catalog had an `[AibtInput]` node crashed at
  load with "level0 is corrupted! [Position out of bounds!]".

## 6. Deviations from the ADR

1. **The row is transcoded, not copied raw** (ADR decision 6: "raw `T` bytes, the existing snapshot
   payload format"). The dispatch reads canonical bytes (§3.2), so the container reorders fields,
   zeroes padding and normalises −0.0 to +0.0. NaN, ±Inf and bools above 1 fault the reading agent
   (`InvalidEncoding`) instead of reaching the node.
2. **One bulk copy per step plus per-dispatch staging.** The ADR has nodes read the game array.
   Instead, `Step` copies the bound rows once into a group-owned buffer (§1), and each dispatch
   stages its row from that buffer. This keeps game safety handles out of jobs and lets the game
   write right after `Step`.
3. **No generator staging in the agent-frame entry** (ADR G2 step 2 / D3). It is not needed: the
   job body is runtime code (`AgentFrameCoreV1.RunAgent`). The generator change is a binding-id
   table (the catalog had no binding ids) plus the `AibtInput` short form, both additive.
4. **Quota overflow faults the agent** (the ADR says "the node sees Failure"). Every non-Success
   dispatch result faults the agent (`NativeLifecycleMachineV1.TryCompleteDispatch`).
   `AgentFault.Cause = CapacityExceeded` says why; commands already emitted in that think are kept.
5. **The observer phase cannot be exercised** (ADR proof item "inputs read in Observer"). It has no
   production caller; the reactive-interrupt test uses a reactive composite re-ticking an input
   condition.
6. **Old catalogs.** The ADR's ABI table says `CatalogTooOld` "only when they use inputs/commands".
   Actually: inputs → `CatalogTooOld`; commands → still discarded as in G1 (no behaviour change
   for G1 programs); the program still runs in place. "Regenerate catalogs" means rebuilding the
   analyzer, because catalogs are source-generated at compile time. The old-catalog proof wraps the
   generated executor to hide the new interface.
7. **Every effect binding is a command channel.** The container has no effect queue, so built-in
   typed `CommandHandle<float>` etc. are command channels too.
8. **A fixture shard needs its own assembly.** A catalog set cannot select a shard from its own
   assembly (`AIBT5011`: "lacks a usable generated shard authority"). The test nodes live in
   `AIBT.CodeGen.P10010SimulationIo.Tests` and the benchmark nodes in `AIBT.Perf.IoNodes`.
9. **Player link.xml change (editor build code, not in the ADR).**
   - **Why.** `TreeGroup.TryBindInput` and `TryGetCommands` identify a registered payload type by
     reflection over `[AibtBurstValue]` and `[AibtValueField]`. A High-stripping IL2CPP Player had
     both attributes removed, so the bind returned `TypeMismatch`. The stripped
     `AIBT.Perf.IoNodes.dll` had no trace of them.
   - **Fix.** `AibtLinkXmlProcessor` now also preserves every `[AibtBurstValue]` type and the two
     attribute types (`Editor/PlayerBuild/AibtLinkXmlProcessor.cs`, internal, additive).
   - **Test.** `AibtLinkXmlProcessorTests.PreservesRegisteredValueTypesAndTheAttributesTheSimulationContainerReads`.
   - **Not affected.** Built-in payloads (`float`, …) never needed it.
10. **Benchmark harness: one MonoBehaviour per file.**
    - **Symptom.** With `PerfIoShard` in `PerfCatalog` (even reduced to one `SnapshotReadHandle<float>`
      condition), every Player, Mono and IL2CPP alike, crashed loading `level0` with
      "Position out of bounds".
    - **Cause found.** The Unity build logged "Script attached to 'Perf' … is missing" and wrote
      `PerfFrameEnd` without its `runner` field (32 instead of 44 bytes), which the Player still
      reads. In a fresh Editor session that component loads as a null (missing) script.
    - **Why only that script.** `PerfFrameEnd` was declared in `PerfClock.cs` together with
      `PerfFrameBegin` and `PerfFeeder`. Unity 6000.6 stores such classes as MonoScripts embedded
      in the scene, and this one failed to resolve. Without the input field the same scene loads.
    - **Workaround.** Moving the three MonoBehaviours to files named after their classes fixed it.
    - **Not explained.** Why an `[AibtInput]` node in another assembly breaks that one embedded
      script was not found. The stripped `AIBT.Perf.dll` is identical with and without the input,
      and so is its script-type table.
    - **Classification.** Recorded as a Unity-side finding; see §7.

## 7. What G3 must know

- **External actions are refused.** `SimulationProgramMapV1.TryBuild` returns `Unsupported` for
  `AsyncOperation`/`Completion` bindings, and the dispatch adapter still refuses them in container
  mode. G3 replaces that refusal.
- **The container has no effect queue** (`EffectFree = -1`). Commands go through
  `SimulationAgentRoutinesV1.TryReserve`/`TryCapture`, keyed by the program map's command channels.
  Async starts and cancels publish `NativeBurstDispatchCommandV2` with other kinds, which `TryCapture`
  currently treats as `TypeMismatch` (it accepts `Effect` only). G3 must route them.
- **The quota is per channel per think, and a teardown has its own quota**
  (`ResetCommandQuota` at think and at teardown). Async starts need their own capacity rule.
- **Completions.** The binding-id table (`IGeneratedBindingIdentityV1`) gives every catalog binding
  ordinal an id, so completion channels can be named the same way as inputs and commands.
- **Wake.** Wake on change compares canonical rows per slot (`LastSeen`). A completion arriving
  should wake its agent the same way (`_wakeScratch`, due selection in `BeginStep`).
- **Unchanged rules.** Hosts must stay unchanged, ABI v2 must stay byte-identical, and the old
  executor wrapper (`OldExecutor` in the tests) is the pattern for proving old-catalog behaviour.
- **Testing.** Run long Editor test assemblies with `--async_tests true` (§4, pipeline bug).
- **Player builds.** Anything resolved by reflection at bind time must survive stripping (deviation
  9). G3's completion payloads go through the same `SimulationValueLayoutV1`.
- **Benchmark scenes.** Keep one MonoBehaviour per file (deviation 10). The cause of the embedded
  MonoScript failing next to an `[AibtInput]` catalog is open. If a consumer scene shows "level0 is
  corrupted", check the build log for "Script attached … is missing" first.
