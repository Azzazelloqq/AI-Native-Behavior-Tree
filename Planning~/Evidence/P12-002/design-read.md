# Indexed input channel — independent design read (P12-002)

Date: 2026-11-24. Read-only review of `C:\UnityProjects\Modules\Assets\AIBT` (branch
`feature/aibt-consumer-workflow`). Paths below are relative to that folder. **[fact]** = read in the code
or docs at the cited line; **[judgement]** = my opinion. Nothing was run (no Unity, no tests).

## 0. Where the brief disagrees with the code

1. **Copy length.** `BeginStep` does not copy `Capacity` rows: it copies the first `HighWaterIndex`
   rows (`Runtime/Simulation/TreeGroup.cs:927-932`, `_highWater * state.Stride` at :931). The
   `Capacity` requirement is a bind/validate rule (:818, :898). [fact]
2. **Staging is per node dispatch, not per think.** Every dispatch of a node with a snapshot binding
   stages the row again into that node's live region: job path `Runtime/Integration/AgentFrame/AgentFrameCoreV1.cs:328-337`,
   in-place path `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:1815-1827`. This
   includes teardown dispatches (Abort/Exit) and observer re-evaluations. It is also called by wake
   (`TreeGroup.cs:959`, `:972`) and the trace (`TreeGroup.Debug.cs:684`). [fact]
3. **Short array result.** After `TryReserve` grows past the bound array, `Step` returns
   `InvalidArgument` (`TreeGroup.cs:898`; pinned by `Tests/Integration/Simulation/TreeSimulationIoTests.cs:581-582`).
   ADR-P10-008 says `InputMissing` for this (`Documentation~/decisions/ADR-P10-008-simulation-container.md:143`).
   Doc is stale; code is consistent. [fact]
4. **Wake check scope.** It does not compare "for agents that are not due" generally: only for alive agents
   that are neither scheduled this tick nor already woken (`TreeGroup.cs:586-592`). Also only if the
   agent has thought at least once since wake was turned on (`Seen`, :970). [fact]
5. **ADR-P10-008 has another design for this.** Its provisional slot for squad data is a Shared-scope
   blackboard context (`TryCreateSharedContext` / `TryAssignSharedContext` / `TryWriteShared`,
   ADR-P10-008:557-562). P12-002 records that this stays the provisional slot
   (`Planning~/Tasks/P12/P12-002-indexed-input-channel.md:20`). After P12-002 the ADR text should say both
   routes exist. That is not a protected file. [fact + judgement]

## 1. How a channel row travels today

**Binding** (`TreeGroup.cs:811-836`): `Usable()` (not stepping, not disposed; :207-212). Then the channel
id must be one some node reads (`_map.FindInput`, :816-817, else `UnknownChannel`). Then
`rows.IsCreated && rows.Length >= Capacity` (:818, else `InvalidArgument`). On the first bind of a new `T`,
a raw-to-canonical leaf table is built by reflection and written into the group's shared `_leaves`
(:821-829, `TypeMismatch` on mismatch). The game array is kept as `NativeArray<byte>` without copying or
owning it (:831, `InputState.Game` :70). `EnsureStaging` allocates an owned `Staging` of
`Capacity*stride` bytes (only if the size changed) and publishes it into the native channel record
`SimulationInputChannelV1 {Rows, RowStride, RowCount = Capacity}` (:324-338;
`Runtime/Simulation/SimulationNativeV1.cs:13-22`). Unbind clears `Game`/`Bound` (:839-851).

**Validation** (`TreeSimulation.cs:158-164` → `TreeGroup.ValidateInputs` :882-901). This runs before
`_stepping` is set and before anything runs. Every channel the program reads must be bound
(`InputMissing`). With collection checks, `CheckReadAndThrow` on the game array's safety handle must pass
(:888-897). A disposed array, or one a game job is still writing, gives `InputMissing`. The array must
still be ≥ `Capacity` (:898). One failing group refuses the whole step. [fact]

**Copy** (`TreeGroup.BeginStep` :907-933, called for every group not torn down, `TreeSimulation.cs:168-169`).
This is the only read of the game's array during a step: a raw memcpy of `_highWater*stride` bytes into
`Staging` (:931). The copy is raw `T` bytes. Canonicalisation happens later, per staging call. Order in
`Step`: BeginStep → teardown (:170-181) → thinks (:183-189). So removed/restarting agents' teardown
dispatches already read **this** step's rows. [fact]

