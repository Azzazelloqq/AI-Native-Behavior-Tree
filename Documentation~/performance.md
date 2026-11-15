# Performance

What both runtime layers cost in a Player, measured, and how to keep the cost low. Numbers are
from P10-001 (`Planning~/Evidence/P10-001/README.md`); the harness is in
`Benchmarks~/Phase10/PlayerPerf/`.

## Machine and method

Windows 11, Intel Core Ultra 9 275HX (24 logical processors, 23 Unity job workers), 32 GB, Unity
6000.7.0b1, IL2CPP, managed stripping High, non-development Player, no vSync. A frame's cost is the
main-thread time from the first script `Update` to the last `LateUpdate`; each scenario is warmed
up (150 frames, 60 at N ≥ 10 000) and measured for 400 frames (300 at N ≥ 5 000, 150 at N ≥ 10 000),
three runs, the median of the run means is shown. Allocation is measured in a 60-frame window with
the GC disabled. Worker-thread time of Jobs policies is not measured.

These are synthetic trees with trivial leaves. They show the cost of AIBT itself, not of your
nodes; a node that raycasts or moves a NavMeshAgent costs what that Unity call costs.

## Managed layer (`BehaviorTreeAgent`)

A warmed-up tick allocates nothing (0 B per agent-tick in every managed scenario, no GC during
measurement), including blackboard writes and reads from game code, reactive re-evaluation,
parallels and restarts. Fixed-string blackboard values are the exception: they allocate when written
and read.

| Tree, ticked every frame | µs per agent-tick | agents at 1 ms | agents at 4 ms |
|---|---:|---:|---:|
| 15 nodes, Unity standard nodes (`unity-shape15-both`) | 1.2 (N=1 000) | ≈830 | ≈2 700 |
| 15 nodes, core and stdlib nodes (`shared-shape15`) | 1.0 | ≈970 | ≈3 200 |
| 16 nodes, 2 reactive conditions (`shared-reactive2`) | 1.2 | — | — |
| 16 nodes, 16 reactive levels (`shared-reactive16`) | 9.6 | ≈100 | ≈400 |
| 64 reactive conditions (`shared-reactive64`) | 39 | — | — |
| Simple Agent sample (movement, raycasts, events), per agent | 1.1 | ≈880 | ≈2 400 |

The cost of a reactive tree grows with the number of conditions it re-evaluates every tick.

### Tick less often

`TickMode = Interval` is the largest lever: 1 000 agents ticking every 100 ms cost 0.08 ms per
frame instead of 1.2 ms; 5 000 cost 0.35 ms.

Agents created in the same frame tick in the same frame, and so do agents that went through a
frame longer than the interval (such as the frame after a large spawn): every Interval agent is due
then, and they stay in step afterwards. At 1 000 agents that is a 1.3–7 ms frame every 100 ms.
With `StaggerIntervalTicks` (off by default) each agent keeps its own phase within the interval,
also across long frames, so the ticks spread over the frames of the interval; every tick receives
the real time since the previous one. Use it when you spawn many Interval agents at once.

Measured (IL2CPP Player, `unity-shape15-both` every 100 ms, 3 000 frames, µs of main thread per
frame; run `il2cpp-20261115-155326`):

| N | stagger | mean | p99 | max frame (3 runs) |
|---:|---|---:|---:|---:|
| 1 000 | off | 78 | 130 | 6 230–6 922 |
| 1 000 | on | 81 | 137 | 252–306 |
| 5 000 | off | 363 | 441 | 19 890–21 091 |
| 5 000 | on | 416 | 660 | 1 035–1 789 |

The worst frame drops 11–25×; the mean and p99 rise slightly because the ticks now land on many
frames instead of one in every few hundred.

## Native layer (`NativeTreeAgent`)

A native tick allocates nothing while the tree runs. The fixed cost is per agent and update, on the
main thread with the default profile (`Normal`, `Immediate` policy): ≈3.8 µs for an agent waiting in
one leaf at N=1 000, ≈5 µs for one that reads a fact and emits an effect; each further leaf step
costs about 8 µs.

Two things allocate:

- **Restarts.** Every restart (`NativeTreeRestartMode`) destroys the agent's `ProductionTreeHost` and
  creates a new one, which allocates (about 1.7–2 KB per agent-tick for a 15-node tree that finishes
  every ~22 ticks, and GC every few frames at thousands of agents). For a tree that finishes every few
  ticks, loop inside the tree (a repeater at the root, as `shared-loop16`: 0 B) instead of restarting.
- **Game code that makes the tree finish more often** restarts it more often: the cost follows the
  restart rate, not the facts.

At the same N the managed layer is faster for these small trees: the native layer's strengths are
zero GC while running, deterministic Burst execution and a world that stays off the scene.

### Jobs policies

`BatchedJobsSameFrame` and `PipelinedJobs` are opt-in through a profile that forces them, and they
need `SchedulerJobsCapabilities` on the world's scheduler (there is no default):

```csharp
SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, pipelinedJobsPermitted: true,
    out var capabilities, out _);
world.GetComponent<ProductionTreeScheduler>().SetJobsCapabilities(capabilities);
SchedulingProfile.TryCreate("my.pipelined", 0, 1u, null, null, true, SchedulingPolicy.PipelinedJobs,
    out var profile, out _);
agent.Profile = profile; // before the agent is enabled
```

The shared world is the GameObject `AIBT Native World` created by the first `NativeTreeAgent`; or
assign your own `ProductionTreeWorld` to `NativeTreeAgent.World`. Without capabilities an agent with
such a profile faults with a message that names the missing call.

Main-thread µs per agent and frame (worker time not included):

| Tree | profile | N=1 000 | N=5 000 | N=20 000 | B per agent-tick |
|---|---|---:|---:|---:|---:|
| one waiting leaf (`io-wait1`) | default (`Immediate`) | 3.8 | 4.4 | 4.9 | 0 |
| | `BatchedJobsSameFrame` | 15.0 | 12.3 | 8.9 | ≈1 100 |
| | `PipelinedJobs` | 2.9 | 5.3 | 8.3 | ≈260 |
| looping 16-node tree (`shared-loop16`) | default (`Immediate`) | 8.3 | 9.0 | 9.6 | 0 |
| | `BatchedJobsSameFrame` | 36.3 | 24.1 | 19.2 | ≈2 600–2 900 |
| | `PipelinedJobs` | 2.6 | 4.6 | 10.8 | ≈65–110 |

`BatchedJobsSameFrame` costs more main-thread time than the default at every measured N and
allocates every round. `PipelinedJobs` costs less main-thread time at N=1 000 but advances each
tree about once every three frames (a round spans frames), so it does a third of the work per frame;
per unit of work it is not cheaper. Keep the default unless a measurement of your own tree on your
target says otherwise.

## Advice

- Start with the managed layer and `EveryFrame`; switch slow-changing agents to `Interval`, and turn
  on `StaggerIntervalTicks` when many are spawned together.
- Keep reactive conditions few; each one is re-evaluated every tick.
- In the native layer, prefer trees that loop over trees that finish and restart.
- Keep the default native profile unless a measurement on your target shows a Jobs policy is faster.
