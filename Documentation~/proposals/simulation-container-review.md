# AIBT simulation container: review for the game team

Status: proposal for review, nothing implemented yet. Full design and code evidence:
`Documentation~/decisions/ADR-P10-008-simulation-container.md`. Reading time: about ten minutes.

## In short

AIBT will add a `TreeSimulation` container that your game owns and steps from its own tick. It
holds groups of agents (one compiled tree per group), with no GameObject or MonoBehaviour per
agent. Agents are slots with stable handles (index + generation). Each agent thinks every N ticks
at a phase you choose, and you can wake it early. Nodes read your per-agent data from native
arrays you fill each tick, and write your command structs into buffers you read after the step.
Long actions run until you report their result; when a higher-priority branch takes over, you get
a cancel event. The tree runtime underneath is the existing native core, which is already measured
and tested. The container only replaces the component shell around it.

## Your requirements: where AIBT stands today and after the plan

| # | Requirement | Today | After | Stage |
|---|---|---|---|---|
| 1 | N agents per container, several programs and containers, runtime add/remove, stable handles, no global state | one component per agent; process-wide id counter | `TreeSimulation` → `TreeGroup` per program → `AgentHandle {Index, Generation, Group}`; identity is local to the container | G1 |
| 2 | Explicit `Step(tick)`, `uint` ticks, waits/cooldowns in ticks | driven by Unity `Update`, microsecond clock | `Step(uint tick)`; tree time = `tick × TickMicroseconds` (see Q1) | G1 |
| 3 | Think rate + phase, early wake on event or changed fact | frame cadence without phase; "urgent" does not wake | `ThinkSchedule(interval, phase)` / `Spread(interval)`, `TryWake`, `TryWakeMany`, optional wake when an input row changed | G1, G2 |
| 4 | Bulk facts from native arrays, read in Burst by agent index | per-agent blackboard writes | `TryBindInput(channel, NativeArray<T>)`; a node reads its own row (existing snapshot read, works in Burst and in reactive conditions) | G2 |
| 5 | Game command structs into a native buffer | closed set of three payload types, managed queue | `GetCommands(channel)` → `NativeArray<Command<T>>.ReadOnly`, ordered by agent, then emission | G2 |
| 6 | Externally completed actions, preemption visible as cancel | the tree-side API exists, the runtime side is not connected | `GetActionEvents()` (Started / Cancelled with reason), `TryComplete(action, outcome, result)` wakes the agent; every started action ends in your completion or a Cancelled event | G3 |
| 7 | Reactive interrupts | reactive composites re-check every update | same semantics; latency = think interval unless woken (wake on input change, wake on completion) | G1–G3 |
| 8 | EditMode, same results, deterministic on one machine | needs Play Mode; ids and seeds are process-wide | container runs anywhere; results depend only on your calls, inputs and seed; jobs on/off give identical results | G1 |
| 9 | No per-step allocations, jobs complete in the step, nothing between steps, one thread | zero GC is measured; one mode crosses frames | `Step` allocates nothing after warm-up, completes its jobs, returns `Busy` if called during a step; add/remove are applied inside the next step | G1 |
| 10 | Trace one agent; "why is agent N doing X" | trace per component, internal active-path query | `TrySetTraced(agent)`, `TryGetActivePath(agent, …)`, optional last result per node | G4 |
| — | Group-shared blackboard (provisional) | exists in the format, not connected | open design slot, the API leaves room for it | later |
| — | 500 agents at ~6 Hz in a 30 Hz tick (provisional) | not measured | measured in G5 (estimate below) | G5 |

## API sketch

Namespace `AIBT.Simulation`. Every call that can fail returns a `SimulationResult` code, and
nothing on the per-tick path throws.

```csharp
var sim = new TreeSimulation(TreeSimulationSettings.Default(tickMicroseconds: 33_333));  // you own it
sim.TryAddGroup(program, new TreeGroupSettings { Capacity = 512, DefaultThinkInterval = 5,
    CommandQuotaPerAgent = 4, ActionCapacityPerAgent = 2 }, out TreeGroup soldiers);

soldiers.TryAdd(ThinkSchedule.Spread(5), out AgentHandle a);   // stable Index = row in your arrays
soldiers.TryRemove(a);                                          // teardown + cancels happen in the next Step
soldiers.TrySetThinkSchedule(a, ThinkSchedule.Every(5, phase: 2));
soldiers.TryWake(a);  soldiers.TryWakeMany(indices);

soldiers.TryBindInput(InputChannel<Senses>.Create("senses"), sensesArray);   // bound once, read inside Step
sim.Step(tick);                                                              // all work happens here
foreach (var c in soldiers.GetCommands(CommandChannel<MoveCommand>.Create("move"))) { /* c.Agent, c.Value */ }
foreach (var e in soldiers.GetActionEvents()) { /* Started or Cancelled(e.CancelReason) */ }
soldiers.TryComplete(action, ActionOutcome.Succeeded, in result);            // applied at the next Step
```