**Staging per agent** (`SimulationAgentRoutinesV1.TryStage`, `SimulationNativeV1.cs:171-202`). It zeroes
the destination, then returns zeros (success) when the channel is unbound **or `agent.Row >= RowCount`**
(:179). Otherwise it copies each leaf from `Rows[agent.Row*RowStride + RawOffset]` and normalises -0.0
(:180-200). It returns false only for internal mismatches (size/leaf bounds). The callers turn false
into `InvalidHandle` (agent fault) (`AgentFrameCoreV1.cs:333-336`). `agent.Row` is the per-slot field
`SimulationAgentNativeV1.Row` (`SimulationNativeV1.cs:123`), set once to the slot index when the entry
is created (`TreeGroup.cs:299`). The job gets a by-value copy of the entry (`TreeGroup.cs:696`). It
reads `Inputs` through the shared `UnsafeList` pointer, so it sees the staged rows. Non-canonical values
(NaN, bool > 1) are copied as they are, and the dispatch refuses them: the reading agent faults with
`InvalidEncoding` (`SimulationNativeV1.cs:167-169`; `Documentation~/simulation-container.md:127-130`). [fact]

**Wake on change.** `TrySetWakeOnChange` allocates per channel `LastSeen` (`Capacity*CanonicalSize`
bytes) and `Seen` (`Capacity` bytes) and clears `Seen` (`TreeGroup.cs:859-876`, `:340-357`). At the
start of each think (after `TryBeginUpdateAt`, before the agent runs) `RecordSeenInputs` stages the
agent's **canonical** row of every wake channel into `LastSeen[slot*size]` and sets `Seen=1`
(:618, :951-962). For an alive agent that is neither scheduled nor woken, `InputChanged` stages the current
row into `_wakeScratch` and byte-compares it with `LastSeen`. The first difference wakes the agent with
`ThinkReasons.InputChanged` (:586-592, :964-978). `TryAdd` clears `Seen` for the slot (:397, :942-949).
Per agent it stores one canonical row per wake channel. The compare is value-based, so A→B→A between
thinks does not wake. [fact]

**Commands and actions** do not use `Row`. They are per-slot native logs in the same
`SimulationAgentNativeV1` entry (`Commands`, `Payload`, `Actions`, `Events`; `SimulationNativeV1.cs:119-138`).
They are collected in slot order (`CollectCommands` :983-988, `CollectActionEvents` :1070-1087) and
decoded with an `AgentHandle(slot, generation)` (:1040, :1078). Completions find the slot through
`ActionHandle.Agent.Index` with a generation check (:1239, :1252). They are outputs keyed by slot. Nothing
about them is shared between agents. [fact]

**Add / remove / reuse.** `TryAdd` takes the lowest free slot, resets the instance and clears `Seen`
(:368-404). It does not touch any input row: a reused slot reads whatever the game left in row `i`.
`TryRemove` marks `PendingRemoval` (:411-419). Teardown runs in the next step after the copy (:530-541),
and the slot becomes free after that step. `TryReserve` grows `Staging` and wake storage, keeping old
`LastSeen`/`Seen` (:316-321, :348-354). The game must rebind a longer array (:898). [fact]

**Where "row = slot" is assumed** [fact]: `TreeGroup.cs:299` (Row = slot), :327 and :337 (staging and
RowCount sized by Capacity), :818 and :898 (game length ≥ Capacity), :931 (copy `_highWater` rows),
:343-347, :959 and :973 (wake storage and offsets by slot), `SimulationNativeV1.cs:179-180` (row address),
the doc comments `TreeGroup.cs:798-799` and `SimulationNativeV1.cs:9-10`, ADR-P10-008:245 and :300, and
`simulation-container.md:111`.

## 2. API shapes for "row chosen per agent by an index"

Common runtime core for every shape [judgement, grounded in §1]: give `SimulationInputChannelV1` an
optional `NativeArray<int> RowOf` (owned, `Capacity` long) and a mode byte. `TryStage` computes
`row = mode==Indexed ? RowOf[agent.Row] : agent.Row` and keeps the existing `row >= RowCount → zeros` rule.
Size the staging by the rows length, not by Capacity. `TryStage` is the single routine used by job,
in-place, wake and trace. With this change all five readers follow the index, and in-place/job equality
holds by construction.

