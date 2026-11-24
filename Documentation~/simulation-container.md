# Simulation container (experimental)

`AIBT.Simulation.TreeSimulation` runs behaviour trees without GameObjects, components or Unity
`Update`. The game owns it, adds agents to it, and advances it with `Step(tick)` from its own loop:
a fixed-step game loop, a server tick, an EditMode test or a tool.

> **Experimental.** Stages G1–G3 of `Documentation~/decisions/ADR-P10-008-simulation-container.md`.
> Names and members may change until stage G5. Generated nodes may use Tree/Agent-scope blackboard
> keys, game inputs (`SnapshotReadHandle`), commands (`CommandHandle`) and (G3) external
> long-running actions (`AsyncOperationHandle`, `CompletionHandle`).

## Model

| Type | What it is |
|---|---|
| `TreeSimulation` | One world. `TreeSimulationSettings`: `TickMicroseconds` (time of one tick), `RandomSeed`, `UseJobs`, `MinimumJobBatch` (default 64), `MaximumStepsPerThink` (default 10 000; an agent that needs more steps in one think faults with `NativeUpdateStepLimitExceeded`, so a runaway tree cannot hold `Step`). |
| `TreeGroup` | Agents that run one `ProductionTreeProgram`. `TreeGroupSettings`: `Capacity` (slots allocated up front), `DefaultThinkInterval`, `RootCompletion` (`Restart`, the default, or `Stop`), `CommandQuotaPerAgent` (G2; 0 = default 4), `ActionCapacityPerAgent` (G3; 0 = default 2). |
| `InputChannel<T>` (G2) | A game input: the id nodes give in `[AibtInput("id")]` and the row type `T`. A game `NativeArray<T>` bound to a group gives row `i` to the agent in slot `i`. |
| `CommandChannel<T>`, `Command<T>` (G2) | A command: the id nodes give in `[AibtCommand("id")]` and the payload type `T`; `Command<T>` is one emitted command (agent handle, value). |
| `ActionHandle`, `ActionEvent` (G3) | An external action a node started (agent, operation id, channel) and one Started or Cancelled event of the last step. |
| `AgentHandle` | Slot index + generation + group id. A handle stays valid until its agent is removed. After that, every call with it returns `StaleHandle`, also when the slot is reused. |
| `ThinkSchedule` | `Every(interval, phase)`: thinks on ticks where `tick % interval == phase`. `Spread(interval)`: the group assigns phases 0, 1, 2, … round-robin. |
| `SimulationResult` | Every mutating call returns one: `Success`, `Busy` (called during `Step`), `Disposed`, `StaleHandle`, `CapacityExceeded`, `TickNotIncreasing`, `InvalidArgument`; from G2 also `InputMissing`, `TypeMismatch`, `CatalogTooOld`, `Unsupported` (no longer returned since G3), `UnknownChannel`; from G3 `StaleAction`, `AlreadyCompleted`. |

## One step

`Step(tick)` works in this order:

1. It refuses a tick that is not greater than the last one (`TickNotIncreasing`), and (G2) a group
   with an input channel that is unbound or whose array was disposed (`InputMissing`) or is shorter
   than the group's capacity (`InvalidArgument`). A refused step changes nothing.
2. (G2) Every group forgets last step's commands and copies the first `HighWaterIndex` rows of each
   bound input array into its own buffer. This is the only copy a step makes: every agent of the
   step (in place or in a job) reads the same values, jobs never touch the game's arrays, and the
   game may write its arrays again as soon as `Step` returns.
3. In every group, in creation order, agents removed or restarted since the last step are torn
   down (abort with `TreeStopped`). Groups removed one step earlier are released.
4. Due agents think. An agent is due when its schedule matches the tick, it was woken by
   `TryWake`/`TryWakeMany` or (G3) by a completion the game gave, or (G2, opt-in) its row of a wake-on-change input differs from the row
   it read at its last think. Groups go in creation order, agents in ascending slot order. Each think
   is one update at time `tick × TickMicroseconds`. With `UseJobs` on and at least
   `MinimumJobBatch` due agents in a group, the group's think runs as one agent-frame job; the step
   completes it before it returns. Otherwise it runs on the calling thread. Results are identical
   either way.
5. Root completions, faults, (G2) commands and (G3) action events are recorded in slot order;
   action events of removed groups' teardowns are included. They can be read with
   `GetRootCompletions()` and `GetFaults()` until the next step. A completed agent restarts in
   place at its next think (`Restart`), or keeps its result and stops thinking (`Stop`, until
   `TryRestart`).

