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
main thread with the default profile (`Normal`, `Immediate` policy): ≈2.0 µs for an agent waiting in
one leaf at N=1 000, ≈2.7 µs for one that reads a fact and emits an effect (since P10-003; it was
≈3.8 and ≈5 µs before). Each node dispatch costs roughly 1.2–1.7 µs.

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
allocates every round.

<a id="pipelinedjobs-cadence"></a>**`PipelinedJobs` cadence.** A `PipelinedJobs` group advances one
lifecycle step per frame, so it makes one logical update per (lifecycle steps of its slowest tree +
1) frames, where the default makes one update per frame. A group is the agents of one tree asset with
one profile, and the whole group waits for its slowest member. A tree waiting in one leaf
(`io-wait1`) takes 2 steps, so it updates every 3 frames. The looping `shared-loop16` takes 2, 25
or 43 steps depending on where it is (one update every 3, 26 or 44 frames; on average every 6.6
frames over 120 frames). The Swarm Arena sample's tree takes about 32 frames (31.2 to 31.9 measured
in a Player), so at 60 FPS a pipelined agent decides about twice a second. `PipelinedJobsCadenceTests`
pins the rule by counting frames and steps.

The `PipelinedJobs` rows above are therefore the main-thread cost of a frame in which an agent does a
third of an `io-wait1` update, or about a sixth of a `shared-loop16` update on average. They are not
the cost of an update. Per logical update the cost is higher than the default's: about 2.9 × 3 ≈ 8.7
µs against 3.8 µs for `io-wait1`, and about 2.6 × 6.6 ≈ 17 µs against 8.3 µs for `shared-loop16`, at
N=1 000. Choose `PipelinedJobs` only for trees that tolerate that latency. To run whole updates in
jobs every frame, use [`JobsAgentFrame`](#jobsagentframe-p10-004). Keep the default unless a
measurement of your own tree on your target says otherwise.

The table above was measured before P10-002. Since P10-002, `BatchedJobsSameFrame` reuses its round
storage and schedules one parallel job per round, and a warmed-up frame allocates 0 B. The quick
benchmark (`Invoke-Perf.ps1 -Quick`, IL2CPP, 2 runs; main-thread µs per agent and frame) then gives:

| Tree | profile | N=1 000 | N=5 000 | B per agent-tick |
|---|---|---:|---:|---:|
| `io-wait1` | default (`Immediate`) | 3.8 | 4.4 | 0 |
| | `BatchedJobsSameFrame` | 3.2 | 3.7 | 0 |
| `shared-loop16` | default (`Immediate`) | 8.3 | 8.7 | 0 |
| | `BatchedJobsSameFrame` | 6.8 | 7.6 | 0 |
| `io-move1-fact-effect` | default (`Immediate`) | 5.0 | 5.6 | 0 |
| | `BatchedJobsSameFrame` | 6.5 | 16.0 | 0 |
| `shared-reactive16` | default (`Immediate`) | 128 | 127 | 0 |
| | `BatchedJobsSameFrame` | 119 | 125 | 0 |

These numbers exclude worker time.

Since P10-003 the dispatch framing around each node is cheaper (the bridge no longer copies its
native backing on every access, catalog metadata is validated once per workspace, and the grouped
path lost two quadratic loops). Interleaved before/after, IL2CPP, quick benchmark, main-thread µs
per agent and frame:

| Tree | profile | N=1 000 | N=5 000 | B per agent-tick |
|---|---|---:|---:|---:|
| `io-wait1` | default (`Immediate`) | 2.0 | 2.5 | 0 |
| | `BatchedJobsSameFrame` | 2.0 | 2.5 | 0 |
| `shared-loop16` | default (`Immediate`) | 3.8 | 4.7 | 0 |
| | `BatchedJobsSameFrame` | 3.9 | 5.0 | 0 |
| `io-move1-fact-effect` | default (`Immediate`) | 2.7 | 3.4 | 0 |
| | `BatchedJobsSameFrame` | 2.7 | 3.5 | 0 |
| `shared-reactive16` | default (`Immediate`) | 54 | 54 | 0 |
| | `BatchedJobsSameFrame` | 63 | 73 | 0 |

The managed controls on the same runs: `m-shared-shape15` 1.0 / 1.2 µs, `m-unity15-both` 1.2 /
1.6 µs. For these small trees the managed layer is still faster per agent. Trees with facts and
effects are no longer slower under `BatchedJobsSameFrame` (`Planning~/Evidence/P10-003/README.md`).

### JobsAgentFrame (P10-004)

`SchedulingProfile.JobsAgentFrame` runs each native agent's whole update inside one Burst job work
item: the lifecycle steps, the generated node bodies and the dispatch framing around them. The main
thread keeps clock reads, the agent table, one schedule and one complete per group, trace replay
and effect delivery in agent order. It needs no `SchedulerJobsCapabilities`:

```csharp
agent.Profile = SchedulingProfile.JobsAgentFrame; // before the agent is enabled
```

Interleaved quick benchmark, IL2CPP, 23 job workers, main-thread µs per agent and frame:

| Tree | profile | N=1 000 | N=5 000 | B per agent-tick |
|---|---|---:|---:|---:|
| `io-wait1` | default (`Immediate`) | 1.9 | 2.6 | 0 |
| | `BatchedJobsSameFrame` | 2.0 | 2.6 | 0 |
| | `JobsAgentFrame` | 0.9 | 1.2 | 0 |
| `shared-loop16` | default (`Immediate`) | 3.6 | 4.5 | 0 |
| | `BatchedJobsSameFrame` | 3.8 | 5.1 | 0 |
| | `JobsAgentFrame` | 1.1 | 1.6 | 0 |
| `io-move1-fact-effect` | default (`Immediate`) | 2.6 | 3.4 | 0 |
| | `BatchedJobsSameFrame` | 2.7 | 3.5 | 0 |
| | `JobsAgentFrame` | 1.1 | 1.5 | 0 |
| `shared-reactive16` | default (`Immediate`) | 53 | 53 | 0 |
| | `BatchedJobsSameFrame` | 62 | 76 | 0 |
| | `JobsAgentFrame` | 5.0 | 4.9 | 0 |

Total CPU time, measured by running the Player with `JobsUtility.JobWorkerCount = 0` (every job
on the main thread), is also lower than Immediate: 0.63–0.74× for the small trees and 0.46× for
`shared-reactive16`, because the node bodies and the framing run Burst-compiled in the job. So the
policy saves work, not only main-thread time.

What it gains and where it does not:

- The gain grows with the number of dispatches per update. A tree that dispatches once per update
  still pays a fixed managed cost per agent on the main thread (begin and finish around the job),
  so it gains about 2×.
- Agents the job cannot run fall back to Immediate in their own due position: hosts without
  generated dispatch, hosts with a per-host `StepBudget`, and catalogs generated before P10-004
  (rebuild to regenerate them). `SchedulerFrameEntry.AgentFrameFallback` names the reason and the
  scheduler logs one warning per host.
- Every member's clock is read before the job runs, not just before its own update; this only
  matters for a custom clock that depends on other agents' updates in the same frame.
- Measured on a 24-thread desktop CPU. On a CPU with few cores the worker time is the frame time;
  measure on your target.

### Which policy when (P10-005, automatic since P10-007)

**Since P10-007 the scheduler chooses for you.** With the default profile (no `ForcedPolicy`), a
contiguous run of at least 64 job-eligible agents with the same profile and catalog in the frame's
due order runs as `JobsAgentFrame`; shorter runs stay `Immediate`. 64 is about twice the largest
crossover in the table below, so a population that is selected is already well past break-even, and
one that is not pays nothing. With `NativeTreeAgent` the catalog is per tree asset, and due order is
mostly the order agents were enabled: **64 consecutive agents of one tree**. Agents of several trees
enabled interleaved form short runs and are not selected; enable each tree's agents together, or
force the profile.

| You want | Do |
|---|---|
| Convenience (default) | nothing: runs of 64+ eligible agents use `JobsAgentFrame` automatically, everything else `Immediate` |
| The previous behaviour, everything on the main thread | `scheduler.AutomaticAgentFrameSelection = false;` (`world.GetComponent<ProductionTreeScheduler>()` for a `ProductionTreeWorld`) |
| `JobsAgentFrame` regardless of the count | `agent.Profile = SchedulingProfile.JobsAgentFrame;` (batches shorter than `scheduler.AgentFrameMinimumBatchSize`, default 64, still run in place; set it to 1 to schedule a job for every batch) |
| `Immediate` for some agents only | a profile built with `SchedulingProfile.TryCreate(..., forcedPolicy: SchedulingPolicy.Immediate, ...)` |
| A different threshold | `scheduler.AgentFrameMinimumBatchSize = n;` (applies to both the automatic and the forced path) |

Notes on the automatic choice:

- It applies only with the default unbounded scheduler budget. With `SetFixedBudget` or
  `SetBudgetProvider` every default-profile agent keeps the per-agent `Immediate` admission
  (`MaximumBudgetShare` included).
- An agent the job cannot run (no generated dispatch, a catalog generated before P10-004, a
  `StepBudget`) stays on its policy without a warning and ends the run it is in. The one warning per
  host remains for the forced `SchedulingProfile.JobsAgentFrame`.
- Frame entries show the choice: `SelectedPolicy = JobsAgentFrame` with
  `SelectionReason = AutomaticAgentFrameRun`; a batch that ran on the main thread because it was
  shorter than the minimum reports `AgentFrameRanInPlace`.
- **Trees with many reactive re-evaluations gain below 64 too.** `shared-reactive16` is 5× faster
  under `JobsAgentFrame` already at 10 agents (P10-005). The threshold leaves such small groups on
  `Immediate`, and since P10-007 a forced `SchedulingProfile.JobsAgentFrame` group below 64 runs in
  place as well. For a small group of expensive reactive agents, force the profile and set
  `scheduler.AgentFrameMinimumBatchSize` lower (for example 1).
- Results, traces, effects and their order are identical to `Immediate`. The differences are the
  ones listed for `JobsAgentFrame` above: clock read order within a batch, work on job workers
  (competing with the game's own jobs), and `LastMeasuredMicroseconds` being the batch average.

The measurement behind the threshold:

Measured in an IL2CPP Player on Unity 6000.6.9f1 (the earlier tables in this page are from
6000.7.0b1; compare policies within one table, not across), 24-thread desktop CPU, main-thread µs
per frame for the whole population (`Planning~/Evidence/P10-005/README.md`):

| Agents with the same tree | `Immediate` (default) | `JobsAgentFrame` | Use |
|---|---|---|---|
| 1–10 | 16–50 µs (simple trees), 70–550 µs (`shared-reactive16`) | +3…+17 µs slower for simple trees; 1.6–5× faster for the reactive tree | default |
| 20–30 | simple trees 55–135 µs | ≥10% faster from 20 (looping, fact/effect trees) or 30 (one waiting leaf) | either |
| 50–500 | | 1.4–9× faster | `JobsAgentFrame` |
| 1 000–20 000 | | 1.7–3.5× faster (simple trees), 10–11× (reactive) | `JobsAgentFrame` |

- `JobsAgentFrame` also uses less CPU for the agents' work itself (0.45–0.88× of Immediate on one
  thread). With many idle job workers the process can use 0.3–1.2 ms more CPU per frame at 10–500
  simple agents, because woken workers spin before they sleep.
- **Few cores** (simulated by limiting the job workers on the same CPU): with 0, 1 or 3 workers it is
  still faster than Immediate (0.49–0.80× with none, 0.27–0.68× with one).
- **Keep fallback agents together.** An agent that cannot run in the job (no generated dispatch, a
  `StepBudget`, an old catalog) closes the current job batch. If such agents are spread among
  job-eligible agents of the same profile, every batch holds one agent and the population becomes
  1.3–1.7× slower than plain `Immediate`. Give them a different profile, or keep them out of the
  population that uses `JobsAgentFrame`. Since P10-007 a piece shorter than 64 agents runs in
  place instead of in a one-agent job, so such a population runs at about `Immediate` speed (not
  re-measured); it still gains nothing from the job.
- **`UpdateCadence`** works with `JobsAgentFrame`. When many agents are enabled in the same frame
  they stay in step; enabling them over several frames spreads their updates (p99 frame 2.7–4.5×
  lower).
- `BatchedJobsSameFrame` was not faster than `Immediate` by more than 2% in any measured case; prefer
  `JobsAgentFrame`.
- The managed layer (`BehaviorTreeAgent`) costs 1.15–1.5 µs per agent at 1 000–5 000 agents for a
  15-node tree; native agents under `JobsAgentFrame` cost 0.9–1.6 µs for comparable trees there and
  more at 20 000 (1.8–2.3 µs).

### Step limit

Every native policy stops an update after `ProductionTreeHost.MaximumStepsPerUpdate` lifecycle
steps (default 1 000 000) and fails the host with `NativeUpdateStepLimitExceeded`. The trees in the
benchmarks need between a few and a few hundred steps per update; only a tree that never yields
(for example a repeater over an instant leaf) reaches the default. Lower it on a host to catch such
a tree sooner.

## Size and memory (P11-015)

Windows x64, IL2CPP, release build, Unity 6000.6.9f1, Built-in pipeline, managed stripping `Minimal`
unless named. Compared with the same project without AIBT. Raw data and method:
`Planning~/Evidence/P11-015/README.md`, section "Size and memory".

| Player | Shipped size added | `GameAssembly.dll` added | Burst library |
| --- | --- | --- | --- |
| Native trees only (First Agent sample) | 22.6 MB | 17.3 MB | 0.39 MB |
| Native trees only, stripping `High` | 15.2 MB | 11.8 MB | 0.37 MB |
| Managed trees only (Simple Agent sample) | 22.5 MB | 17.3 MB | 0.31 MB |

- Both kinds of Player contain `AIBT.Runtime`, `AIBT.Authoring` and `AIBT.Agents`. `AIBT.Authoring`
  stays in a native-only Player under `High` stripping (230 KB of IL).

| Tree asset in the build, per node | Bytes |
| --- | --- |
| Prepared native tree (`.runtime.asset`) | 353 (705 on disk) |
| Managed tree (`.aibt`, carried as JSON) | 230 |

Measured on generated trees of 10, 100 and 1000 nodes; size grows linearly with the node count. The
prepared asset stores the program twice, and every node's debug entry holds the tree's source path,
so a longer path makes every node larger.

Memory per added agent, measured after every agent has updated once; a line fitted to N = 100 and
1000 (and 300 for `NativeTreeAgent`), Windows x64 IL2CPP release Player:

| Agent | Tree nodes | Managed | Native |
| --- | --- | --- | --- |
| `NativeTreeAgent` | 1 | 6.1 KB | 60 KB |
| `NativeTreeAgent` | 109 | 143 KB | 963 KB |
| `BehaviorTreeAgent` | 17 | 16.5 KB | 0.7 KB |

- `NativeTreeAgent` adds a one-time native block of about 74 MB with the first agents, on top of the
  per-agent figures (100 agents took 63 MB in one run and 89 MB in another).
- A native agent's memory grows by about 7.3 KB native and 1.3 KB managed per tree node: 1000 agents of
  a 109-node tree take 1.0 GB native and 144 MB managed.
- `BehaviorTreeAgent` adds about 0.7 MB managed and 3.2 MB native once.
- Not measured: other platforms, Mono, development builds, mobile devices.

## Advice

- Start with the managed layer and `EveryFrame`; switch slow-changing agents to `Interval`, and turn
  on `StaggerIntervalTicks` when many are spawned together.
- Keep reactive conditions few; each one is re-evaluated every tick.
- In the native layer, prefer trees that loop over trees that finish and restart.
- For native populations, keep the default profile: from 64 consecutive agents of one tree the
  scheduler uses `JobsAgentFrame` by itself (see "Which policy when"). Force
  `SchedulingProfile.JobsAgentFrame` and lower `AgentFrameMinimumBatchSize` only if you want the
  job for smaller groups too (expensive reactive trees), or turn
  `AutomaticAgentFrameSelection` off for everything on the main thread; measure on your target,
  especially on mobile CPUs.