### A. Second bound array of indices (game-owned)
- Surface: overload `TryBindInput<T>(InputChannel<T>, NativeArray<T> rows, NativeArray<int> rowOfSlot)`.
  `rowOfSlot[slot]` is the row. Existing `TryUnbindInput` and `TrySetWakeOnChange` work unchanged.
- Runtime: `InputState` gets `GameIndex`. `ValidateInputs` adds CheckRead on it, `Length >= Capacity`, and
  a range check. `BeginStep` copies `rows.Length*stride` bytes and `_highWater*4` bytes of indices.
  `EnsureStaging` gets a rows-length variant. `GrowSimulationStorage` grows `RowOf`.
- Cost at 500 agents: about 2 KB index memcpy, R*stride rows memcpy (50 squads × 32 B = 1.6 KB), a
  500-int range pass, one extra load per staging call. Memory: `Capacity*4` + `R*stride` per channel.
- Easy: same model the game already uses ("write by `handle.Index`, between steps"). Several channels can
  share one index array (bind the same `NativeArray<int>`). A game job can write it, and the existing
  CheckRead gives `InputMissing` if that job is still running. Hard: no "unset" state, because a fresh
  array is all 0, which is a valid row. A reused slot keeps the previous occupant's index. After
  `TryReserve` the indices must be rebound, as rows are today.

### B. Per-agent setter on the group (runtime-owned indices)
- Surface: `TryBindInputRows<T>(InputChannel<T>, NativeArray<T> rows)` (indexed mode) and
  `TrySetInputRow<T>(AgentHandle, InputChannel<T>, int row)` (+ optional `TryGetInputRow`). It could have
  a bulk form like `TryWakeMany` (`TreeGroup.cs:462-476`).
- Runtime: owned `RowOf` per channel. `TryAdd` resets it to "none" (-1) next to `ForgetSeenInputs`. The
  setter checks the generation and `row < rows.Length`. Rebinding a shorter rows array must re-scan live
  agents (O(Capacity), at bind time). No per-step index copy or validation.
- Cost: O(1) per membership change. Per step only the rows memcpy. Memory: `Capacity*4` + `R*stride`.
- Easy: safe slot reuse (reset on add), stale handles are refused, real "unset", and errors surface at the
  call that caused them. Hard: one managed call per agent per change. N channels for the same squad
  need N calls each (or variant B′: one per-agent index for the group, used by every indexed
  channel; this cannot express two levels such as squad and platoon). The game cannot write it from a job.

### C. Group-level single row
- Surface: `TryBindGroupInput<T>(InputChannel<T>, NativeArray<T> oneRow)` or
  `TrySetGroupInput<T>(channel, in T value)`. Runtime: `row = 0` for every agent. No index storage.
- It serves squads only if squad = group. A group is one program plus its own slots
  (`TreeSimulation.cs:90-114`). Job batching is per group and needs `dueCount >= MinimumJobBatch`, default
  64 (`TreeGroup.cs:628`, `SimulationTypes.cs:108`). 50 squads × 10 agents × 1/5 due ≈ 2 due per group per
  tick, so everything runs in place. That loses the jobs target. Each group also has its own tables and
  spread counter. [fact for the rules, judgement for the consequence]
- Easy: trivial API. It is A with all indices 0, or B with every agent set to 0. Not enough alone.

### D. Index taken from a field of another channel
- Surface: e.g. `TryBindInput<T>(channel, rows, InputChannel<int> indexChannel)` or a field selector.
- Blocker [fact]: a channel can only be bound if some node of the program reads it
  (`TreeGroup.cs:816-817`, `UnknownChannel`). Channel ids are fixed strings in node code
  (`ADR-P11-002-native-node-library.md:142-146`). So an index channel must also be read by a node, which
  forces tree/node changes on the consumer. A field selector inside a struct row needs a field-id
  lookup on the leaf table (`SimulationLeafV1` has no field id, `SimulationNativeV1.cs:29-35`). It also
  creates an order dependency between two channels' staging. Rejected.