After warm-up, `Step` allocates no managed memory, logs nothing and throws nothing. A failing agent
is reported as an `AgentFault` and stops. The step itself still succeeds.

## Determinism

Results depend only on the programs, the settings, the calls made (and their order) and the ticks
passed. They do not depend on wall-clock time, job worker count, `UseJobs`, `MinimumJobBatch`, other
containers, or what ran earlier in the process.

- Tree instance ids are container-local: 1, 2, … in the order agents are added or restarted.
- Random-capable nodes get streams derived from `RandomSeed`, the program's semantic hash, the
  instance id and the node index, as `Documentation~/specifications/time-and-random-v1.md` requires.

## Game loop (G1)

```csharp
using AIBT;
using AIBT.Simulation;

// Once: the program comes from a baked ProductionTreeAsset and the game's generated catalog.
asset.TryCreateProgram(MyCatalog.TryCreateRuntimeCatalog, out var program, out var failure);

var settings = TreeSimulationSettings.Default(tickMicroseconds: 33_333); // 30 Hz
settings.RandomSeed = matchSeed;
var simulation = new TreeSimulation(settings);
simulation.TryAddGroup(program, TreeGroupSettings.Default(capacity: 512), out var guards);

var handles = new AgentHandle[500];
for (var i = 0; i < handles.Length; i++)
    guards.TryAdd(ThinkSchedule.Spread(5), out handles[i]); // each agent thinks every 5th tick

// Every fixed tick of the game:
void FixedTick(uint tick)
{
    simulation.Step(tick);
    foreach (var completion in guards.GetRootCompletions()) OnGuardFinished(completion.Agent, completion.Result);
    foreach (var fault in guards.GetFaults()) Debug.LogError(fault.Agent + ": " + fault.Failure);
}

// Spawning and despawning between ticks:
guards.TryRemove(handles[3]);      // torn down in the next Step; the slot is free after that
guards.TryAdd(out var newcomer);   // lowest free slot (a removed slot is free after its teardown step)
guards.TryWake(newcomer);          // think in the next Step, outside the schedule

// Shutdown: remove groups and step once if aborts must run, then dispose.
simulation.Dispose();
program.Dispose();
```

## Inputs and commands (G2)

A node reads its agent's row of a game input through a `SnapshotReadHandle<T>` field marked
`[AibtInput("id")]` and `TryReadSnapshot`, and emits a command through a `CommandHandle<T>` field
marked `[AibtCommand("id")]` and `TryEmit`. `T` is a built-in value type (`bool`, integers, `float`,
`double`, `Float2Value`, `Float3Value`, `QuaternionValue`) or a struct marked `[AibtBurstValue]`.

```csharp
static readonly InputChannel<SenseInput> Sense = InputChannel<SenseInput>.Create("sense");
static readonly CommandChannel<OrderCommand> Orders = CommandChannel<OrderCommand>.Create("order");

var sense = new NativeArray<SenseInput>(guards.Capacity, Allocator.Persistent); // row i = slot i
guards.TryBindInput(Sense, sense);          // once; rebind after TryReserve grows the group
guards.TrySetWakeOnChange(Sense, true);     // optional: think as soon as the row changes

void FixedTick(uint tick)
{
    for (var i = 0; i < handles.Length; i++) sense[handles[i].Index] = SenseOf(handles[i]); // the game's own data
    simulation.Step(tick);
    foreach (var order in guards.GetCommands(Orders)) Apply(order.Agent, order.Value);
}
```

- **Inputs.** `TryBindInput` checks that `T` is the type the program's nodes declare for the channel
  (type id, version, every field), else `TypeMismatch`; an id no node reads gives `UnknownChannel`.
  The group does not own the array: keep it alive and at least `Capacity` rows long while bound.
  The first bind of a type uses reflection; later binds and steps do not allocate.
- **What a node sees.** The row is translated to the canonical value (fields ordered by field id,
  natural alignment, little-endian; -0.0 becomes +0.0). A row that is not canonical (NaN, an
  infinity, a bool above 1) faults the agent that reads it (`AgentFault.Cause` = `InvalidEncoding`);
  the step and other agents go on.
