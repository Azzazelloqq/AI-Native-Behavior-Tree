# Read-only game arrays in Burst nodes: independent design read (P12-003)

Reader: independent design read, 2026-11-25. Read-only. Basis: committed content of `Assets/AIBT` at `8396202`
(release 0.2.0, branch `feature/aibt-consumer-workflow`). Paths are relative to `Assets/AIBT`; line numbers are
those at `8396202`. **[fact]** = read in the code or a committed document at that line; **[judgement]** = my
opinion or estimate; **[unclear]** = not settled by the code I read. No Unity, tests or benchmarks were run.

The request (consumer, verbatim in the brief): bind a read-only `NativeArray` that is not indexed by slot to a
group or the container, readable from Burst nodes during `Step`, with the guarantees inputs have.

## 0. Where the brief and the code disagree, or need precision

1. **"A node sees only its own agent's row"** is out of date since 0.2.0. A node sees one row per input channel
   per dispatch: the agent's slot row, or with an indexed channel the row `rowOfSlot[slot]` that the *game*
   chose (`Runtime/Simulation/SimulationNativeV1.cs:175-185`, `SelectRow`). Many agents may share one row, so
   small fixed-size data that is not per agent (a zone id, a squad target, a world constant as a one-row channel)
   is readable today **[fact]**. What does not exist is random access: an index chosen *by the node*, a length
   query, or anything larger than one fixed-size value per channel. "Partial" is therefore accurate **[judgement]**.
2. Snapshot reads exist only on `BurstEnterContext`, `BurstTickContext` and `BurstObserverContext`
   (`Runtime/Nodes/Contracts/BurstNodeContracts.cs:467`, `:508`, `:562`; contract
   `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt:150`, `:327`, `:336`). Abort has only
   `TryBeginCancel` (`BurstNodeContracts.cs:534`), Exit has no access call (contract `:173`) **[fact]**. Yet rows are
   staged for every dispatch, teardown included (`Planning~/Evidence/P12-002/design-read.md:12-16`) **[fact]**.
3. "Managed in the Editor": the generated `ExecuteImmediate` carries `[BurstCompile]` (generator
   `CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs:1448-1449`), but the in-place adapter calls it from C#, and the
   catalog-set class itself is emitted without `[BurstCompile]` (`:1413`), which Burst's direct-call needs
   **[fact for the emitted text; unclear whether a consumer class adds it]**. The P12-002 numbers agree: in place
   ≈ 41 ms per step against ≈ 0.72 ms in jobs at 500 agents, 100 thinks
   (`Planning~/Evidence/P12-002/README.md:119-130`) **[fact]**. So in the Editor the in-place path is much slower,
   and at 41 ms per step it is over a 33 ms tick. At the consumer's load, jobs are the path that counts
   **[judgement]**.
4. An unbound input does not read zeros in the container: `Step` is refused with `InputMissing`
   (`Runtime/Simulation/TreeGroup.cs:951-956`). Zeros are staged only internally (unbound in teardown, negative
   index) (`SimulationNativeV1.cs:194-203`) **[fact]**.

## 1. What a native node body can read today, and why nothing else

**Handles and context calls [fact].** The ABI's typed handles are `BlackboardRead/Write/ReadWriteHandle<T>`,
`SnapshotReadHandle<T>`, `CommandHandle<T>`, `AsyncOperationHandle<TStart,TCancel>` and `CompletionHandle<T>`, each
8 bytes, `Sequential` with pack 4, with an internal constructor (contract `:32`, `:357` and neighbours; for example
`BurstNodeContracts.cs:317-321`). Reads go through
`TryBeginBlackboardRead`, `TryBeginSnapshotRead`, `TryBeginConsume`, plus time and random (`TryGetTimeMicroseconds`,
`TryNextUInt32`, `TryNextFloat32`) on the Enter/Tick/Observer contexts (contract `:143-158`, `:323-347`). Every
read returns an opaque `BurstValueReader`. The generated `BurstAccess.TryRead*` then reads it leaf by leaf through
`BurstGeneratedRuntimeBridge.TryReadValue(ref reader, fieldOrdinal, elementIndex, out scalar)` (contract
`:224-234`; generator `:1188-1196`, `:1360-1369`). Here `elementIndex` is a *leaf of a composite value* (for example
Float3.x/y/z), not an array index (generator `:2157-2174`). Configuration is per program and constant
(`TryReadUInt32/Float32/...` over `BurstConfigurationReader`, contract `:206-223`).