### E. Shared index set
- Surface: `TryBindRowIndex(name/handle, NativeArray<int>)` plus channels bound "by index set X".
  It generalises A for several channels at once. It adds a new handle type for something A already gets
  by passing the same array twice. [judgement] Not worth a new concept now. It can be added later.

## 3. Semantics to fix (options; my pick in **bold**, judgement)

| Topic | Options | Pick and reason |
|---|---|---|
| Index type | `int` / `ushort` / `uint` | **int**: negative works as "none", and it matches `TryWakeMany` (`NativeArray<int>`). |
| Owner (A) | game-owned, not copied / copied at BeginStep | **copied at BeginStep** into owned `RowOf`. This is the same reason rows are copied: jobs never touch game memory, and the game may write again after `Step` returns (`TreeGroup.cs:800-804`). |
| Negative index | refuse / fault / zeros | **zeros** ("no squad"). This matches unbound or out-of-range staging (`SimulationNativeV1.cs:10-11`, :179). The node sees the default `T` (author can add an `IsValid` bool). |
| Index ≥ rows.Length, live agent | refuse step / fault agent / zeros / clamp | **refuse step with `InvalidArgument` in `ValidateInputs`**. It is a binding error like a short array (:898) and nothing runs. Faulting via `TryStage` false would report `InvalidHandle` (`AgentFrameCoreV1.cs:336`), a misleading cause, and would fault teardowns too. Silent zeros hide bugs. Clamp gives wrong data. |
| Index of non-alive slot | check / ignore | **ignore** (out of range → zeros). Otherwise a removed agent's stale index blocks the world when a squad array shrinks. Teardown of a `PendingRemoval` slot then reads zeros or its old row. |
| When game writes | between steps only (any call during Step returns `Busy`, :210) | Same as today. The rows and indices seen by a step are the values at `Step` entry. |
| Rows length | ≥ Capacity / ≥ 1 / any ≥ 0 | **any ≥ 0, created**. Not tied to Capacity, so no rebind on `TryReserve` for rows. The indices array (A) stays ≥ Capacity. |
| Rows copy size | whole array / `max index+1` | **whole bound array**. Document that cost scales with the array the game binds, not with agents. |
| Wake: row changes | value compare via index (as today) / per-row version | **value compare through the index, unchanged code** (`InputChanged` calls `TryStage`). Per agent still `CanonicalSize` bytes. A per-row version scheme would need a canonicalising pass per row, because staging holds raw bytes (:931) with padding. It would also change semantics (A→B→A wakes). |
| Wake: index changes | wake iff value differs / always wake / never | **wake iff the canonical value differs**. This falls out of the same compare, with no extra state. No implicit wake from the setter (B). |
| "No per-agent copy" inside runtime | — | Holds for the game. Inside, the runtime still copies per dispatch (snapshot ABI) and per wake channel per agent (`LastSeen`), as today. Rows are copied once per step, not per agent. |
| Add/remove/reuse | A: game must write `rowOfSlot[handle.Index]` on add / B: reset to -1 on add | A keeps today's rows hazard. B removes it. |
| Mode switching | rebind switches slot↔indexed / refuse | **rebind switches**. `LastSeen` is kept, so the next compare may wake once, which is acceptable. |
| Job safety | — | The job reads only owned `Staging` and `RowOf` through the `_inputs` `UnsafeList`, exactly as `Rows` today (`TreeGroup.cs:334-337`, `:107`). The main thread writes them only in BeginStep and between steps. `RunJob` completes synchronously (:703-704). **Unclear in code:** how collection checks treat a `NativeArray` reached through an `UnsafeList` inside a job. The new array has the same status as the existing `Staging`, which the job tests already exercise. |
| Determinism in place vs jobs | — | One routine (`SimulationNativeV1.cs:158-161`), same staged bytes, slot-order thinks (`TreeGroup.cs:581`). It is deterministic if indices are staged before thinks. |
| Debug/trace/MCP | value only / + row | The `Input` trace fact already shows the selected row's value (`TreeGroup.Debug.cs:681-690`). **Add `AgentTraceRecord.InputRow`** (additive public property; -1 = slot mode), and `"row"` in MCP `read_agent_trace` (`MCP/McpSimulationTools.cs:174-178`). Values over 16 bytes are truncated in traces (`SimulationDebugTypes.cs:62`), and a squad struct with two `Float3` is 24 B. |

