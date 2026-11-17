# ADR P10-008: A GameObject-free simulation container stepped by the game

- Status: Proposed (stage G0 of the re-plan approved by the owner, 2026-11-16). Nothing in this ADR
  is implemented. The owner decisions listed in "Owner decisions" are open.
- Decision ID: not assigned
- Task: `Planning~/Tasks/P10/P10-008-simulation-container-design.md`
- Review document for the game team: `Documentation~/proposals/simulation-container-review.md`

## Context

A game project (Unity 7, co-op with host authority, a fixed 30 Hz simulation tick driven by the
game, up to 500 AI agents, gameplay tested in EditMode, presentation can be off) asked for ten
things the package does not provide today. The production path is built around one
`MonoBehaviour` per agent:

- An agent is a `ProductionTreeHost : MonoBehaviour`
  (`Runtime/Integration/ProductionTreeHost.cs:16`). It drives itself from Unity's `Update`
  (`:485`) or is driven by a `ProductionTreeScheduler : MonoBehaviour`
  (`Runtime/Scheduling/Production/ProductionTreeScheduler.cs:36`) that runs only from its own
  private `Update` (`:380`). `DriveOneUpdate` is internal (`ProductionTreeHost.cs:497`).
- Time comes from a `Func<long>` clock in microseconds, by default `Time.timeAsDouble`
  (`ProductionTreeHost.cs:342`), read once per logical update.
- Cadence is `SchedulingProfile.UpdateCadence` counted on the scheduler's own frame counter
  (`ProductionTreeScheduler.cs:385`, `:516`). There is no phase.
  `RequestUrgentUpdate` (`:328`) only re-orders hosts that are already eligible: `CompareDue`
  (`:998`) ranks `Urgent` but eligibility is `NextEligibleFrame <= _frame` (`:416`).
- The managed shell is not needed by the native core. What an agent needs natively is listed in
  "Per-agent state" below; it contains no `UnityEngine.Object`.

The JobsAgentFrame path (ADR P10-002, P10-004, P10-007) already runs one agent's whole update in a
Burst job (`Runtime/Integration/AgentFrame/AgentFrameCoreV1.cs:128`) over a per-batch agent table
that the scheduler rebuilds from hosts every frame
(`Runtime/Scheduling/Production/ProductionAgentFrameDriverV1.cs:69-90`), and completes the job
inside the same call (`:89-90`). Frame-by-frame equality with `Immediate` is tested
(`Tests/Integration/NativeRuntime/JobsPolicyEquivalenceTests.cs`).

## The game's requirements and their status today