**What the toolchain forbids, and where [fact].**
- Usage analyzer, inside node callbacks: any symbol in `UnityEngine`, `Unity.Jobs`,
  `Unity.Collections.LowLevel.Unsafe`, `System.Reflection` or `System.Threading.Tasks`
  (`CodeGen~/AIBT.CodeGen/BurstNodeUsageAnalyzer.cs:700-705`); any `Unity.Collections.Native*` type and Burst
  `SharedStatic*` (`:667-683`); strings; allocations, lambdas, `stackalloc`, `&` and `*` (`:28-35`, `:80-88`,
  `:609-622`). It also forbids a direct `ctx.TryBegin*` call, so access must go through the generated `BurstAccess`
  with a declared binding field (`:59-71`; AIBT5006/5007/5008 in `AbiDiagnostics.cs:13-15`).
- Generator, node, configuration, memory and value types: pointer and function-pointer types, `IntPtr`, `string`,
  `Unity.Collections.Native*` and `UnityEngine.*` (`BurstNodeGenerator.cs:493-509`); managed types (`:521-526`);
  unregistered structs (`:529-530`); mutable static fields (`:239-243`); configuration fields other than
  bool/uint/ulong/float or a generic `*Handle` (`:486-491`). Handles are only the seven listed in
  `IsGeneratedHandle` (`:577-581`). A fixed-size buffer in an `[AibtBurstValue]` struct is a pointer-typed field
  in Roslyn, so it is refused by `:496` **[judgement from the rule; not tested]**.
- Specification: ABI v2 "adds no public constructor, native-container accessor, pointer accessor, ownership
  operation" (`Documentation~/specifications/burst-node-abi-v2.md:40-42`). Runtime may put *private* `NativeArray`
  views in carriers (`:128-133`), but the execution path must not use a global registry, a mutable static map,
  `SharedStatic`, a thread-local or a managed fallback (`:136-140`; ADR-P2-012 `:8-16`, `:96-104`).

**How `SnapshotReadHandle<T>` is bound, staged and read [fact].**
1. *Bind (game).* `TreeGroup.TryBindInput(channel, rows)` / `(channel, rows, rowOfSlot)`
   (`TreeGroup.cs:832-874`) type-checks `T` against the catalog value (`SimulationValueLayoutV1.TryBuild`, leaf
   table; `:876-899`) and keeps the game array without copying it.
2. *Validate (Step phase 0).* For each group, every channel must be bound. With collection checks on, it calls
   `AtomicSafetyHandle.CheckReadAndThrow` on the game arrays, and a disposed array or one a job still writes gives
   `InputMissing`. Lengths and indices are checked too (`TreeGroup.cs:951-979`, called at
   `TreeSimulation.cs:158-164` before `_stepping`).
3. *Copy (main thread).* `BeginStep` copies `HighWaterIndex × stride` bytes (slot rows) or the whole rows array plus
   the indices into group-owned `Staging` (`TreeGroup.cs:986-1019`). After that, jobs never see game memory
   (ADR-P10-008 amendment, `Documentation~/decisions/ADR-P10-008-simulation-container.md:781-792`).
4. *Stage per dispatch.* Before every dispatch of a node with a snapshot binding, `SimulationAgentRoutinesV1.TryStage`
   (`SimulationNativeV1.cs:194-226`) writes the selected row into the node's live binding region in canonical
   layout. It copies leaf by leaf and byte by byte and turns -0.0 into +0.0. NaN and infinities are copied as they
   are. The job path calls it at `Runtime/Integration/AgentFrame/AgentFrameCoreV1.cs:328-337`, the in-place path at
   `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:1815-1827`. Wake uses it at
   `TreeGroup.cs:1038-1063` and the trace at `TreeGroup.Debug.cs:681-691`.
5. *Read.* `TryBeginSnapshotRead` resolves the handle (frame, token, kind, phase) and opens a value session that
   copies the live region into `ValueStagingBytes` (`Runtime/Nodes/Contracts/BurstBindingBridgeCoreV2.cs:391-432`,
   `:1807-1850`). Each `TryReadValue` finds the leaf and validates it canonically (bool > 1, -0.0, NaN and Inf are
   `InvalidEncoding`, which latches and faults the agent; `:732-778`, `:2303-2360`). `TryCompleteValueRead`
   requires *every* leaf to have been read (`IncompleteValue`, `:780-806`).