## 4. Does anything make this worse than it looks, or force a protected change?

**No protected area is forced** [fact]:
- `TryStage`, `SimulationInputChannelV1` and `SimulationAgentNativeV1` are `internal` in `Runtime/Simulation`.
  Generated code only schedules `AgentFrameJobRunnerV1.ExecuteAgent<…>` (`CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs:1432`,
  `:1474`). Staging is runtime code (`AgentFrameCoreV1.cs:334`). CodeGen~ only knows `SnapshotReadHandle<T>`
  as a binding kind (`BurstNodeGenerator.cs:579`, `:1113-1116`). No catalog regeneration is needed.
- `ExpectedPublicAbiV2.txt` lists `AIBT.Burst` types. A search for `Simulation|TreeGroup` in it finds
  nothing. Node code is unchanged: the same `[AibtInput("id")] SnapshotReadHandle<T>` reads a slot row or
  a squad row, and the mode is chosen by the game at bind time.
- The compiled program format is untouched. The program map only reads binding metadata
  (`SimulationProgramMapV1.cs:197-215`).
- `Documentation~/specifications` has no text on container rows (search for row/input channel/TreeGroup
  finds nothing relevant).

**Costs and traps that are real:**
1. **The generated API reference must be regenerated.** `Tests/Editor/Documentation/McpDocumentationGeneratorsTests.cs:233-234`
   compares `Documentation~/generated/api-reference-runtime.md` with the live assembly, and `:245` checks
   that every public member is covered. This is not protected, but the editor test fails without it. [fact]
2. **Library nodes and blackboard-key trees cannot see squad facts.** No package node reads `[AibtInput]`
   (only test, benchmark and spike nodes do, per a search). A library node may assume only Tree-scope keys
   (`ADR-P11-002-native-node-library.md:148-150`). A tree that feeds a library node from a key needs a
   custom node that copies input to key: a per-agent copy inside the tree at think time. P12-002 already
   asks the consumer (`P12-002…md:30`). This is the main gap against "shared blackboard". [fact + judgement]
3. **Read-only.** Nodes cannot write squad facts. ADR Q7 (ADR-P10-008:755-756) is still open. [fact]
4. **Wake can defeat the spread schedule.** If target and formation anchor are one channel with wake on,
   a moving anchor changes the row every tick and wakes every member every tick: 500 thinks/tick instead
   of ~100 with `Spread(5)`. Wake is per channel (:859-876), so the fix is to split into a target channel
   (wake) and an anchor channel (no wake). With A they can share one index array. Document this. [judgement]
5. **One channel id = one mode per group.** Channel ids are fixed per node type (ADR-P11-002:142-146).
   A node cannot read both "own row" and "squad row" of the same id. Two ids need two node fields. [fact]
6. **Copy cost scales with the bound rows array.** The per-step memcpy (:931 analogue) is proportional to the
   array the game binds. A game that binds a 10 000-row world table pays for it every step. [judgement]
7. **Squad = group (C) loses jobs**, see §2C. [fact + judgement]
8. **Doc drift** (§0 items 3 and 5). [fact]

Nothing I found makes the indexed route impossible without a protected change.

## 5. Recommendation (judgement)

**Build A: `TryBindInput<T>(InputChannel<T> channel, NativeArray<T> rows, NativeArray<int> rowOfSlot)`**.
It is an additive overload and the existing two-argument method is untouched. Semantics:
- rows of any length; `rowOfSlot` ≥ Capacity;
- both copied at BeginStep into owned buffers;
- negative index → zeros;
- index ≥ rows.Length of an alive slot → `Step` refused with `InvalidArgument`;
- non-alive slots never checked;
- wake = existing canonical compare through the index;
- trace and MCP report the row.

Why A:
- It is the consumer's own wording ("input channel indexed by squad, with a per-agent squad index").
- It is the model they already use for rows: arrays by `handle.Index`, written between steps, rebound
  after `TryReserve`.
- Several squad channels can share one index array with no extra API.
- The game can compute membership in its own jobs, and the existing CheckRead gives a clean
  `InputMissing` if that job is still running.