A node, written with the existing node API (marked attributes are proposed short forms):

```csharp
public partial struct ChaseConfig
{
    [AibtInput("senses")]          public SnapshotReadHandle<Senses> Senses;            // your struct, this agent's row
    [AibtCommand("move")]          public CommandHandle<MoveCommand> Move;              // your struct
    [AibtExternalAction("attack")] public AsyncOperationHandle<AttackStart, EmptyValue> Attack;
    [AibtActionResult("attack-done")] public CompletionHandle<EmptyValue> AttackDone;
}
// Tick: TryReadSnapshot(… out senses) → TryEmit(… move) → TryStart(… out opId) → Running
//       later ticks: TryConsume(… opId …) → StaleCompletion = still Running, else Success/Failure
// Abort: TryCancel(… opId …) → you receive Cancelled (Preempted). Without it, the container cancels for you (NodeEnded).
```

Your structs are registered in your own assembly with `[AibtBurstValue("game.senses", 1, …)]` and
`[AibtValueField]`. AIBT never references your types. A bound array is checked once against the
node's declared type.

### Ownership, validity, ordering

- **Ownership.** You own the container, the programs and the arrays you bind. The container owns
  the agents and every buffer it returns.
- **Validity.** Returned buffers are valid until the next `Step` (or until you remove the group or
  dispose the container). A bound array must stay alive while it is bound.
- **Threading.** Make every call from one thread, and never during `Step` (such calls return
  `Busy`). AIBT never calls into your code.
- **Handles.** A removed slot is reused after the next `Step`, with generation + 1. Old handles are
  refused with `StaleHandle`.
- **Capacity.** You set it explicitly and grow it with `TryReserve`, which you must not call during
  a step. Commands have a quota per agent, so overflow is deterministic: the emitting node fails.
- **Ordering.** Groups run in creation order. Inside a group, agents and all outputs are in
  ascending slot index; inside one agent, in emission order. Completions apply in the order you
  reported them. Removed agents are torn down before anything thinks.
- **Determinism.** Results depend on the programs, `TickMicroseconds`, `RandomSeed`, your calls,
  ticks and input contents. They do not depend on wall-clock time, measured durations, job workers
  or other containers. No time-measured admission exists in the container.

## How it will be proven

1. **EditMode test, no GameObjects.** Two programs, 500 agents, a `for` loop calling `Step(tick)`
   for 300 ticks, with adds, removes and completions at fixed ticks. A per-tick digest (root
   results, active paths, commands, action events) must be equal across two runs in the same
   process, across jobs on and off, and across 0 and N job workers. The test also checks zero GC
   bytes over 100 ticks after warm-up, exactly one `Cancelled (Preempted)` when a reactive branch
   takes over a running action, and that a late completion is refused.
2. **Benchmark** in an IL2CPP Player on the existing AIBT harness: 500 agents, think every 5 ticks
   with spread phases, `Step` µs median and p99, with 0 and N job workers, compared with the same
   trees on today's components.

**Estimate (not a measurement)** from earlier measurements of the same native core (IL2CPP,
24-thread CPU): 100 agents think per tick. Expect about 90–150 µs per `Step` for small trees with
job workers, about 160–320 µs with none, and about 0.5–2.6 ms for a 16-node tree that re-evaluates
reactively on every think. A tick is 33 ms. G1 replaces this estimate with a measured baseline.

## Plan

- **G1:** container, `Step`, handles, think rate, phase and wake, EditMode test, baseline benchmark.
- **G2:** inputs and commands.
- **G3:** external actions.
- **G4:** trace and active-path query.
- **G5:** check on Unity 7 and the 500-agent, 6 Hz benchmark.

Each stage ends with its test or benchmark result.

## Questions for you (answers change the design)

1. **Durations.** Can designers author waits and cooldowns in time units that take effect at tick
   granularity, or must the authored unit be ticks?
2. **Latency.** At 6 Hz, a cooldown that expires between thinks is seen up to 4 ticks late.
   Acceptable, or do you need wake-at-deadline?
3. **Other agents' rows.** Do nodes need to read other agents' rows (a target's data by index), or
   only their own?
4. **Global counters.** Is a process-wide id counter that never affects results acceptable, or must
   all counters be per container?
5. **Completions.** Is it enough that a completion reported between ticks is seen at the next
   `Step`? Seeing it within the same `Step` would need a callback during the step.
6. **Root completion.** Do your trees loop at the root (the default restarts them), or should the
   game decide what happens when a tree finishes?
7. **Shared squad data.** Is it written only by the game, or also by nodes? Under jobs, node writes
   become visible to other agents only at the next step.