6. *Job data.* The agent-frame job gets an opaque `AgentFrameJobDataV1 { UnsafeList<AgentFrameAgentV1> }`
   (`Runtime/Integration/AgentFrame/AgentFrameContractsV1.cs:51-54`). It is scheduled with a `default` dependency
   and completed inside `Step` (`TreeGroup.cs:718-723`). The job system sees no container in it, so no dependency
   tracking applies. The package's own staging arrays are reached through `UnsafeList`.
7. *Session capacity.* There is one value session per binding per frame (two for async), taken as the maximum over
   the catalog's cases (`Runtime/Integration/GeneratedDispatch/GeneratedBurstCatalogV2.cs:203-239`). Sessions are
   reset only when a transaction starts (`BurstBindingBridgeCoreV2.cs:1106`, `:1278`, `:1291`), and a completed
   read stays `ReadComplete` (`:803`). So reading the same snapshot binding many times in one callback runs into
   `CapacityExceeded` unless other bindings leave slack **[fact for the rule; a test should confirm the
   container's workspace size]**.

**Earlier decisions on array or pointer reads [fact].** ADR-P10-008 alternative 4, "a new node-context API with an
agent index and raw pointers to game arrays … `ref readonly T`", was rejected for G2. It needed new ABI v2 members
and a generator exception to the no-pointers/`Native*` rule, while snapshots gave the same data; a zero-copy read
"can be added later if a benchmark shows the row copy matters" (`ADR-P10-008:590-595`). Open question Q3 asked
whether nodes need *other* agents' rows ("needs a new context call", `:750-752`). The P12-002 decision defers
"reading another squad's row needs a node-context call (protected ABI)" to P12-003
(`Planning~/Evidence/P12-002/decision.md:71`). ADR-P2-012 rejected raw pointers in validation tokens and any global
lookup (`ADR-P2-012-burst-node-abi-v2.md:96-104`). The Shared-scope blackboard is still a provisional slot, not
built (`ADR-P10-008:561-566`, `:794-797`).

## 2. Realistic designs for "a node reads element k, with k computed by the node"

Load assumed throughout: 500 agents, `Spread(5)`, about 100 thinks per tick, jobs path at about 0.72 ms per step,
so ≈ 7 µs per think all-in (`ADR-P10-008:567`; `P12-002/README.md:126-127`) **[fact]**. Per-read costs below are
**[judgement]** until measured (§5).

### A. The game samples per agent into rows (no protected change)
- *Surface.* Node: an ordinary `[AibtInput("senses")] SnapshotReadHandle<Senses>` with, for example,
  `AheadWalkable`, `NearestThreatPos`, `NearestThreatDist` and `HasThreat`. Game: a Burst job per tick (its own
  DOTS code) fills `NativeArray<Senses>` by slot, completes it, then `Step`.
- *Protected changes.* None. Programs keep their bytes.
- *Can and cannot.* Any query whose index comes from data the game has (position, heading). The node cannot choose
  k from tree state (a branch, a memory field, a loop counter) unless that state is mirrored to the game through a
  command or row. No length query. One fixed-size answer per channel.
- *Cost.* Game job ≈ 500 × the query's work, parallel and off the AIBT path. For "nearest of 32", 16 000 distance
  tests is tens of µs in Burst **[judgement]**. AIBT pays one row staged per dispatch, as today. Memory is
  `Capacity × sizeof(Senses)`. Wake on change works per agent and stays meaningful.
- *Verdict.* This covers both of the consumer's examples today, at the lowest cost **[judgement]**.

### B. Query through an external action (no protected change)
- *Surface.* Node: `AsyncOperationHandle<CellQuery, Empty>` and `CompletionHandle<CellAnswer>`, `TryStart` with k.
  Game: after `Step`, read `GetActionEvents()`, answer `TryComplete<CellAnswer>(action, Succeeded, answer)`
  (`TreeGroup.cs:1261`). The completion wakes the agent at the next `Step`.
- *Can and cannot.* True node-chosen k, any element type, any data structure on the game side, including managed.
  Latency is one tick; a node cannot read several cells inside one think. The default is 2 actions per agent
  (`Runtime/Simulation/SimulationTypes.cs:156`).
- *Cost.* One managed `TryComplete` per query on the main thread, plus a payload layout per type (allocated once).
  At 100 queries per tick this is likely tens of µs **[judgement; unmeasured]**.
- *Verdict.* A fallback for rare, node-driven probes, not for scans.

### C. A fixed-size window in a row (no protected change; "indexed channel misused")
- *Surface.* A registered value struct with N named fields: a 5×5 walkability patch as 25 `byte` fields, or 32
  threats as `T0…T31` of `Float3`. It is filled per agent (slot channel), or per tile with an indexed channel
  (`rows` = 16×16 tiles of the 256×256 grid, `rowOfSlot` = the agent's tile).
- *Can and cannot.* Random access only *inside* the window, and only by a hand-written `switch` from k to the
  field. There are no arrays in values (fixed buffers are refused, §1). The node must read *all* leaves of the
  value each time (`IncompleteValue`, `BurstBindingBridgeCoreV2.cs:794-800`). There is no length beyond a count
  field, and no variable length.
- *Cost.* Every dispatch stages the whole value byte by byte (`SimulationNativeV1.cs:205-224`). A read costs one
  `TryReadValue` per leaf with a linear field lookup. For 96 leaves (32 × Float3) that is likely several µs per
  read in Burst, comparable to a whole think **[judgement]**. Wake compares the whole value. The trace keeps 16
  bytes (`Runtime/Simulation/SimulationDebugTypes.cs:106-117`). A 256-byte tile would mean 256 declared fields.
- *Verdict.* Works for windows of tens of leaves. It does not scale to a grid **[judgement]**.

### D. Row k of an existing input channel (ABI addition on the snapshot machinery)
- *Surface.* Node: an overload `ctx.TryBeginSnapshotRead<T>(SnapshotReadHandle<T> handle, uint row, out
  BurstValueReader reader)` plus `TryGetSnapshotRowCount(handle, out uint count)` on Enter/Tick/Observer, and the
  generated `BurstAccess.TryReadSnapshotRow(ref ctx, handle, uint row, out T)`. Game: the existing indexed bind; a
  grid is a channel of 65 536 one-byte rows.
- *Protected changes.* New lines in `ExpectedPublicAbiV2.txt` (three context types × two methods). Generator:
  BurstAccess overloads, and an analyzer rule extension for the new call. A section in `burst-node-abi-v2.md` in the
  form of the AIBT-042 addendum (`:1-16`). Binding kind stays `SnapshotRead` (3), so catalog metadata and
  `AccessLayout` do not change **[judgement: no new kind is serialized]**. Programs keep their bytes.
- *Internal changes.* The dispatch backing (`Runtime/Execution/Burst/Dispatch/BurstDispatchBackingV2.cs:7-46`) has
  no view of the simulation rows; today rows only arrive by staging. It needs a private read-only view of the
  channel's staged rows (allowed by spec `:128-133`), filled by the agent-frame core and by the adapter. The
  session rule (§1 item 7) must change: either recycle a session after `TryCompleteValueRead`, or size capacity
  for N reads. That is a change to specified transaction semantics (burst-node-abi-v1.md:1030-1051), so it is
  protected.
- *Can and cannot.* Node-chosen k, a length query, structs with canonical checks for free, and Q3's "another
  squad's row". There are no 2D indices (the node computes `y*w+x`, with w from configuration or a row).
- *Cost.* Per read: handle resolve, session, copy into staging, a per-leaf lookup with validation, the all-marked
  check. Likely 100–300 ns in Burst **[judgement]**. 32 reads per think ≈ 3–10 µs, which roughly doubles a think.
  Per step: the indexed copy of the whole rows array (64 KB for the byte grid) per group that binds it.
- *Verdict.* The smallest ABI delta that gives random access. Its cost model (a session per element) is wrong for
  scans **[judgement]**.

### E. A new array handle with a direct element read (ABI addition)
- *Surface (node).* `[AibtArray("walkable")] ArrayReadHandle<WalkCell> walkable;` (long form
  `[AibtConfigField(id,"GeneratedHandle",1), AibtArrayBinding(id, valueTypeId, version)]`), then generated
  `BurstAccess.TryReadElement(ref ctx, walkable, uint index, out WalkCell v)` and `BurstAccess.TryGetLength(ref ctx,
  walkable, out uint length)` on Enter/Tick/Observer. *Surface (game).* `TreeSimulation.TryBindArray<T>(ArrayChannel<T>
  channel, NativeArray<T> elements)` (container-level, see §3) plus `TryUnbindArray`. `T` gets the same type check
  as inputs (`SimulationValueLayoutV1`).
- *Protected changes.* Contract file: a new handle type, a new attribute, `TryReadArrayHandle` on the bridge (like
  `TryReadSnapshotHandle`, contract `:221`), `TryReadElement` and `TryGetLength` (on the contexts, or as bridge
  statics called by BurstAccess), and possibly a `BurstContextResult` member for out of range (contract `:117-140`,
  additive). `CodeGen~`: `IsGeneratedHandle` (`BurstNodeGenerator.cs:577-581`), binding validation (`:551-620`),
  metadata emitter (`GeneratedMetadataEmitter.cs:128`, `:348-349`), binding identity, short form
  (`ShortFormNormalization.cs`), and the analyzer's binding check. Internal enum: `ArrayRead = 7`
  (`NativeBurstDispatchBindingContractsV2.cs:6-15`). This kind enters metadata and the access-layout fingerprint
  *only for catalogs that use it*. Spec: a new section in `burst-node-abi-v2.md` and a sentence lifting
  "no native-container accessor" (`:40-42`) for this read. `package.json` gets a minor bump if the owner wants it.
- *Implementation.* The bridge resolves the handle as it does any handle (token, kind, phase). It then reads
  element k from a *private* read-only view in the backing: bounds check, per-leaf copy raw→canonical with the
  existing leaf table, canonical check of the leaves it read. There is no session, no staging and nothing done per
  dispatch.
  - Sub-variant **E1, copy:** the view is a container-owned copy made in `BeginStep`.
  - Sub-variant **E2, view:** the view aliases the game's memory for the duration of `Step` (§4).
- *Can and cannot.* Node-chosen k, a length, a struct element, a 2D grid by arithmetic (width from configuration or
  an input row), a variable-length list by binding `GetSubArray(0, count)` or a `NativeList.AsArray()` each tick.
  It cannot write, cannot take a pointer or `ref` to an element, and cannot iterate faster than one element per
  call.
- *Cost.* Per read ≈ handle check, bounds check and a few leaf copies: probably 20–60 ns in Burst **[judgement]**.
  32 reads per think ≈ 1–2 µs (+15–30% on a 7 µs think). Per step: E1 copies the array once per container
  (256×256 B = 64 KB, a few µs of memcpy **[judgement]**); E2 pays nothing. Memory: E1 is one copy of the array,
  E2 none.
- *Verdict.* The right shape if random access is built **[judgement]**.

### F. Raw `ref readonly T` / pointer / `NativeArray` in node code (ADR alternative 4)
This breaks `burst-node-abi-v2.md:40-42` and `:136-140`, the analyzer (`:667-705`) and the generator
(`:496-509`). It lets a node keep the reference past the step and gives no bounds or type check, and it was
rejected once already. Reject **[judgement]**.

### G. Shared-scope blackboard (ADR-P10-008's provisional slot)
These are fixed-size keys with no index, the same as a one-row channel, plus a spec addition and an unwired owner
(plan-2026-11-24 §1). It is not random access. Out of scope for this request **[judgement]**.

### Summary

| | k by node | length | grid 256² | list ≤ 32 | protected change | program bytes | per-read cost (judgement) |
|---|---|---|---|---|---|---|---|
| A sample per agent | no (game) | n/a | yes, by game | yes, by game | none | same | ≈ 0 extra |
| B action query | yes, +1 tick | game | yes | yes | none | same | managed main-thread call |
| C window row | inside window | no | no | awkward | none | same | µs per read (all leaves) |
| D row k of channel | yes | yes | as 64 K rows | yes | ABI, gen, spec, session rule | same | 100–300 ns |
| E1/E2 array handle | yes | yes | yes | yes | ABI, gen, analyzer, spec, internal kind | same | 20–60 ns |
| F raw pointer | yes | yes | yes | yes | breaks spec | same | ≈ 1 ns |

## 3. Semantics to fix for D and E (options; my pick is marked **[judgement]**)

| Topic | Options | Pick and reason |
|---|---|---|
| Copy or view | E1: copy at `BeginStep`. E2: alias game memory during `Step`. | **E1 by default.** It keeps the one safety argument the package already has: jobs never touch game memory (`ADR-P10-008:786-787`). At 64 KB it is a few µs. Revisit E2 only if the prototype shows the copy matters for arrays of MB size. A container-level bind means one copy, not one per group. |
| A game job still writes | refuse the step / wait / undefined | **Refuse with `InputMissing`**, as inputs do (`TreeGroup.cs:958-966`). This only works with collection checks on; in a Player both E1 and E2 race silently. Consider an additive `Step(tick, JobHandle)` or bind-time `JobHandle` that `Step` completes first; it would serve today's inputs too. |
| Cost of a 256×256 copy | bytes: ≈ 64 KB memcpy; Float32: 256 KB | Likely 3–30 µs on the main thread per step, against a ≈ 720 µs jobs step **[judgement]**. A per-element canonicalising loop in managed `BeginStep` would cost far more (Mono in the Editor): copy raw and check at read time. |
| Out-of-range index | fault the agent / zeros / clamp / return a code the node handles | **Fault the agent (latch) with a precise cause.** The node has `TryGetLength`, so out of range is a node bug. Zeros hide it and clamping gives wrong data. This needs either `BurstContextResult.OutOfRange` (an additive enum member, contract `:117-140`) or reuse of `InvalidHandle`, which misleads (the P12-002 decision rejected that kind of cause, `decision.md:58-64`). |
| Element types | scalars only / registered `[AibtBurstValue]` structs | **Registered values and built-in scalars**, with the same bind-time type check as inputs (`TreeGroup.cs:880-887`). No fixed buffers, no nested arrays. |
| Canonical form | validate on copy / validate per read / none | **Per read, only the leaves read.** That is the same rule as inputs: NaN/Inf/bool > 1 faults the reader with `InvalidEncoding`, and -0.0 reads as +0.0 (`SimulationNativeV1.cs:189-192`; `simulation-container.md:129-130`). It is one compare per float leaf. Validating on copy would cost O(array) per step and fault agents that never read the bad cell. |
| Determinism, in place vs jobs | — | Holds by construction if the read lives in the bridge, which both paths reach through the same generated `ExecuteImmediate` (generator `:1429`, `:1473`), like `SelectRow` today. ADR-P10-008's determinism sentence already includes "input array contents at each step" (`:552`); arrays join that list. |
| Wake on change | none / whole array / per element read | **None.** A whole-array version would wake every reader on any change. P12-002 measured wake at ≈ +1 ms per step for 40 extra thinks (`README.md:126-130`). Per-element tracking needs per-agent read sets. The game already has `TryWakeMany` (`TreeGroup.cs:481`) for the agents near a changed cell. |
| Trace and MCP | record each element read / record the binding only / nothing | **Binding-level only at first** (the channel is bound, plus its length, in the think record). Per-read events would need a new trace event kind: a `trace-v1.md` change, which is protected. State the debugging gap to the consumer. |
| Per group or container | group (like inputs) / container / both | **Container**, with each group resolving the channel id against its program map, and channels not read by a program ignored. A grid is world data, E1 copies once, and several programs share it. Group-level is an easy later overload. |
| Unbound array | refuse step / length 0 / zeros | **Refuse with `InputMissing`**, consistent with inputs (`TreeGroup.cs:955`). Length 0 is a legal *bound* state (an empty threat list). |
| Teardown dispatches | — | Abort/Exit have no read call, so nothing is staged and nothing changes for teardown. Do not add the read to Abort/Exit. |
| Phase visibility | Enter/Tick/Observer | Same as snapshot reads. Observers (reactive conditions) can read too, so a reactive condition may depend on grid cells it does not declare up front; re-evaluation timing is the existing rule. |
| Length and alignment | — | `uint` index and length, matching ABI sizes (`burst-node-abi-v1.md:452-454`). Arrays over 2³²−1 elements are refused at bind. |

## 4. Burst and safety specifics

- **What is safe today, and why [fact].** Game memory is touched only on the main thread: validation
  (`CheckReadAndThrow`, `TreeGroup.cs:958-966`) and then the copy (`:1009-1018`). The job reads only package-owned
  arrays nested in an `UnsafeList` the job system cannot see (`AgentFrameContractsV1.cs:51-54`). It is scheduled
  with no dependency and completed before `Step` returns (`TreeGroup.cs:722-723`; `TreeSimulation.cs:11-14`).
- **E1 (copy)** keeps exactly this structure. No new safety argument and no bypass. The copy should be a raw
  `MemCpy` (as `NativeArray<byte>.Copy` is at `:1011-1017`), not the byte-and-leaf loop of `TryStage`.
- **E2 (view)** gives the job an alias of game memory that the job system does not track. That is a deliberate
  bypass of dependency tracking, defensible only under three invariants: (i) the read check at validation, which
  runs only with collection checks; (ii) no game code runs during `Step` (members return `Busy`, and there are no
  callbacks; `TreeSimulation.cs:11-14`); (iii) every AIBT job is completed before return. To avoid tripping the
  checks inside the job, store the view as an internal pointer plus length taken on the main thread
  (`GetUnsafeReadOnlyPtr` after the check). Do not store the game's `NativeArray` with its safety handle.
  **[unclear]** Whether the game's own `NativeArray`, nested and read from a worker thread, would raise a safety
  error: the package does this today with *its own* staging arrays (`AgentFrameCoreV1.cs:334-335` reading
  `input.Rows`), which suggests not. A game array allocated `Temp` or `TempJob` and one with a concurrent
  *read-only* game job must both be tested.
- **One routine for both paths.** Put the element read in `BurstBindingBridgeCoreV2`, reading from a private view
  in `BurstDispatchBackingV2`. The agent-frame core (job) and the generated adapter (in place) fill that view from
  the same group/container state, as `TryStage` is shared today (`SimulationNativeV1.cs:163-167`). Burst compiles
  it in the job. In the Editor the in-place path runs it as C# (§0.3), with identical results.
- **What the generator must emit.**
  - Recognition of the new handle in configuration (`AllowedConfigurationField` already admits generic `*Handle`
    names, `:486-491`, but `IsGeneratedHandle` must list it, `:577-581`).
  - A binding descriptor of the new kind in catalog metadata and binding identity.
  - A `TryReadArrayHandle` configuration-decode call.
  - Per element type `T`, `BurstAccess.TryReadElement` and `TryGetLength` for Enter/Tick/Observer, decoding leaves
    through the existing leaf codec.
  - The analyzer's rule that the handle argument is a declared binding field of the callback's configuration
    (AIBT5006), and a phase check (AIBT5007).
  - Container metadata: the channel id of each array binding, as `IGeneratedBindingIdentityV1` provides for inputs
    (`AgentFrameContractsV1.cs:39-44`).
- **Versioning.** Follow the AIBT-042 precedent (`burst-node-abi-v2.md:1-16`): additive public members, and
  `AbiVersion` stays `2`. The consequences:
  - Catalogs that do not declare the handle must regenerate byte-identically: same fingerprints, same generated
    source. Pin this with the existing CodeGen fixtures (`Tests/Fixtures/P2/CodeGen`).
  - A catalog that uses it does not compile against an older runtime (missing types), which fails closed.
  - A program using new nodes is new content. Programs that do not use them keep their bytes, provided the
    compiled "access tables" (`compiled-program-v1.md:62`) do not enumerate binding kinds in a way the new kind
    perturbs **[unclear: I did not trace whether the compiled access tables record catalog binding kinds]**.
  - Prepared assets compiled before need no migration **[judgement]**.
  - Bumping `AbiVersion` to 3 would invalidate every catalog, which is not warranted for an additive read.

## 5. What a prototype should measure before any decision

Smallest form: a test-only harness in the container (no public API, no generator change), so a hand-written
generated-style node calls an internal bridge read. Measure on the P12-002 harness shape: 500 agents, `Spread(5)`,
30 Hz, jobs and in place, 8 interleaved rounds with rotating order, and the load logs the owner requires for this
machine.

**Variants**
1. **A (baseline, no change):** a game Burst job computes per-agent answers into a slot-row channel, then `Step`.
2. **E1:** container copy at `BeginStep`, then direct element reads.
3. **E2:** pointer view taken after the read check, with no copy.

Optional and cheap: **D's session-per-read cost** with one microbenchmark of `TryBeginSnapshotRead` plus leaves.

**Workloads**
- *W1, "is the cell ahead walkable":* a 256×256 `byte` grid, each think reads 3 cells (ahead, left, right) at
  `y*256+x` from the agent's position row.
- *W2, "nearest of 32 threats":* a `Float3` list bound as `GetSubArray(0, count)` with count 0–32 varying per tick;
  each think scans all elements.
- *W3, size sweep:* the same W1 node with 64 KB, 1 MB and 4 MB arrays (byte grid and Float32 grid), for the copy cost.

**Numbers**
- `Step` median and p99 per variant and path; `BeginStep` copy time against array size.
- ns per element read in jobs (Burst) and in place (Editor).
- For A, the game job time plus `Step` time, so that totals are compared rather than AIBT's share alone.
- GC allocation per step from the `GC.Alloc` recorder (`GetAllocatedBytesForCurrentThread` reports 0 in this Editor).
- A digest of commands identical between in place and jobs.
- Safety behaviour, each a test: a running write job gives `InputMissing`; a running read job is fine; disposed gives
  `InputMissing`; `Temp`/`TempJob` arrays behave; no safety exception inside the job with collection checks on.
- One Player build with checks off, for the read costs.

**Rule-outs**
- E2 is out if any safety error appears in jobs, or if it needs safety checks suppressed beyond an internal pointer,
  or if it gains under ≈ 2% of a step at 64 KB over E1 (then the copy is free, and isolation wins).
- E1 is out as the default if the copy of a realistic array is > 5% of a jobs step (≈ 35 µs).
- The whole feature is unjustified for the stated examples if A's total (game job + `Step`) is within noise of, or
  below, E1 for both W1 and W2. Then only node-driven queries remain, and B covers rare ones.
- D is out for scans if a read costs > 150 ns in Burst.

## 6. Recommendation

**Do not build it now in the form asked [judgement].** The two examples (walkability ahead, nearest threat) are
per-agent queries whose index comes from the agent's position, which the game already has in its own Burst jobs.
Design A answers both today, with no protected change. It is probably cheaper in total, because the game's job is
parallel and simple, while an element read inside the tree pays a handle check per element. It also keeps wake on
change meaningful. The request's real core, a node choosing k from *tree state*, is not shown by these examples.

**If the prototype or the consumer shows a node-driven case** (probing along a path, filtering a variable list by
tree-specific criteria, scanning a zone map with a node's own parameters), build **E1**:
- `ArrayReadHandle<T>` with `TryReadElement` and `TryGetLength`, direct (no session).
- Container-level `TryBindArray`, a copy in `BeginStep`.
- Per-read canonical check; out of range faults the agent with a precise cause.
- No wake; binding-level trace only.
- An ADR naming the contract lines, the generator, analyzer and metadata changes, the internal binding kind 7, and
  the spec section. That ADR also lifts `burst-node-abi-v2.md:40-42` for this read.

**Rejected [judgement]:**
- **F**: breaks the spec and was rejected before.
- **D as the element read**: the right size for "another squad's row" (Q3), but its session-per-read model and the
  session-capacity change make it slow and semantically invasive for scans. If Q3 is wanted, E's handle can carry
  that case too, as an array bound to the same rows.
- **E2 as the default**: an untracked alias of game memory for a few µs saved per step.
- **C beyond small windows** and **G**: not random access.

**Tell the consumer, until then:**
1. Compute per-agent answers in your own Burst job before `Step` and bind them as a slot-row channel: walkable
   ahead/left/right as flags, the nearest threat as position, distance and a valid flag, or a small local patch as
   a value struct of up to a few tens of fields.
2. Use the indexed channel for zone- or squad-level facts, one row per zone or squad.
3. For rare node-chosen lookups, use an external action answered with `TryComplete<TResult>`, with one tick of
   latency.
4. Use `TryWakeMany` for agents affected by a grid change.
5. Complete your writing jobs before `Step`. With collection checks on, an unfinished writer refuses the step.

## 7. Open questions only the consumer or the owner can answer

Consumer:
1. List the concrete queries. For each: is k derived only from the agent's position and heading (A suffices), or
   from tree state the game does not know?
2. Sizes and types: grid dimensions, element type (byte, float, struct), how many arrays, maximum list length;
   how often each changes.
3. Must a node read several elements within one think (a scan), or is one probe per think enough (B suffices)?
4. Out-of-range: a node bug (fault), or a legitimate "off the map" that should read a default?
5. Are the arrays written by jobs that may still run when `Step` is called? Would you pass a `JobHandle` to `Step`?
6. Do you need wake when a cell an agent cares about changes, or is your own `TryWakeMany` enough?
7. Do you need to see element reads in the trace or MCP, or is "which arrays were bound" enough?
8. Are shipping Player builds run with collection checks off? (Then nothing detects a racing writer, for inputs or
   arrays.)
9. Must library or shared nodes read these arrays, or only your own nodes?

Owner:
1. Accept an additive ABI v2 change (new handle, context or bridge reads, an `OutOfRange` result member) under the
   AIBT-042 precedent, or require a version bump?
2. Is copy-per-step isolation (E1) a principle, or may a measured view (E2) be chosen?
3. Should an additive `Step(tick, JobHandle)` (or a bind-time writer handle) be decided now for inputs and arrays
   alike?
4. Should Q3 ("another squad's row") be folded into the same ADR, through E, rather than decided separately?
5. Should the in-place Editor path's speed (≈ 41 ms per step at this load, so not Burst) be treated as a separate
   issue? It already exceeds the 33 ms tick.