- It adds one public member, plus the trace property.
- The extra per-step work is about 2 KB of memcpy and 500 integer compares.

Rejected:
- **B** is the close runner-up. It is safer on slot reuse and has a real unset state, but needs one call
  per channel per agent, cannot be written from game jobs, and adds 2–3 members. Prefer B only if the
  consumer says membership changes rarely and comes from managed code, or if the owner values
  reset-on-add over job-friendliness. B can be added later on top of the same `RowOf` core.
- **C** as a separate API: A with zeros covers it. As the squad model it breaks job batching.
- **D**: needs a node to read the index channel (`TreeGroup.cs:816-817`).
- **E**: a new concept with no gain over A now.
- **Clamp or zeros on bad indices**: hides game bugs.

Tests that pin A (`Tests/Integration/Simulation/TreeSimulationIoTests.cs` or a new `TreeSimulationIndexedInputTests.cs`):
1. `IndexedInput_MembersReadTheirRow_NotTheirSlotRow(useJobs)`: 3 squads × N agents; each node's observed value equals `rows[rowOfSlot[slot]]`.
2. `IndexedInput_Digests_IdenticalAcrossPathsWorkerCountsAndRuns`: the Population digest (:313) with an indexed channel and membership changes.
3. `IndexedInput_Bind_Refusals`: indices uncreated or < Capacity → InvalidArgument; wrong `T` → TypeMismatch; unknown id → UnknownChannel; rows length 0 accepted.
4. `IndexedInput_OutOfRangeIndexOfAliveAgent_RefusesStep_NothingRuns`: `LastTick` unchanged, `TotalUpdates` 0.
5. `IndexedInput_NegativeIndex_ReadsZeros`.
6. `IndexedInput_OutOfRangeIndexOfRemovedAgent_DoesNotRefuse_TeardownReadsZeros`.
7. `IndexedInput_GameWritesAfterStep_SeenOnlyNextStep`: the copy semantics for rows and for indices.
8. `IndexedInput_WakeOnChange_RowChange_WakesExactlyTheNotDueMembersOfThatRow(useJobs)`.
9. `IndexedInput_WakeOnChange_IndexChange_WakesOnlyWhenValueDiffers`: same-valued rows do not wake.
10. `IndexedInput_SharedIndexArray_AcrossTwoChannels`: one array, two channels, consistent rows.
11. `IndexedInput_TryReserve_RequiresIndicesRebind_RowsNot`.
12. `IndexedInput_Rebind_SwitchesSlotAndIndexedModes`: values follow the mode; unbind → InputMissing.
13. `IndexedInput_IndicesWrittenByRunningGameJob_StepReturnsInputMissing` (collection checks only).
14. `IndexedInput_NonCanonicalRow_FaultsEveryReaderOfThatRowOnly(useJobs)`.
15. `IndexedInput_Step_AllocatesNothingAfterWarmUp(useJobs)`: extends :405.
16. `IndexedInput_CallsDuringStep_ReturnBusy`.
17. `IndexedInput_Trace_InputFactCarriesRowAndValue`, plus an MCP `read_agent_trace` "row" check.
18. Existing `Hosts_StillRefuseSnapshotBindings…` (:801) and the docs generator test stay green after regeneration.

## 6. Open questions for the consumer or the owner

1. Must squad facts be readable by library nodes or blackboard-key trees? If yes, the indexed input is not
   enough: it needs a copy-to-key node or the Shared-scope route (ADR-P10-008:557-562).
2. Do nodes ever write squad facts (ADR Q7)? The indexed input is read-only.
3. Bad index of an alive agent: may one game bug refuse the whole step, or should only that agent fault
   (which would need a precise fault cause)?
4. Where is membership computed: managed code on change (favours B) or game jobs or ECS every tick (favours A)?
5. More than one grouping level (squad and platoon) on one agent? A supports it per channel. B′ does not.
6. Row sizes: are squad rows over 16 bytes (traces truncate), and how many squads and rows per group?
7. Should a membership change wake the agent by itself, or only when the visible value differs?
8. Is reading another squad's row (an enemy squad) needed? That is ADR Q3 and needs a node-context call,
   which is protected ABI.
9. Owner: is amending ADR-P10-008 (two routes for group data, stale `InputMissing` text) part of P12-002?
