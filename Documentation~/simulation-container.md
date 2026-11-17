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
| `TreeSimulation` | One world. `TreeSimulationSettings`: `TickMicroseconds` (time of one tick), `RandomSeed`, `UseJobs`, `MinimumJobBatch` (default 64), `MaximumStepsPerThink`. |
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
- **Not under a reactive composite.** A reactive sequence or selector aborts its running child at
  every update before it re-evaluates (`execution-semantics-v1.md`, "Reactive selector").
  - An action node anywhere below one is cancelled (`Preempted`) and started again at every think.
  - It never reads a completion: the completion's own wake-up think aborts it first.
  - Put long actions under memory composites. Use a reactive composite only for a node whose action
    is fire-and-forget, or whose restart at every think is intended.
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
## Lifetime rules

- Capacity is allocated by `TryAddGroup` and `TryReserve`, never during `Step`. Growing a group
  keeps its handles and running agents.
- A removed agent's slot becomes reusable after the step that tears it down.
- `TryRemoveGroup`: the next step tears down the group's agents, the step after that releases its
  storage. Then the program can be disposed.
- `Dispose` releases storage without running aborts. Nothing runs outside `Step`.
- Call the container from one thread at a time. Calls made while `Step` runs (from a callback)
  return `Busy`.

## Not yet (G4 and later)

- Trace channels and the live debugger.
- Shared- and World-scope blackboard keys.