Status: **there** (met as is), **partial** (the core exists, the shell around it does not fit),
**missing**, **conflict** (today's behaviour contradicts the requirement).

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 1 | N agents of one program in one container without GameObjects; several programs and containers side by side; add/remove at runtime with stable handles; no static/global state | **partial / conflict** | Agent = `MonoBehaviour` (`ProductionTreeHost.cs:16`), handle = object reference. `ProductionTreeProgram` is already a shareable program (`Runtime/Integration/ProductionTreeProgram.cs:7`) created without a GameObject (`ProductionTreeAsset.TryCreateProgram`, `ProductionTreeAsset.cs:57`). Static state: `s_nextTreeInstanceId` (`ProductionTreeHost.cs:43`) used for `TreeInstanceId`, which seeds random streams (`Documentation~/specifications/time-and-random-v1.md`, combiner input) and identifies operations (`Runtime/Core/Identity/OperationId.cs:8`); owner-id counters in seven native owners (for example `NativeCommandAsyncOwnerV1.cs:1214`, `NativeSnapshotOwnerV1.cs:342`); `NativeTreeProgramCache` (`NativeTreeProgramCache.cs:13`) and `NativeTreeAgent.s_sharedWorld` (`NativeTreeAgent.cs:33`) belong to the component facade only. |
| 2 | Explicit `Step(tick)` from the game; no own `Update`; time in `uint` ticks; waits and cooldowns in ticks | **missing / partial** | No public step (`ProductionTreeScheduler.cs:380` private, `ProductionTreeHost.cs:497` internal). The machine takes any monotonic `long` time per update (`NativeLifecycleMachineV1.cs:330-355`); Timeout/Cooldown compare it with deadlines `now + duration` (`NativeDecoratorPolicyV1.cs:91`, `NativeLifecycleMachineV1.cs:1275`, `:1297`, `:1315`). Durations are authored as `durationMicroseconds` (`RuntimeBuiltInCatalogAuthority.cs:34`). The time spec fixes microseconds from a host-defined epoch (`time-and-random-v1.md:5-8`), so `tick × tickMicroseconds` is a conforming clock. There is no built-in Wait leaf. |
| 3 | Per-agent think rate and phase; wake early on an event or a changed fact | **partial** | Cadence without phase on a frame counter (above). An urgent request does not make a not-yet-due host eligible (`ProductionTreeScheduler.cs:416` vs `:1006`). |
| 4 | Bulk facts: nodes read game data by agent index from native arrays filled once per tick; no per-agent blackboard writes; readable from Burst | **missing (ABI is there)** | Facts go in per host and key through `TryWriteExternalTreeValue<T>` with a `Temp` `NativeArray` per write (`ProductionTreeHost.cs:428-458`). The ABI already has the right read: `SnapshotReadHandle<T>` / `AibtSnapshotBindingAttribute` (`Runtime/Nodes/Contracts/BurstNodeContracts.cs:248`, `:317`), readable in Enter, Tick and Observer (`:467`, `:508`, `:562`), and snapshot payloads are raw `T` bytes checked by size, alignment and a same-process type token (`Runtime/Integration/Snapshots/NativeSnapshotBuilderV1.cs:73-117`). The production adapter rejects every binding kind except blackboard and `EffectCommand` (`Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:667-689`). |
| 5 | Game-defined command structs written by nodes into a native buffer the game reads after the step | **partial** | Nodes emit through `CommandHandle<T>` / `TryEmit` for any registered value type (`BurstNodeContracts.cs:254`, `:327`; generator `BurstNodeGenerator.cs:1123-1126`, `:1389-1390`). Delivery is a per-host managed ring (`ProductionTreeEffectQueue`) whose payload is a closed set Int32/Float32/Float3 (`ProductionTreeEffects.cs:6-12`; generator `BurstNodeGenerator.cs:1528-1533`) and carries no agent id. |
| 6 | Long actions completed from outside; preemption visible to the game as a cancel | **missing (ABI is there)** | ABI: `AsyncOperationHandle<TStart,TCancel>`, `CompletionHandle<T>`, `TryBeginStart`/`TryBeginConsume`/`TryBeginCancel` (`BurstNodeContracts.cs:337-356`, `:470`, `:509-511`, `:534`). The native ledger `NativeCommandAsyncOwnerV1` has start, consume, cancel and fault-cancel with a stored fault-cancel payload (`NativeCommandAsyncOwnerV1.cs:451`, `:518-564`, `:566-626`, `:628`). The sample shows the node pattern (`Samples~/BurstNodes/Runtime/PublicBurstNodeSample.cs`, `AsyncWriteActionNode`). Production rejects AsyncOperation/Completion bindings (adapter `:667-689`), and JobsAgentFrame lists them as a fallback reason (`:333-338`). |
| 7 | Reactive interrupts (like UE observer aborts) | **partial** | Native `ReactiveSequence`/`ReactiveSelector` re-evaluate on every update (`NativeLifecycleMachineV1.cs:358`, `:1677-1692`): a higher-priority branch preempts at the agent's next think. `TryApplyObserverTransition` (`:568`) has no production caller. Latency is therefore the think interval unless the agent is woken. |
| 8 | EditMode without Play Mode, same results; same inputs → same result on one machine | **partial / conflict** | Native core and Burst jobs do not need Play Mode, but nothing drives them outside `Update`. Determinism conflicts: `TreeInstanceId` comes from a process-wide counter, so a second run in the same process gets different random seeds and operation ids; bounded-budget admission measures stopwatch microseconds (`ProductionTreeScheduler.cs:132`, `:496`). JobsAgentFrame vs Immediate equality is tested. |
| 9 | No allocations per step; jobs complete inside the step; nothing runs between steps; one thread, never during a step | **partial** | Zero GC per tick on the native path is measured (`Documentation~/performance.md:21`, B per agent-tick = 0). JobsAgentFrame completes in the call (above); `PipelinedJobs` crosses frames by design. `NativeTreeAgent` restart adds a component per run (`NativeTreeAgent.cs:233`), which allocates. |
| 10 | Trace of one selected agent; "why is agent N doing X" (active path, last results) | **partial** | Every host owns a trace channel (`ProductionTreeHost.cs:121`, created at `:318`). `TryCopyActiveNodes` is internal (`:224`) over `NativeLifecycleMachineV1.CopyActivePath` (`:318`). MCP finds hosts with `FindObjectsByType` (`MCP/McpRuntimeTools.cs:163`). No per-node last result is stored outside the trace. |

### Per-agent state the native machine needs

Read from `ProductionTreeHost.TryBootstrap*` (`:261-340`), `BeginAgentFrame` (`:833-901`),
`AgentFrameAgentV1` (`AgentFrameCoreV1.cs:91-118`) and `SchedulingPolicyDriver.TryCreateAgent`
(`Runtime/Scheduling/SchedulingPolicyDriver.cs:49-66`):

1. **Lifecycle machine** (`NativeLifecycleMachineV1`): per-agent copies of the node records,
   child indices, node bindings and configuration bytes (all immutable program data, duplicated per
   agent today), plus the mutable instance memory, frames, generations, cooldown flags, parallel
   branch states and one control record.
2. **Generated dispatch state** (`GeneratedTreeDispatchAdapterV2`, internal): Tree/Agent-scope
   value bytes and versions, and per generated leaf one slot with about twenty native arrays
   (`AgentFrameSlotV1`, `AgentFrameCoreV1.cs:43-70`: configuration, memory, random state,
   bindings, completions, commands, operations, transaction, published commands, workspace).
3. **Update bookkeeping** (plain fields of the host): update id, time of the open update, open /
   suspended / ready flags, steps this update and the step limit, last root result, last failure.
4. **Optional**: trace recorder and channel; effect queue; agent-frame trace/effect logs.

**Separable from `MonoBehaviour`.** Only these parts of `ProductionTreeHost` touch Unity's object
model: `Update` (`:485`), `OnDestroy` (`:1189`), `isActiveAndEnabled` checks (`:499`, `:558`,
`:836`), `Debug.LogError(..., this)` in `Fail` (`:1177`) and the default clock (`:342`). Everything
else (bootstrap, update begin/end, `RunSegment`, agent-frame begin/finish, teardown with
`TreeStopped` abort, disposal) is plain logic over the native state above.

## Decision

1. **A new container, not a new mode of the old shell.** Add `TreeSimulation` (namespace
   `AIBT.Simulation`, assembly `AIBT.Runtime`): a plain C# object owned by the game, holding
   groups of agents of one `ProductionTreeProgram` each, stepped only by `Step(tick)`. It reuses
   the native machine, the generated dispatch state and `AgentFrameCoreV1` unchanged in semantics.
   `ProductionTreeHost`, `ProductionTreeScheduler`, `NativeTreeAgent` and the managed
   `BehaviorTreeAgent` are untouched in behaviour.
2. **Shared core, not copied code.** G1 moves the `MonoBehaviour`-free parts of
   `ProductionTreeHost` (state list above) into one internal per-agent core type that both the host
   and the container use; the host keeps its public API and delegates. Equality is proved by the
   existing host and JobsAgentFrame equivalence tests. (Owner decision D2.)
3. **Agents live in pooled slots of a group.** An agent is a slot index plus a generation. A group
   has an explicit capacity; slots are allocated once (at `Reserve`) and reused after removal, so
   a steady population allocates nothing after warm-up. Restart after root completion resets the
   slot in place (new instance identity, same handle).
4. **Time is the game's tick.** The container passes `tick × TickMicroseconds` to the machine as
   the update time. This is the existing time contract (signed microseconds from a host-defined
   epoch), so no spec or semantics change; durations authored in microseconds take effect at tick
   granularity. A tick-native authoring unit is an open question for the game (Q1).
5. **Think rate, phase and wake are container scheduling**, not tree semantics: an agent thinks on
   tick `t` when `t % interval == phase`, or when it was woken since its last think. Due agents run
   in ascending slot index, groups in creation order.
6. **Inputs are snapshot bindings.** A node reads the current agent's row of a game-owned
   `NativeArray<T>` through the existing ABI v2 `SnapshotReadHandle<T>`; the snapshot binding id is
   the input channel id. The container stages the row (raw `T` bytes, the existing snapshot payload
   format) before the node body runs. No ABI v2 line changes.
7. **Commands are the existing `CommandHandle<T>`**, delivered to a per-group native buffer of
   `Command<T>` records ordered by agent and emission order, valid until the next step.
8. **External actions are the existing async ABI** (`AsyncOperationHandle` + `CompletionHandle`),
   wired into production for the container only. The container owns the operation ledger per agent,
   publishes `Started`/`Cancelled` events, accepts completions through handles, wakes the agent on
   completion, and guarantees a `Cancelled` event for every started action that ends without the
   node consuming a completion (including authors who forget to cancel in `Abort`).
9. **Determinism by construction.** Identity is container-local (instance ids from a container
   counter in add order, random roots from a container seed), admission is never time-measured,
   jobs on or off give equal results, nothing runs outside `Step`.
10. **No exceptions on hot paths.** `Step`, wake, completion and output reads return a
    `SimulationResult` code; per-agent faults stop that agent and are reported, never thrown.
    Setup calls throw only `ArgumentNullException` / `ObjectDisposedException` for programmer
    errors.

## Public API sketch (items 1–7)

Compilable-looking signatures; bodies omitted. Namespace `AIBT.Simulation` unless stated.
Everything here is additive public API outside the `AIBT.Burst` namespace that the ABI v2
manifest covers (`Tests/Editor/CodeGen/Contracts/PublicBurstAbiContractTests.cs:109-114`).

```csharp
namespace AIBT.Simulation
{
    /// <summary>Result of every container call that can fail at runtime. No hot-path call throws.</summary>
    public enum SimulationResult : byte
    {
        Success = 0,
        /// <summary>Called while Step runs (re-entrancy or another thread). Nothing changed.</summary>
        Busy,
        Disposed,
        /// <summary>The handle's slot is free or holds a newer generation.</summary>
        StaleHandle,
        /// <summary>Group capacity, command quota or action capacity would be exceeded. Nothing changed.</summary>
        CapacityExceeded,
        /// <summary>Step(tick) with tick <= the previous tick.</summary>
        TickNotIncreasing,
        /// <summary>The program reads an input channel that is not bound, or the bound array is shorter than Capacity.</summary>
        InputMissing,
        /// <summary>The bound T does not match the binding's declared type (id, version, size, alignment, type token).</summary>
        TypeMismatch,
        /// <summary>The program needs a feature its catalog was generated without (regenerate the catalog).</summary>
        CatalogTooOld,
        /// <summary>Completion for an action that already ended (cancelled, completed, or its agent removed).</summary>
        StaleAction,
        InvalidArgument,
    }

    /// <summary>Container settings, fixed for its lifetime.</summary>
    public struct TreeSimulationSettings
    {
        /// <summary>Microseconds per game tick (33 333 for 30 Hz). Machine time = tick × this. Must be ≥ 1.</summary>
        public uint TickMicroseconds;
        /// <summary>Root seed of every agent's random streams (spec time-and-random-v1). Same seed + same calls = same results.</summary>
        public ulong RandomSeed;
        /// <summary>
        /// Run due agents in Burst jobs (JobsAgentFrame core) when a group has at least
        /// <see cref="MinimumJobBatch"/> due agents in a step; otherwise in place on the calling thread.
        /// Results are identical either way; only time differs.
        /// </summary>
        public bool UseJobs;
        public int MinimumJobBatch;          // default 64, the P10-007 threshold
        /// <summary>Per agent and think; exceeded → that agent faults with NativeUpdateStepLimitExceeded.</summary>
        public uint MaximumStepsPerThink;    // default 10 000 (was the host default until 2026-11-17, finding F1 of P10-011)
        public static TreeSimulationSettings Default(uint tickMicroseconds);
    }

    /// <summary>
    /// One simulation world owned by the game. Holds groups; runs only inside <see cref="Step"/>.
    /// Threading: every member is called from one thread at a time and never while Step runs
    /// (calls during Step return Busy). Step completes all jobs it schedules before returning;
    /// nothing runs between steps. Several containers may coexist; they share no state.
    /// Disposal: disposes groups and their native storage, not the programs (the game owns them).
    /// </summary>
    public sealed class TreeSimulation : IDisposable
    {
        public TreeSimulation(in TreeSimulationSettings settings);

        public TreeSimulationSettings Settings { get; }
        /// <summary>The tick of the last successful Step; 0 before the first.</summary>
        public uint LastTick { get; }
        public bool IsStepping { get; }
        public int GroupCount { get; }

        /// <summary>
        /// Adds a group of agents that all run <paramref name="program"/>. Allocates the group's
        /// storage for <see cref="TreeGroupSettings.Capacity"/> agents now (not during Step).
        /// The program must outlive the group; disposing it first is refused by the program.
        /// </summary>
        public SimulationResult TryAddGroup(ProductionTreeProgram program, in TreeGroupSettings settings, out TreeGroup group);

        /// <summary>
        /// Marks every agent of the group removed. Their teardown (Abort with TreeStopped, Cancelled
        /// events) runs in the next Step; those outputs are the group's last, readable until the Step
        /// after, when the group's storage is released and the TreeGroup is disposed.
        /// </summary>
        public SimulationResult TryRemoveGroup(TreeGroup group);

        /// <summary>
        /// Advances the world to <paramref name="tick"/>. Order inside one call:
        /// 1. pending removals tear down (abort, Cancelled events);
        /// 2. queued action completions are applied in the order they were reported (each wakes its agent);
        /// 3. per group (creation order), the due set = scheduled ∪ woken, in ascending slot index;
        /// 4. each due agent runs one update at time tick × TickMicroseconds (jobs or in place);
        /// 5. outputs (commands, action events, root completions, faults, trace) are merged in
        ///    ascending slot index, then emission order, and stay readable until the next Step.
        /// Returns TickNotIncreasing / InputMissing / TypeMismatch / Busy / Disposed without running
        /// anything. Per-agent failures do not fail the Step: they appear in the group's faults.
        /// Allocates no managed memory after warm-up.
        /// </summary>
        public SimulationResult Step(uint tick);

        public void Dispose();
    }

    public enum RootCompletionPolicy : byte
    {
        /// <summary>Default. The agent restarts as a fresh instance at its next think (same handle).</summary>
        Restart = 0,
        /// <summary>The agent keeps its root result and stops thinking until TryRestart.</summary>
        Stop = 1,
    }

    public struct TreeGroupSettings
    {
        /// <summary>Slots allocated at creation. Add beyond it fails with CapacityExceeded; grow with TryReserve.</summary>
        public int Capacity;
        /// <summary>Default think interval in ticks (≥ 1) for Add without an explicit rate.</summary>
        public uint DefaultThinkInterval;
        public RootCompletionPolicy RootCompletion;
        /// <summary>Commands one agent may emit per think, per command channel. Exceeding it fails that node's
        /// dispatch with CapacityExceeded (the node sees Failure), the existing effect-capacity rule.</summary>
        public int CommandQuotaPerAgent;
        /// <summary>Concurrently started, not yet ended actions per agent.</summary>
        public int ActionCapacityPerAgent;
    }

    /// <summary>
    /// Stable identity of one agent. Index is the agent's slot: stable for the agent's whole life and
    /// the row index of every input array and of the agent's commands. After removal the slot is reused
    /// with Generation + 1, so an old handle is refused (StaleHandle) instead of addressing the new agent.
    /// Generation 0 is never issued (default(AgentHandle) is null).
    /// </summary>
    public readonly struct AgentHandle : IEquatable<AgentHandle>
    {
        public int Index { get; }
        public uint Generation { get; }
        /// <summary>Container-local group id; a handle used with another group is refused.</summary>
        public ushort Group { get; }
        public bool IsNull { get; }
    }

    /// <summary>
    /// N agents of one program inside one container. Owned by its container; invalid after the
    /// container or TryRemoveGroup disposes it. Same threading rules as the container.
    /// </summary>
    public sealed class TreeGroup
    {
        public ProductionTreeProgram Program { get; }
        public int Capacity { get; }
        public int Count { get; }
        /// <summary>One past the highest slot index ever used; input arrays must be at least Capacity long.</summary>
        public int HighWaterIndex { get; }

        /// <summary>Grows capacity (allocates; never during Step). Existing handles and indices are kept.</summary>
        public SimulationResult TryReserve(int capacity);

        // ---- 1. agents --------------------------------------------------------------------
        /// <summary>
        /// Adds an agent in the lowest free slot. Its first think is the first Step whose tick
        /// matches its schedule (or the next Step if woken). Nothing of the tree runs in this call.
        /// </summary>
        public SimulationResult TryAdd(out AgentHandle agent);
        public SimulationResult TryAdd(in ThinkSchedule schedule, out AgentHandle agent);

        /// <summary>
        /// Marks the agent removed: IsAlive is false at once and the handle is stale. Its teardown
        /// (Abort with TreeStopped, Cancelled events for running actions) runs at the start of the next
        /// Step; the slot is reusable after that Step.
        /// </summary>
        public SimulationResult TryRemove(AgentHandle agent);
        public bool IsAlive(AgentHandle agent);
        /// <summary>Restarts a stopped or running agent as a fresh instance at its next think (teardown first if running).</summary>
        public SimulationResult TryRestart(AgentHandle agent);

        // ---- 3. think rate, phase, wake ---------------------------------------------------
        public SimulationResult TrySetThinkSchedule(AgentHandle agent, in ThinkSchedule schedule);
        /// <summary>The agent thinks in the next Step even if not due (once; does not move its phase).</summary>
        public SimulationResult TryWake(AgentHandle agent);
        /// <summary>Bulk wake by slot index (generation not checked: indices of free slots are ignored).</summary>
        public SimulationResult TryWakeMany(NativeArray<int>.ReadOnly indices);

        // ---- 4. inputs --------------------------------------------------------------------
        /// <summary>
        /// Binds a game-owned array to an input channel the program reads. Row i is agent slot i.
        /// The container keeps a read-only view and reads it only inside Step; the game may rewrite
        /// rows between steps but must keep the array alive and at least Capacity long until it
        /// unbinds, rebinds or disposes the group. Checked once here: T matches the channel's
        /// declared value type (TypeMismatch otherwise).
        /// </summary>
        public SimulationResult TryBindInput<T>(InputChannel<T> channel, NativeArray<T> rows) where T : unmanaged;
        public SimulationResult TryUnbindInput<T>(InputChannel<T> channel) where T : unmanaged;
        /// <summary>Optional: wake agents whose row of this channel changed since their last think (byte compare inside Step).</summary>
        public SimulationResult TrySetWakeOnChange<T>(InputChannel<T> channel, bool enabled) where T : unmanaged;

        // ---- 5. commands ------------------------------------------------------------------
        /// <summary>
        /// Commands emitted in the last Step on this channel, ordered by agent slot index, then by
        /// emission order inside that agent's update. The view is valid until the next Step,
        /// TryRemoveGroup or Dispose; reading it later is a safety error in the Editor.
        /// Empty (not an error) when nothing was emitted or the program has no such channel.
        /// </summary>
        public NativeArray<Command<T>>.ReadOnly GetCommands<T>(CommandChannel<T> channel) where T : unmanaged;

        // ---- 6. external actions ----------------------------------------------------------
        /// <summary>
        /// Started and Cancelled events of the last Step, ordered by agent slot index, then by the
        /// order the agent's update produced them. Same validity window as GetCommands.
        /// </summary>
        public NativeArray<ActionEvent>.ReadOnly GetActionEvents();
        /// <summary>Start payload of a Started event (TypeMismatch if TStart is not the channel's start type).</summary>
        public SimulationResult TryGetStartPayload<TStart>(in ActionEvent started, out TStart payload) where TStart : unmanaged;
        /// <summary>Cancel payload of a Cancelled event (the node's cancel payload, or its fault-cancel payload).</summary>
        public SimulationResult TryGetCancelPayload<TCancel>(in ActionEvent cancelled, out TCancel payload) where TCancel : unmanaged;
        /// <summary>
        /// Reports the outcome of a started action. Queued; applied at the start of the next Step in the
        /// order of these calls, and wakes the agent for that Step. StaleAction if the action already
        /// ended (the game then also has, or will get, a Cancelled event for it).
        /// </summary>
        public SimulationResult TryComplete<TResult>(ActionHandle action, ActionOutcome outcome, in TResult result) where TResult : unmanaged;
        public SimulationResult TryComplete(ActionHandle action, ActionOutcome outcome);

        // ---- results and faults -----------------------------------------------------------
        /// <summary>Agents whose root finished in the last Step (agent, Success/Failure), in slot order.</summary>
        public NativeArray<RootCompletion>.ReadOnly GetRootCompletions();
        /// <summary>Agents that faulted in the last Step, with the native failure code. A faulted agent stops
        /// until TryRestart or TryRemove.</summary>
        public NativeArray<AgentFault>.ReadOnly GetFaults();
    }

    /// <summary>
    /// When an agent thinks: on ticks t with t % Interval == Phase % Interval. Interval 1 = every tick.
    /// Spread() assigns Phase = (add sequence) % Interval, so N agents with one interval spread evenly.
    /// </summary>
    public readonly struct ThinkSchedule
    {
        public ThinkSchedule(uint interval, uint phase);
        public static ThinkSchedule Every(uint interval, uint phase);
        public static ThinkSchedule Spread(uint interval);
        public uint Interval { get; }
        public uint Phase { get; }
        public bool IsSpread { get; }
    }

    /// <summary>
    /// Names one input channel. The id is the node-side snapshot binding id
    /// ([AibtSnapshotBinding("senses", ...)]); T must be the declared value type.
    /// Create once (hashing the id) and reuse.
    /// </summary>
    public readonly struct InputChannel<T> where T : unmanaged
    {
        public static InputChannel<T> Create(string channelId);
        public ulong Id { get; }
    }

    /// <summary>Names one command channel: the node-side command binding id ([AibtCommand("move")]).</summary>
    public readonly struct CommandChannel<T> where T : unmanaged
    {
        public static CommandChannel<T> Create(string channelId);
        public ulong Id { get; }
    }

    /// <summary>One emitted command. Value is the node's payload, copied out of the step.</summary>
    public readonly struct Command<T> where T : unmanaged
    {
        public AgentHandle Agent { get; }
        public T Value { get; }
    }

    /// <summary>One external action. Wraps the ABI OperationId plus its agent; valid until it ends.</summary>
    public readonly struct ActionHandle : IEquatable<ActionHandle>
    {
        public AgentHandle Agent { get; }
        public OperationId Operation { get; }
        /// <summary>The node's async-operation binding id (which kind of action).</summary>
        public ulong Channel { get; }
    }

    public enum ActionEventKind : byte { Started = 0, Cancelled = 1 }

    public enum ActionCancelReason : byte
    {
        /// <summary>A higher-priority branch, observer or reactive re-evaluation aborted the node (BurstNodeAbortReason Explicit/Observer*).</summary>
        Preempted = 0,
        Timeout = 1,
        /// <summary>Agent removed, group removed, restarted or container disposed.</summary>
        AgentStopped = 2,
        /// <summary>The node ended without consuming a completion and without cancelling: the container fault-cancelled it.</summary>
        NodeEnded = 3,
        /// <summary>The agent faulted.</summary>
        Fault = 4,
    }

    public readonly struct ActionEvent
    {
        public ActionEventKind Kind { get; }
        public ActionHandle Action { get; }
        public ActionCancelReason CancelReason { get; }   // Cancelled only
    }

    public enum ActionOutcome : byte { Succeeded = 0, Failed = 1 }

    public readonly struct RootCompletion { public AgentHandle Agent { get; } public NodeStatus Result { get; } }
    public readonly struct AgentFault { public AgentHandle Agent { get; } public NativeRuntimeFailureV1 Failure { get; } }
}
```

### Node side (items 4–6)

No new ABI is needed for the first cut. A node author uses the existing long attributes, or new
short forms in `AIBT.Native` (additive, outside the ABI namespace, the ADR P9-005 pattern):

| Purpose | Long form (ABI v2, exists) | Short form (proposed) | Call in the node (generated, exists) |
|---|---|---|---|
| Read this agent's row of an input | `[AibtConfigField("senses","GeneratedHandle",1u), AibtSnapshotBinding("senses","game.senses",1u)] SnapshotReadHandle<Senses>` | `[AibtInput("senses")]` | `Shard.BurstAccess.TryReadSnapshot(ref ctx, cfg.Senses, out Senses s)` (Enter, Tick, Observer) |
| Emit a command | `[AibtCommandBinding("move","game.move",1u)] CommandHandle<MoveCommand>` | `[AibtCommand("move")]` (exists) | `Shard.BurstAccess.TryEmit(ref ctx, cfg.Move, in cmd)` |
| Start an external action | `[AibtAsyncOperationBinding("attack","game.attack",1u,"game.attack-cancel",1u)] AsyncOperationHandle<AttackStart,AttackCancel>` | proposed `[AibtExternalAction("attack")]` (`AibtAction` already names the node attribute) | `TryStart(ref ctx, h, in start, in faultCancel, out OperationId id)` |
| Receive its result | `[AibtCompletionBinding("attack-done","game.attack-result",1u)] CompletionHandle<AttackResult>` | `[AibtActionResult("attack-done")]` | `TryConsume(ref ctx, h, id, out outcome, out result)`; `StaleCompletion` = still running |
| Cancel on abort | — | — | `Abort`: `TryCancel(ref abortCtx, h, id, in cancel)`; if omitted the container fault-cancels with the start's `faultCancel` payload (`ActionCancelReason.NodeEnded`) |

**Registering game types.** A game struct used as input, command or action payload is declared in
the game's shard assembly with the existing value registration:
`[AibtBurstValue("game.senses", 1u, "<schema id>")]` on the struct and `[AibtValueField]` on its
fields (`BurstNodeContracts.cs:225-232`). The generator already emits a codec for such types
(`BurstNodeGenerator.cs:1389-1390`). AIBT never references the game's types; the container checks
the bound `T` against the binding's declared type id/version, size, alignment and the same-process
type token that snapshot payloads already use (`NativeSnapshotBuilderV1.cs:110`).

**Agent index in the node context** is not needed for items 4–6: the container stages the current
agent's row, and commands carry the agent handle. Reading another agent's row (a target's
position) is not in the requirements; it would need a new context call (ABI line additions) and is
asked as Q3.

### End-to-end example

```csharp
// --- game side, shard assembly (AIBT never sees these types) ---
[AibtBurstValue("game.senses", 1u, "game.senses.v1")]
public struct Senses { [AibtValueField("enemy-visible", "Bool", 1u)] public bool EnemyVisible;
                       [AibtValueField("enemy-pos", "Float3", 1u)] public Float3Value EnemyPos; }
[AibtBurstValue("game.move", 1u, "game.move.v1")]
public struct MoveCommand { [AibtValueField("target", "Float3", 1u)] public Float3Value Target; }
[AibtBurstValue("game.attack", 1u, "game.attack.v1")]
public struct AttackStart { [AibtValueField("target", "Float3", 1u)] public Float3Value Target; }

public partial struct ChaseAndAttackConfig
{
    [AibtInput("senses")] public SnapshotReadHandle<Senses> Senses;
    [AibtCommand("move")] public CommandHandle<MoveCommand> Move;
    [AibtExternalAction("attack")] public AsyncOperationHandle<AttackStart, EmptyValue> Attack;
    [AibtActionResult("attack-done")] public CompletionHandle<EmptyValue> AttackDone;
}
public partial struct ChaseAndAttackMemory { [AibtMemory("op")] public OperationId Op; [AibtMemory("started")] public bool Started; }

[AibtAction("game.chase-and-attack", Cancellation = BurstCancellationMode.Command)]
public partial struct ChaseAndAttackNode
{
    public static NodeStatus Tick(in ChaseAndAttackConfig cfg, ref ChaseAndAttackMemory mem, ref BurstTickContext ctx)
    {
        if (!mem.Started)
        {
            if (GameShard.BurstAccess.TryReadSnapshot(ref ctx, cfg.Senses, out var senses) != BurstContextResult.Success
                || !senses.EnemyVisible) return NodeStatus.Failure;
            var move = new MoveCommand { Target = senses.EnemyPos };
            if (GameShard.BurstAccess.TryEmit(ref ctx, cfg.Move, in move) != BurstContextResult.Success) return NodeStatus.Failure;
            var start = new AttackStart { Target = senses.EnemyPos };
            if (GameShard.BurstAccess.TryStart(ref ctx, cfg.Attack, in start, default(EmptyValue), out mem.Op) != BurstContextResult.Success)
                return NodeStatus.Failure;
            mem.Started = true;
            return NodeStatus.Running;                     // until the game completes it
        }
        var r = GameShard.BurstAccess.TryConsume(ref ctx, cfg.AttackDone, mem.Op, out var outcome, out EmptyValue _);
        if (r == BurstContextResult.StaleCompletion) return NodeStatus.Running;
        return r == BurstContextResult.Success && outcome == BurstCompletionOutcome.Succeeded ? NodeStatus.Success : NodeStatus.Failure;
    }

    public static void Abort(in ChaseAndAttackConfig cfg, ref ChaseAndAttackMemory mem, ref BurstAbortContext ctx, BurstNodeAbortReason reason)
    {
        if (mem.Started) GameShard.BurstAccess.TryCancel(ref ctx, cfg.Attack, mem.Op, default(EmptyValue)); // → ActionEvent Cancelled
    }
}

// --- game side, simulation code ---
var sim = new TreeSimulation(TreeSimulationSettings.Default(tickMicroseconds: 33_333));
sim.TryAddGroup(soldierProgram, new TreeGroupSettings { Capacity = 512, DefaultThinkInterval = 5,
    CommandQuotaPerAgent = 4, ActionCapacityPerAgent = 2 }, out var soldiers);
var senses = new NativeArray<Senses>(512, Allocator.Persistent);
soldiers.TryBindInput(InputChannel<Senses>.Create("senses"), senses);
var move = CommandChannel<MoveCommand>.Create("move");
for (var i = 0; i < 500; i++) soldiers.TryAdd(ThinkSchedule.Spread(5), out handles[i]);

void SimulationTick(uint tick)              // called by the game's 30 Hz host loop
{
    FillSenses(senses);                     // the game writes rows by AgentHandle.Index
    soldiers.TryWakeMany(enemySpottedIndices.AsReadOnly());
    sim.Step(tick);
    foreach (var c in soldiers.GetCommands(move)) movement.SetTarget(c.Agent.Index, c.Value.Target);
    foreach (var e in soldiers.GetActionEvents())
        if (e.Kind == ActionEventKind.Started) combat.BeginAttack(e.Action);
        else combat.Abort(e.Action);        // preempted, timed out, removed or forgotten
    foreach (var done in combat.FinishedThisTick) soldiers.TryComplete(done.Action, done.Hit ? ActionOutcome.Succeeded : ActionOutcome.Failed);
}
```

`EmptyValue`, `AibtInput`, `AibtExternalAction` and `AibtActionResult` are proposals (G2/G3);
everything else in the node exists today.

### Rules the API commits to

- **Ownership.** The game owns `TreeSimulation`, programs and every bound input array. The
  container owns groups, agent slots, and every array it returns. A group never outlives its
  container; a program must outlive every group that uses it (the catalog refuses disposal while
  retained, `ProductionTreeProgram.cs:31`).
- **Lifetimes and validity windows.** Returned `ReadOnly` views (commands, action events, root
  completions, faults) are valid from the end of one `Step` to the start of the next `Step`, or
  until the group or container is removed/disposed. Bound inputs must stay alive while bound.
- **Threading.** One thread at a time, never during `Step`; `Busy` otherwise. No callbacks into
  game code exist, so re-entrancy is impossible by construction.
- **Handle reuse.** Slot index is stable for the agent's life; a removed slot becomes reusable
  after the teardown `Step`, with generation + 1. Generations wrap after 2³² − 1 reuses of one slot;
  that slot is then retired (capacity shrinks by one) rather than reissuing a used generation.
- **Errors.** Hot paths return `SimulationResult`; per-agent runtime failures become `AgentFault`
  entries (existing `NativeRuntimeFailureV1` codes) and stop only that agent. No logging inside
  `Step` (the host logs per failure today, `ProductionTreeHost.cs:1177`); a game can log faults.
  Exceptions only for null arguments and use after `Dispose`, which are programmer errors.
- **Capacity.** Explicit. `Capacity` and quotas are fixed until `TryReserve`, which allocates and
  must not be called during `Step`. Command quota per agent (not per buffer) keeps overflow
  deterministic regardless of how jobs split the work.
- **Ordering.** Groups in creation order; inside a group due agents and all outputs in ascending slot
  index; inside one agent, emission order. Completions apply in the order `TryComplete` was called.
  Teardown of removed agents runs before any think of the same `Step`.
- **Determinism.** Results depend only on: programs, settings (`TickMicroseconds`, `RandomSeed`),
  the sequence of API calls with their arguments, the ticks passed, and input array contents at each
  `Step`. They do not depend on: wall-clock time, measured durations, job worker count, `UseJobs`,
  `MinimumJobBatch`, other containers, or how many containers ran before in the process. Instance
  ids are a container counter in add order (seed for random streams and operation ids). Process-wide
  owner-id counters remain for validation tokens only (they never reach a result, an output or the
  digest); moving them into the container is option D6.

### Provisional slots the API does not block

- **Group-shared blackboard** (squad target, formation anchor). The ABI already has
  `BlackboardScope.Shared` and `NativeSharedContextOwnerV1` exists but is not wired. Slot:
  `TreeGroup.TryCreateSharedContext(out SharedContextHandle)` + `TryAssignSharedContext(agent,
  context)` + game-side `TryWriteShared<T>(context, key, value)` between steps. Nodes read shared keys;
  node writes to Shared stay refused under jobs (ADR P10-002 option 3(a)) unless the owner later
  accepts next-step visibility. Nothing in G1–G5 assumes agents have no shared context.
- **500 agents at ~6 Hz inside a 30 Hz tick.** `ThinkSchedule.Spread(5)` gives 100 thinks per tick;
  G5 measures it. A deadline-aware wake (an agent whose Timeout/Cooldown deadline falls between its
  thinks is woken at the deadline tick) is a possible addition; it needs a machine query for the
  earliest deadline (internal) and is decided after G5's numbers.

## Alternatives considered

1. **Extend `ProductionTreeScheduler` / `ProductionTreeHost` with a public manual step.** Smallest
   change, but it keeps one `MonoBehaviour` and one GameObject per agent (requirement 1), the
   process-wide `TreeInstanceId` (8), per-host managed effect queues and trace channels (9, memory),
   object handles without generations (1), frame-counted cadence without phase (3), stopwatch
   admission in budgeted mode (8), and the per-frame rebuild of the agent table from hosts (cost).
   The scheduler would gain a second driving mode whose interaction with `Update`, budgets,
   pipelining and the world bridge must be specified and tested. Rejected.
2. **Turn `ProductionTreeHost` into a plain class.** Breaks public API (`MonoBehaviour`,
   serialized references, `AddComponent` users, `NativeTreeAgent`, MCP lookup by
   `FindObjectsByType`). Also still one managed object per agent, which is the shape the game does
   not want. Rejected; the internal-core extraction (Decision 2) takes the useful part of it without
   the break.
3. **A separate container reusing the native machine and the agent-frame core.** Chosen. It
   reuses everything whose semantics are specified and tested (machine, generated dispatch,
   `AgentFrameCoreV1`, snapshot and async ABI), keeps the component path untouched, and lets the
   container own a persistent agent table instead of rebuilding it each frame.
4. **A new node-context API with an agent index and raw pointers to game arrays** (first cut:
   `ctx.Input(in handle)` returning `ref readonly T`). Needs new public members on `BurstTickContext`
   (ABI v2 manifest lines) and a generator exception to the "no pointers/`Native*` in node storage"
   rule (`BurstNodeGenerator.cs:496-508`), while the snapshot binding gives the same data through
   existing ABI. Rejected for G2; a zero-copy `ref readonly` read can be added later if a benchmark
   shows the row copy matters.
5. **Per-agent blackboard writes through handles in bulk** (a batched `TryWriteExternalTreeValue`).
   Keeps facts per agent per key and the blackboard write path (versioning, observers) on every
   fact; the game asked explicitly for arrays read by index. Rejected.

## Consequences

- One more public entry point (`AIBT.Simulation`) next to `NativeTreeAgent` and the scheduler. The
  documentation must say which to use: components for scene objects, the container for simulations
  the game steps itself.
- Production gains wiring for snapshot and async bindings in the container path only; the generated
  adapter keeps rejecting them for hosts (no behaviour change there).
- The generator gains staging code for snapshot bindings and command decoding for the container,
  emitted into the existing per-catalog agent-frame entry. Catalogs generated before G2 run in the
  container only if they use no inputs, commands or actions (`CatalogTooOld` otherwise).
- Memory: per-agent copies of immutable program data (machine node records, children, bindings,
  configuration) and ~20 native arrays per generated leaf remain in G1. Sharing the immutable part
  per group is an internal follow-up after G5 numbers.
- The container never logs from `Step`; diagnostics are data.

## What stays untouched

- `NativeTreeAgent`, `ProductionTreeHost` (public API and behaviour), `ProductionTreeScheduler`,
  `ProductionTreeWorld`, the managed `BehaviorTreeAgent` layer (`AIBT.Agents`).
- ABI v2 (`ExpectedPublicAbiV2.txt`), execution semantics v1, compiled format, package version,
  normative specs (see "ABI and contract impact" for later, separately approved additions).

## Migration thoughts for `NativeTreeAgent`

Later, not in G1–G5: a `NativeTreeAgent` could become a thin component that owns one slot in a
scene-level `TreeSimulation` (one group per program, stepped by a single driver component from
`Update` with `tick = frame` and `TickMicroseconds` from `Time.deltaTime` quantised, or kept on the
microsecond clock via a variant of `Step`). Facts would become a one-row input per key or stay as
Tree-scope writes; effect handlers would read the group's command buffers filtered by slot. Open
points before that: time source (frame time is not a fixed tick), restart semantics (identical by
Decision 3), MCP/debugger discovery (needs the G4 query API instead of `FindObjectsByType`), and
whether per-scene ordering matches today's due order. Worth doing only if one code path for both
is wanted; the container does not require it.

## ABI and contract impact

| Change | Where | Additive? | Old catalogs | Needs |
|---|---|---|---|---|
| `AIBT.Simulation` types (container, group, handles, channels, events) | `AIBT.Runtime`, new namespace | yes, new public API | unaffected | owner approval of the public API shape (D1) |
| Internal per-agent core extracted from `ProductionTreeHost` | `Runtime/Integration` | internal | unaffected | owner OK to refactor host internals (D2) |
| Production support for `SnapshotRead`, `AsyncOperation`, `Completion` bindings in the container path; adapter for hosts unchanged | `GeneratedTreeDispatchAdapterV2` or the extracted core, `AgentFrameCoreV1` | internal | unaffected (they do not use them, or run only on hosts) | none beyond D1 |
| Per-node fault-cancel (`NativeCommandAsyncOwnerV1` has only `TryFaultCancelAll`) | `Runtime/Commands/Native` | additive member on a public low-level class, or internal helper | unaffected | owner approval if public (D4) |
| In-place instance reset of a machine slot (restart without new allocations) | `NativeLifecycleMachineV1` (internal) | internal | unaffected | equality test with a freshly created instance |
| Generator: snapshot-row staging and command/action payload copy in the agent-frame entry; container catalog capability flag | `CodeGen~` | additive generated members outside `AIBT.Burst` (like P10-004's `IGeneratedAgentFrameExecutorV1`) | keep working on hosts; in the container `CatalogTooOld` only when they use inputs/commands/actions | regenerate catalogs (D3) |
| Short attributes `AibtInput`, `AibtExternalAction`, `AibtActionResult`; `EmptyValue` | `AIBT.Native` / `AIBT` | additive, outside ABI namespace (ADR P9-005 pattern) | n/a | owner approval of names (D1) |
| ABI v2 manifest (`AIBT.Burst`) | — | **no change** | — | — |
| Compiled program format | — | **no change** (container time and schedule are not compiled data) | — | — |
| Normative specs | `native-runtime-v1`, `update-phases-v1`, `async-and-commands-v1` | an additive section describing the container's step phases, input-snapshot mapping and action events, after G3 | — | owner approval to edit specs (D5), not in this task |
| Execution semantics | — | **no change**: a think is one logical update; wake and think rate change only when updates happen, which scheduling already may (AGENTS.md "Scheduling changes may affect timing") | — | — |

### Owner decisions needed

| ID | Decision | Options | Recommendation |
|---|---|---|---|
| D1 | Public API shape and namespace | (a) `AIBT.Simulation` as sketched; (b) fold into `AIBT` namespace; (c) keep internal until G5 | (a), marked experimental until G5 |
| D2 | Share code with `ProductionTreeHost` | (a) extract an internal per-agent core used by both (host delegates, equivalence tests); (b) copy the logic into the container | (a): one implementation of update begin/end, step limit and teardown |
| D3 | How container capability reaches catalogs | (a) extend the generated agent-frame entry (regenerate catalogs); (b) a second generated entry type | (a); old catalogs keep working on hosts |
| D4 | Actions ended without cancel (author forgot `Abort`) | (a) container fault-cancels with the start's fault-cancel payload and reports `NodeEnded`; (b) leave the operation active (game never learns) | (a), needs a per-node fault-cancel; it is what the fault-cancel payload exists for |
| D5 | Spec additions after G3 | (a) additive sections in `native-runtime-v1`/`async-and-commands-v1`; (b) a new `simulation-container-v1` spec | (b): keeps existing specs untouched |
| D6 | Process-wide owner-id counters | (a) keep (they do not affect results; documented); (b) move every counter into the container | (a) for G1; (b) only if the game insists on the letter of "no static state" (Q4) |
| D7 | Time unit for durations | (a) microseconds via `tick × TickMicroseconds` (no change); (b) tick-native duration fields on new decorator versions | (a) now; (b) only if Q1 says authors must write ticks |
| D8 | Root completion default | (a) `Restart` (game AI loops); (b) `Stop` (matches `ProductionTreeHost`) | (a); `NativeTreeAgent` already defaults to restart (`NativeTreeAgent.cs:38`) |
| D9 | Wake on changed input | (a) game calls `TryWakeMany` only; (b) plus opt-in byte-compare per channel | (b) as opt-in, it is cheap and deterministic |

## Proof

### EditMode test (G1 for scheduling, extended in G2–G4)

`Tests/Integration/Simulation/` (EditMode, no scene, no GameObject, no `Update`):

1. Two programs (a looping patrol tree with a Cooldown and a reactive selector over a snapshot
   condition; a chase tree with an input read, a command and an external action), 250 agents each,
   `ThinkSchedule.Spread(5)`, 300 ticks driven by a `for` loop calling `Step(tick)`.
2. Deterministic inputs from a seeded generator per tick; the test completes actions after a
   per-agent number of ticks and removes/adds 20 agents at fixed ticks (handle reuse with new
   generation; stale handle refused).
3. **Digest**: per tick a hash over (agent, generation, root results, active path, commands with
   payload, action events, faults). Assert equality of digests: run A vs run B in the same process
   (identity and random streams container-local), `UseJobs = false` vs `true` with
   `MinimumJobBatch = 1`, and 0 job workers vs default.
4. **Zero GC**: after 30 warm-up ticks, `GC.GetAllocatedBytesForCurrentThread()` delta over 100
   ticks = 0 (the method used by existing allocation tests).
5. **Cancel on preemption**: the reactive selector's higher-priority input flips while the action
   runs → exactly one `Cancelled` (`Preempted`) for that action, in the same `Step`, and a later
   `TryComplete` returns `StaleAction`. Variant without `Abort` in the node → `Cancelled`
   (`NodeEnded`).
6. Schedules: agent with interval 5 phase 2 thinks exactly on ticks ≡ 2 (mod 5); `TryWake` adds one
   think; `TryComplete` wakes; Step with a non-increasing tick returns `TickNotIncreasing` and
   changes nothing; calls during Step are impossible from the test thread (verified with a job-side
   probe in a debug hook only if needed).

### Benchmark (G1 baseline, G5 final)

On the existing IL2CPP Player harness (`Benchmarks~/Phase10/PlayerPerf/`, `Invoke-Perf.ps1`), a new
scenario family `sim-*` that creates a `TreeSimulation` instead of components: 500 agents, two
trees (`shared-loop16`-like and `io-move1-fact-effect`-like rewritten to read an input and emit a
command), `Spread(5)` at 30 Hz, 3 000 measured ticks. Report `Step` µs median and p99, main-thread
and process CPU, B per tick, with `JobsUtility.JobWorkerCount = 0` and default (N) workers, ABBA
interleaved against the same trees on `NativeTreeAgent` with `UpdateCadence = 5` as the reference.

### Cost estimate (not a measurement)

Due agents per tick: 500 / 5 = 100. From P10-005 (IL2CPP, 6000.6.9f1, 24-thread CPU, JobsAgentFrame,
`Planning~/Evidence/P10-005/tables.md`), N = 100, all due, µs per frame:

| Tree | bracket | driver | job | begin/finish | rest |
|---|---:|---:|---:|---:|---:|
| `io-move1-fact-effect` | 139 | 79 | 42 | 38 | 57 |
| `shared-loop16` | 144 | 91 | 45 | 47 | 53 |
| `shared-reactive16` | 520 | 460 | 201 | 259 | 60 |

- "rest" (scheduler due collection over all registrations, frame entries, component overhead) is
  what the container replaces with a scan of 500 slots and output merging; assume 5–15 µs.
- "begin/finish" (managed per-host work around the job: 0.38–0.47 µs per agent for simple trees)
  stays similar in G1 because the core is shared; assume unchanged.
- Estimate, 23 workers: **≈ 90–150 µs per Step** for the simple trees (job 42–45 + begin/finish
  38–47 + 5–15 + output merge ~10), **≈ 470 µs** for a `shared-reactive16`-like tree.
- 0 workers: P10-005 measured JobsAgentFrame main-thread time at 0 workers vs 23 workers as 1.3×
  (`io-wait1`), 1.8× (`io-move1-fact-effect`), 2.1× (`shared-loop16`) and 5.5×
  (`shared-reactive16`) at N = 1 000 (README section "few cores"). Applied to the totals above:
  **≈ 160–320 µs** simple, **≈ 2.6 ms** reactive16-like.
- Against a 33.3 ms tick: 0.3–1% for simple trees with any worker count.
- Unknown until measured: p99 (job wake-up jitter), cost of action/command delivery, the effect of
  six fewer thinks per agent on cache behaviour. The G1 baseline benchmark replaces this estimate.

## Stage plan

Each stage is one task card (P10-009 … P10-013) with its own commit(s); each needs the previous one.

| Stage | Steps | Proves | Risks |
|---|---|---|---|
| **G1** container, `Step(tick)`, handles, think rate/phase/wake (P10-009) | 1. extract the per-agent core from `ProductionTreeHost`, host delegates; host + JobsAgentFrame equivalence tests green. 2. `TreeSimulation`/`TreeGroup`/`AgentHandle`, pooled slots, `TryReserve`, deferred teardown, restart in place (machine reset + test vs fresh instance), container-local instance ids and random roots. 3. schedule (interval/phase/spread), wake, wake-many, due order, tick time. 4. persistent job table + in-place path over `AgentFrameCoreV1`. 5. EditMode test 1–4 and 6 with programs using only blackboard keys; `sim-*` baseline benchmark. | items 1, 2, 3, 8, 9 for trees without inputs/commands/actions | the host refactor touching tested code; restart-in-place equality; random seeding in production may be unexercised today (see "Not established") |
| **G2** inputs and commands (P10-010) | 1. container wiring for `SnapshotRead` bindings: bind, type check, row staging before the body (in place and in job). 2. generator: staging in the agent-frame entry, capability flag, `AibtInput` short form. 3. command delivery: per-agent quota, per-channel `Command<T>` buffer in slot order, payload copy. 4. opt-in wake on change. 5. tests: inputs read in Observer (reactive) and Tick, commands digest, quota overflow. | items 4, 5, 7 (wake on change) | payload byte format of commands vs raw `T` (establish; generator emits a copy if they differ); regenerating catalogs |
| **G3** external actions (P10-011) | 1. per-agent operation ledger in the container (native, readable from the job). 2. `Started`/`Cancelled` events, `TryComplete` queue applied at step start, wake on completion. 3. per-node fault-cancel (D4). 4. agent removal / restart / fault cancel paths. 5. tests 5 and action digests; spec text draft (D5) for owner review. | item 6, and 7 for actions | async ledger is a managed class today; moving its state native for the job is the bulk of the stage |
| **G4** trace and explain (P10-012) | 1. `TrySetTraced(agent, bool)` with one trace channel per traced agent (pooled, bounded). 2. `TryGetActivePath(agent, NativeList<uint>)` and last result per node (opt-in byte per node and agent). 3. MCP/CLI lookup of containers through a registry the game opts into (no `FindObjectsByType`). | item 10 | MCP discovery without global state: the registry must be opt-in and per container |
| **G5** Unity 7 and the target load (P10-013) | 1. compile and run all AIBT tests on 7000.0.0a8 in a separate project copy (no second Editor on the Modules project). 2. `sim-*` benchmark at 500 agents, 6 Hz, 0 and N workers, median/p99. 3. `package.json` compatibility note only if the owner approves. | items 8, 9 on Unity 7; the 6 Hz target | Unity 7 alpha API drift; a second Editor on this machine must not be started without the owner's OK |

Then the features F1 subtrees, F2 graph comments/groups, F3 live-graph breakpoints, F4 native node
library, F5 object blackboard keys + performance guide, as already planned.

## Questions for the game team

Only questions whose answer changes the design:

- **Q1. Durations.** Is it acceptable that designers author waits/cooldowns in microseconds (or
  seconds in the editor) that take effect at tick granularity with a fixed `TickMicroseconds`? Or
  must the authored unit be ticks? (D7.)
- **Q2. Think-rate latency.** With 6 Hz thinking, a cooldown or timeout that expires between thinks
  is observed at the next think (up to 4 ticks late). Is that acceptable, or do you need deadline
  wakes?
- **Q3. Other agents' rows.** Do nodes need to read rows of *other* agents (a target's position by
  index), or only their own? Other rows need a new context call.
- **Q4. Static state.** Is a process-wide id counter that never influences results acceptable, or
  must every counter be per container? (D6.)
- **Q5. Completion timing.** Is "a completion reported between ticks is seen at the next `Step`"
  enough, or must a completion be visible within the same `Step` it is reported in (that would
  need a callback during `Step`, which conflicts with your item 9)?
- **Q6. Root completion.** Do your trees loop at the root (default `Restart`), or do you need the
  game to decide what happens when a tree finishes?
- **Q7. Group-shared data.** Will shared squad data be written only by the game (between steps), or
  also by nodes? Node writes are visible to other agents only from the next step under jobs.

## Not established from the code

- Whether random-capable generated nodes work on the production path today: the adapter allocates
  their stream arrays zeroed (`GeneratedTreeDispatchAdapterV2.cs:701-702`), the spec requires a
  seeded odd increment (`time-and-random-v1.md`), and dispatch validation checks the increment's low
  bit (`NativeBurstDispatchBatchOwnerV2.cs:590`). G1 must seed streams per spec from container-local
  identity and add a test; whether hosts have the same gap is a separate question for the owner.
- The exact byte format of command payloads written by generated `TryEmit` for a registered value
  type (codec writes leaf by leaf, `BurstNodeGenerator.cs:1384-1399`): raw `T` layout or canonical.
  G2 establishes it; the API (`Command<T>.Value`) does not depend on the answer.
- The production snapshot read path end to end (it is covered at the dispatch level by tests, never
  wired in production). G2 establishes it.
- Unity 7000.x behaviour of Burst, Jobs and Collections (G5).