- **Commands.** `GetCommands(channel)` (or `TryGetCommands`) returns the last step's commands of that
  channel: agents in slot order, each agent's commands in emission order. It is valid until the next
  step. Each agent may emit `CommandQuotaPerAgent` commands per channel per think (a teardown has its
  own quota). A node that would go over the quota does not run: the agent faults with
  `AgentFault.Cause` = `CapacityExceeded`; commands emitted earlier in that think are kept. Nothing
  throws. Nodes emit at most one command per command field per dispatch.
- **Wake on change** (off by default): byte compare of the canonical row against the row the agent
  read at its last think, per channel. Agents that have not thought since it was turned on are not
  woken. Deterministic, allocation-free.
- **Catalogs generated before G2** have no binding ids: a program with inputs is refused
  (`CatalogTooOld`); commands of other programs are discarded as in G1 and `TryGetCommands` returns
  `CatalogTooOld`. Regenerate the catalog (it happens on the next compile).
- **Hosts** (`ProductionTreeHost`, `ProductionTreeScheduler`) are unchanged: they still refuse
  programs with input (snapshot) bindings.

## External actions (G3)

A node starts a long-running action the game carries out (a move, an animation, a path request)
through an `AsyncOperationHandle<TStart, TCancel>` field and `TryStart`, and reads how it ended
through a `CompletionHandle<TResult>` field and `TryConsume`. The game sees each start and each
cancellation as an event after `Step`, and completes actions between steps.

```csharp
// The node (generated catalog, Burst). The binding ids name the action and its completion.
public partial struct MoveConfig
{
    [AibtConfigField("move", "GeneratedHandle", 1u)]
    [AibtAsyncOperationBinding("move", "game.move-order", 1u, "game.move-stop", 1u)]
    public AsyncOperationHandle<MoveOrder, MoveStop> Move;

    [AibtConfigField("arrived", "GeneratedHandle", 1u)]
    [AibtCompletionBinding("arrived", "game.move-result", 1u)]
    public CompletionHandle<MoveResult> Arrived;
}

// Tick: start once, then poll. Abort: cancel (BurstCancellationMode.Command).
if (!memory.Started)
{
    MyShard.BurstAccess.TryStart(ref context, config.Move, in order, in new MoveStop { Reason = -1 }, out memory.Operation);
    memory.Started = true;
    return NodeStatus.Running;
}
var consumed = MyShard.BurstAccess.TryConsume(ref context, config.Arrived, memory.Operation, out var outcome, out var result);
if (consumed == BurstContextResult.StaleCompletion) return NodeStatus.Running; // not completed yet
return outcome == BurstCompletionOutcome.Succeeded ? NodeStatus.Success : NodeStatus.Failure;
```

```csharp
// The game, every fixed tick.
void FixedTick(uint tick)
{
    simulation.Step(tick);
    foreach (var e in guards.GetActionEvents())
    {
        if (e.Kind == ActionEventKind.Started)
        {
            guards.TryGetStartPayload(e, out MoveOrder order);
            movement.Begin(e.Action, e.Action.Agent, order);      // keep the ActionHandle
        }
        else
        {
            guards.TryGetCancelPayload(e, out MoveStop stop);     // e.CancelReason says why
            movement.Stop(e.Action);                              // never complete it now
        }
    }
}

// Whenever the game's move ends, between steps:
void OnArrived(ActionHandle action, bool reached, float distance)
    => guards.TryComplete(action, reached ? ActionOutcome.Succeeded : ActionOutcome.Failed,
                          new MoveResult { Distance = distance });
```

- **Events.** `GetActionEvents()` lists the last step's events: agents in slot order, each agent's
  events in the order they happened (a torn-down instance's events before its new instance's). It is
  valid until the next step and allocates nothing. Read the values with `TryGetStartPayload<T>`
  (Started) and `TryGetCancelPayload<T>` (Cancelled) before the next step.
- **Exactly one end.** Every started action ends exactly once: either its node consumes the game's
  completion (no event), or it gets one `Cancelled` event. Never two, never a `Cancelled` after the
  completion was consumed. After either, calls with its handle return `StaleAction`.
