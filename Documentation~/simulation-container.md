# Simulation container (experimental)

`AIBT.Simulation.TreeSimulation` runs behaviour trees without GameObjects, components or Unity
`Update`. The game owns it, adds agents to it, and advances it with `Step(tick)` from its own loop:
a fixed-step game loop, a server tick, an EditMode test or a tool.

> **Experimental.** Stage G1 of `Documentation~/decisions/ADR-P10-008-simulation-container.md`.
> Names and members may change until stage G5. G1 runs trees whose generated nodes use blackboard
> keys only. Game inputs, commands and external actions (G2, G3) are not there yet, and effects a
> node emits are discarded.

## Model

| Type | What it is |
|---|---|
| `TreeSimulation` | One world. `TreeSimulationSettings`: `TickMicroseconds` (time of one tick), `RandomSeed`, `UseJobs`, `MinimumJobBatch` (default 64), `MaximumStepsPerThink`. |
| `TreeGroup` | Agents that run one `ProductionTreeProgram`. `TreeGroupSettings`: `Capacity` (slots allocated up front), `DefaultThinkInterval`, `RootCompletion` (`Restart`, the default, or `Stop`). |
| `AgentHandle` | Slot index + generation + group id. A handle stays valid until its agent is removed. After that, every call with it returns `StaleHandle`, also when the slot is reused. |
| `ThinkSchedule` | `Every(interval, phase)`: thinks on ticks where `tick % interval == phase`. `Spread(interval)`: the group assigns phases 0, 1, 2, … round-robin. |
| `SimulationResult` | Every mutating call returns one: `Success`, `Busy` (called during `Step`), `Disposed`, `StaleHandle`, `CapacityExceeded`, `TickNotIncreasing`, `InvalidArgument`. |

## One step

`Step(tick)` works in this order:

1. It refuses a tick that is not greater than the last one (`TickNotIncreasing`). A refused step
   changes nothing.
2. In every group, in creation order, agents removed or restarted since the last step are torn
   down (abort with `TreeStopped`). Groups removed one step earlier are released.
3. Due agents think. An agent is due when its schedule matches the tick or it was woken by
   `TryWake`/`TryWakeMany`. Groups go in creation order, agents in ascending slot order. Each think
   is one update at time `tick × TickMicroseconds`. With `UseJobs` on and at least
   `MinimumJobBatch` due agents in a group, the group's think runs as one agent-frame job; the step
   completes it before it returns. Otherwise it runs on the calling thread. Results are identical
   either way.
4. Root completions and faults are recorded in slot order. They can be read with
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

## Lifetime rules

- Capacity is allocated by `TryAddGroup` and `TryReserve`, never during `Step`. Growing a group
  keeps its handles and running agents.
- A removed agent's slot becomes reusable after the step that tears it down.
- `TryRemoveGroup`: the next step tears down the group's agents, the step after that releases its
  storage. Then the program can be disposed.
- `Dispose` releases storage without running aborts. Nothing runs outside `Step`.
- Call the container from one thread at a time. Calls made while `Step` runs (from a callback)
  return `Busy`.

## Not in G1

- Game inputs (facts), commands and external actions: stages G2 and G3.
- Effects emitted by nodes are discarded.
- Trace channels and the live debugger.
- A program whose generated nodes bind anything other than Tree- or Agent-scope blackboard keys
  or effects is refused by `TryAddGroup` with `InvalidArgument`.