- **Cancel reasons.** `Preempted` (the node was aborted, for example by a reactive interrupt, and
  cancelled in Abort), `Timeout` (aborted by a timeout), `NodeEnded` (the node ended with the action
  live and did not cancel it), `Fault` (the agent faulted), `AgentRemoved`, `AgentRestarted`,
  `GroupRemoved` (teardown). `ActionEvent.FaultCancel` is true when the cancel value is the
  fault-cancel value the node gave at start (every reason except a node's own cancel in Abort).
- **Completing.** `TryComplete(action, outcome)` or `TryComplete<TResult>(action, outcome, value)`
  between steps. It records the completion at once and wakes the agent, so its node reads it in the
  next step, whatever the schedule. If the node is aborted in that step before it reads it, preemption
  wins: the action gets a `Cancelled` event and the completion is dropped. Without a value the node
  reads zeros. Result codes, nothing changed: `AlreadyCompleted` (second call), `StaleAction` (the
  action ended, or its agent was removed or restarted), `StaleHandle` (another group's handle),
  `TypeMismatch` (not the node's completion type), `InvalidArgument` (an outcome outside
  `ActionOutcome`, NaN, an infinity, a bool above 1), `Busy`, `Disposed`.
- **Guard actions with the retaining reactive composites.** To re-check a condition at every think
  above a long action, use `aibt.core.reactive-sequence-retaining` or
  `aibt.core.reactive-selector-retaining` (ADR-P10-013). They re-check the children before the running
  one at every think and keep the running child: the action is started once, polled at every think and
  reads its completion. A guard that ends the composite or starts running (a higher-priority branch in
  the selector) aborts the kept branch, so its action ends with one `Cancelled` (`Preempted`); when the
  game completes the action in the same step the guard fires, the guard runs first and preemption wins.
  - Every child before the running one is such a guard. Put multi-step work in one child, for example
    `reactive-sequence-retaining [cond, memory-sequence [stepA, stepB]]`: as two siblings
    `[cond, stepA, stepB]`, once `stepA` succeeds and `stepB` runs, `stepA` becomes a guard: at the
    next think it is entered again from scratch (its action starts again); if it returns `Running` it
    takes over, `stepB` is aborted (its action ends with `Cancelled`, `Preempted`) and never finishes
    while `stepA` keeps running.
  - The v1 `reactive-sequence` and `reactive-selector` abort their running child at every update
    before they re-evaluate (`execution-semantics-v1.md`, "Reactive selector"). An action node anywhere
    below one is cancelled (`Preempted`) and started again at every think and never reads a
    completion (the completion's own wake-up think aborts it first). Use them only for a node whose
    action is fire-and-forget, or whose restart at every think is intended; otherwise use a retaining
    composite or a memory composite.
- **Polling.** A poll before the game completed the action (`TryConsume` returns `StaleCompletion`)
  keeps the node `Running` and changes nothing else in that dispatch: anything the node did before
  the poll in the same Tick is dropped. Decide time-outs and other early exits before polling.
- **Capacity.** Each agent may hold `ActionCapacityPerAgent` live actions (default 2) and start at
  most that many per think. A start over the limit faults the agent (`AgentFault.Cause` =
  `CapacityExceeded`), and its live actions end with `Cancelled`, reason `Fault`. Events per agent per
  step are at most 3 × the capacity, so `ActionEventCapacity` (slots × 3 × capacity) is never
  exceeded. A program without actions has none.
- **Paths.** In place and in agent-frame jobs, with any worker count, events, payloads and agent
  states are identical.
- **Catalogs generated before G2** cannot name action channels: a program with actions is refused
  with `CatalogTooOld`.
- **Hosts** (`ProductionTreeHost`, `ProductionTreeScheduler`) are unchanged: they still refuse
  programs with external actions at bootstrap.

## Durations in ticks

Every time node and decorator turns its duration into a deadline, `deadline = now + d`, and ends at
the first think where `now >= deadline`. In the container `now` is `tick × TickMicroseconds`. Two
recipes give exact durations in ticks with the library as it is:

- **A multiple of `TickMicroseconds`.** `d = n × TickMicroseconds` ends exactly n ticks after the
  think that started it, not n − 1 and not n + 1. At 30 Hz (`TickMicroseconds = 33 333`), 30 ticks is
  `999 990`.
- **`TickMicroseconds = 1`.** The clock is then the tick index, and every "microseconds" parameter
  is a tick count: `"duration-microseconds": 30` is 30 ticks. A value of 0 is special (see "A
  duration of 0" below).

Both hold for `aibt.native.wait-microseconds`, the timeout of `aibt.native.wait-until-true`,
`aibt.native.set-deadline` with `aibt.native.deadline-passed`, `aibt.native.wait-random-microseconds`
(a range `[a × T, b × T]` ends between a and b ticks), `aibt.core.timeout`, and `aibt.core.cooldown`
(both start policies), in place and in jobs alike
(`SimulationTickDurationTests.ExactMultipleOfTickMicroseconds_EndsAfterExactlyNTicks_EveryThink`,
`WaitRandomRange_EndsInsideTheRangeInTicks`).

- **A duration that is not a multiple rounds up** to `ceil(d / TickMicroseconds)` ticks. At 30 Hz,
  `1 000 000` ("one second") is 31 ticks; `7 × 33 333 − 1` is still 7 ticks, `7 × 33 333 + 1` is 8
  (`NonMultiple_RoundsUpToTheNextTick`).
- **A duration ends at a think.** The check runs only when the agent thinks, so the duration ends at
  the agent's first think at or after its deadline: never earlier, and with `Spread(5)` up to 4 ticks
  later (1 tick ends after 5, 7 after 10, 10 after 10). Units do not change this; only a wake makes
  the agent think in between (`UnderSpread5_EndsAtTheFirstThinkAtOrAfterTheDeadline`).
- **Limits of the native nodes.** They take `UInt32` microseconds: at 30 Hz the longest exact duration
  is 128 850 ticks (`4 294 957 050`). With `TickMicroseconds = 1` it is 4 294 967 295 ticks only for a
  timer started at tick 0, because the tick counter itself ends at `uint.MaxValue`: a timer started
  at tick s can run at most 4 294 967 295 − s ticks. A larger value does not compile
  (`LargestUInt32Duration_IsExactInBothRecipes`, `NativeDurationAboveUInt32_DoesNotCompile`).
- **Limits of `aibt.core.timeout` and `aibt.core.cooldown`.** They are authored as `UInt64`, but only
  1 to `long.MaxValue` microseconds is a valid duration.
  - Durations up to the end of the tick range are exact in both recipes (tested at 4 294 967 294
    ticks, both cooldown start policies) (`LargeUInt64Duration_IsExact`).
  - A deadline past the largest `Int64` microsecond count (`now + d > long.MaxValue`) faults the
    agent with `NativeCapacityArithmeticOverflow` at the think that starts the deadline (for a
    cooldown with `on-successful-exit`, the think in which its child succeeds), in place and in jobs
    alike (`DeadlineBeyondInt64_FaultsTheAgentAtItsFirstThink`).
  - 0 is not a valid duration for these two: the tree does not compile (`AIBT2028`, a parameter
    constraint), so with `TickMicroseconds = 1` the shortest timeout or cooldown is 1 tick.
  - A value above `long.MaxValue` does not reach the container either: reading the tree JSON throws
    `InvalidCastException` (`TimeoutAndCooldown_ZeroOrAboveInt64_AreRefused`).
- **A duration of 0** in the native nodes, with one agent thinking every tick, in place and in jobs
  (`ZeroDuration_InTheNativeTimeNodes`):
  - `aibt.native.wait-microseconds` with 0 succeeds on the think that enters it.
  - `aibt.native.wait-until-true` with `timeout-microseconds` 0 has no timeout: it waits for its key.
  - `aibt.native.set-deadline` with 0 writes the current time, so the deadline has passed at once: a
    branch that re-arms it whenever `aibt.native.deadline-passed` holds fires at every think.
  - `aibt.native.wait-random-microseconds` with `min = max = 0` succeeds on the think that enters it.
- **`aibt.stdlib.wait` counts thinks, not ticks.** It counts its own Tick calls, and the think that
  enters it is the first: `"ticks": 6` ends 5 ticks after it started for an agent that thinks every
  tick, and 25 ticks after (its 6th think) under `Spread(5)`. Every extra think counts too: a wake
  shortens it, while a microsecond wait still ends at its deadline. Do not use it for durations in
  the container (`StdlibWait_CountsThinks_NotTicks`,
  `StdlibWait_IsShortenedByAnExtraWake_AMicrosecondWaitIsNot`).

## Reactive interrupts

To interrupt a long-running branch as soon as the game raises a trigger (`TriggerRow` and
`PositionRow` are the game's own `[AibtBurstValue]` row types):

```csharp
static readonly InputChannel<TriggerRow> Trigger = InputChannel<TriggerRow>.Create("trigger");     // the flag only
static readonly InputChannel<PositionRow> Position = InputChannel<PositionRow>.Create("position"); // rewritten every tick

// group: the TreeGroup returned by TryAddGroup (the examples above call it guards)
group.TryBindInput(Trigger, triggers);
group.TryBindInput(Position, positions);
group.TrySetWakeOnChange(Trigger, true);    // wake on the trigger channel only
```

```text
aibt.core.reactive-selector-retaining
├─ alert   (memory-sequence: [triggered] → flee → ...)   the guard: a Burst condition reading Trigger
└─ walk    (the long-running action; reads Position)
```

- **Latency.** A trigger row written before `Step(t)` preempts inside `Step(t)`: the agent is woken,
  the guard pass runs the guard first, the kept branch is aborted (its external action ends with one
  `Cancelled`, `Preempted`) and the guard's branch runs in that same step. Only that agent thinks off
  its schedule. Without wake the guard runs at the agent's next scheduled think
  (`TreeSimulationReactivePatternTests.TriggerWrittenBeforeStep_PreemptsTheWalkInsideThatStep`).
- **Wake is per channel.** The whole canonical row of a wake channel is compared, so a field that
  changes every tick wakes the agent every tick: with wake on for a positions channel rewritten every
  tick, every agent that has thought once thinks at every tick with the reason `InputChanged`
  (`PositionsWithWakeOn_RewrittenEveryTick_WakeEveryAgentEveryTick`). Keep the trigger in its own
  channel and leave wake off on the positions channel: positions rewritten every tick then wake no
  one, and every agent thinks only on its schedule
  (`PositionsRewrittenEveryTick_WakeNoOne_ThinksFollowTheSchedule`).
- **`aibt.core.reactive-sequence-retaining`** works the same way with a guard that must hold (for
  example `inverter [triggered]` before the walk): when it fails the sequence ends and the walk is
  preempted in the same step (`RetainingSequence_TriggerWrittenBeforeStep_PreemptsInsideThatStep`).
- **`TryWakeMany`** is the alternative when the game already knows whom to wake: write the trigger,
  wake those slots, and the walk is preempted inside the next step with no row comparison and wake off
  on every channel (`TryWakeMany_WithoutWakeOnChange_PreemptsInsideThatStep`).
- **Conditions.**
  - The agent must have thought once since wake was turned on and since it was added; until then a
    changed row does not wake it
    (`NotWoken_BeforeTheAgentThoughtSinceItWasAdded_OrSinceWakeWasEnabled`).
  - The guard must be a child before the running one. A trigger read by a child after it still wakes
    the agent, but nothing is preempted (`GuardAfterTheRunningChild_DoesNotPreempt`).
  - Rows are compared once per step with the row of the agent's last think, so a value that rises and
    falls between two steps is never seen; without wake, one that falls again before the agent's next
    think is not seen either (`RiseAndFallBetweenTwoSteps_IsNotSeen`).
- **Cost.** Measured in the Editor (Burst safety checks and the jobs debugger on; a Player was not
  measured) with this pattern, 500 agents, `Spread(5)`, one step per tick, the agent-frame job path
  (`TreeSimulationWakeCostBenchmark`, two runs of seven interleaved rounds):
  - wake on the trigger channel, never changing, adds about 150 µs per step to a step of about 330 µs:
    the rows of the 400 agents that are not due are compared on the calling thread before the job,
    about 0.4 µs each;
  - rewriting the positions channel every tick without wake adds nothing measurable;
  - wake on the positions channel makes every agent think at every step: about 2.8 ms per step instead
    of 0.48 ms.

## Debugging (G4)

Three tools answer "why is agent N doing X": a trace of a few selected agents, a query of any
agent's active path between steps, and an opt-in registry through which the AIBT MCP/CLI tools find
the game's containers. None of them changes what agents do: digests are identical with and without
tracing, in place and in jobs, with any worker count.

```csharp
// Select agents to trace (between steps). Allocates the trace's memory once, never in Step.
guards.TryConfigureTracing(new AgentTraceSettings { MaximumTracedAgents = 4, RecordsPerAgent = 2048, RecordsPerThink = 256 });
guards.TrySetTraced(suspect, true);

// After some steps: the trace, oldest first. No allocation when the list is large enough.
using var records = new NativeList<AgentTraceRecord>(2048, Allocator.Persistent);
guards.TryCopyTrace(suspect, records, out var info);   // info.Count, info.Recorded, info.Dropped

// Any agent, traced or not: its active path and state.
using var path = new NativeList<ActivePathNode>(16, Allocator.Persistent);
guards.TryGetActivePath(suspect, path, out var state);  // path[0] is the root, the last the running leaf
Debug.Log(guards.DescribeAgent(suspect));                // readable text; allocates, for logs and tools

// Let the MCP/CLI tools see this container (Editor and development builds only).
TreeSimulationDebug.Register(simulation, "guards");
```

- **What a trace holds.** Per think and per teardown of the traced agent, in order:
  - a `Think` record with the reasons (`ThinkReasons`: `Scheduled`, `WokenByGame`, `InputChanged`,
    `ActionCompleted`, `FreshInstance`), or a `Teardown` record with the reason (`AgentRemoved`,
    `AgentRestarted`, `GroupRemoved`);
  - one `Input` record per bound input channel: the agent's row as the think reads it (canonical
    bytes, up to 16 kept in the record, `ValueHex()`);
  - the `Lifecycle` records of the package's native trace recorder, the same ones a
    `ProductionTreeHost` records (update started and completed with the root result; node entered,
    ticked, exited and aborted, with status and reasons);
  - the agent's `Command` records, `ActionStarted` and `ActionCancelled` records with their values,
    `CompletionConsumed` for a completion its node read, and a `Fault` record with the diagnostic.
  
  Every record carries the step's tick and a native `NativeTraceRecordV1` (container facts use the
  nearest existing event kind), numbered from 1 per agent without gaps.
- **Bounded.** `AgentTraceSettings`: `MaximumTracedAgents` (default 8 per group), `RecordsPerAgent`
  (default 1 024, a ring: the oldest records are dropped and counted) and `RecordsPerThink` (default
  256 lifecycle records per think or teardown; more are dropped and counted). Past four times
  `RecordsPerThink` in one think the count stops and `AgentTraceInfo.DroppedIsLowerBound` is set.
  Configure while no agent is traced.
- **Cost.** An untraced agent pays one test per think. On the agent-frame job a traced agent's
  steps are logged inside the job and replayed into its trace on the main thread, so traces are the
  same on every path. `Step` allocates nothing managed while tracing.
- **Removed agents.** A removed agent's trace, ending with its teardown, stays readable by its stale
  handle until it is untraced or its slot is reused.
- **Active path and state.** `TryGetActivePath` (into a `NativeList` or a `Span`) gives each node's
  index, stable id (FNV-1a 64 of the authoring id), type id and status, plus an `AgentDebugState`:
  `NotStarted`, `Running`, `Finished` (with the root result) or `Faulted` (with the fault and its
  cause), when and why it last thought, when it thinks next, pending wake or restart, live action
  count and whether it is traced. `TryCopyLiveActions` lists its live actions and whether the game
  completed them. A finished or faulted agent has an empty path or the path at the fault.
- **DescribeAgent** puts all of it in a few lines, with node names from the program's debug map and,
  for a traced agent, each node's last result seen in its trace. Pass a `Func<ulong, string>` to name
  custom node types.
- **Between steps only.** During `Step` every debug call returns `Busy` (`DescribeAgent` says so).
- **Registry.** `TreeSimulationDebug.Register(simulation, name)` lists a container for tools; it
  holds it weakly, a disposed container leaves the list at once, and nothing is listed unless the
  game registers it. Outside the Editor and development builds `Register` returns false and keeps
  nothing (`IsAvailable`).
- **Tools.** MCP `runtime_simulations` lists registered containers, their groups and traced agents;
  `describe_agent` returns an agent's state, active path, live actions and text; `read_agent_trace`
  pages through a traced agent's trace (`afterSequence`, `limit`). All three are read-only. The CLI
  has `runtime simulations` and `agent describe|trace <simulation> <group> <slot>`.

## Lifetime rules

- Capacity is allocated by `TryAddGroup` and `TryReserve`, never during `Step`. Growing a group
  keeps its handles and running agents.
- A removed agent's slot becomes reusable after the step that tears it down.
- `TryRemoveGroup`: the next step tears down the group's agents, the step after that releases its
  storage. Then the program can be disposed.
- `Dispose` releases storage without running aborts. Nothing runs outside `Step`.
- Call the container from one thread at a time. Calls made while `Step` runs (from a callback)
  return `Busy`.

## Not yet (G5 and later)

- The AIBT Graph window's live highlighting and trace timeline for a container agent (G4 gives the
  data and the MCP/CLI tools; the window still binds to hosts only).
- Shared- and World-scope blackboard keys.
