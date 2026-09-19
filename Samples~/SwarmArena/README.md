# Swarm Arena

`Swarm Arena` is a gameplay sample for the public production boundary. It is not a benchmark and
does not make an FPS or scalability claim.

In Unity Package Manager, select **AI-Native Behavior Tree**, open its **Samples** tab, and import
**Swarm Arena**. Then open the imported `SwarmArena.unity` scene and press Play. The scripted loop
advances every four seconds:

```text
patrol -> beacon pursuit -> danger flee -> attack with cooldown -> return to patrol
```

Blue agents patrol, green agents pursue, yellow agents flee, and red agents are in attack range.
Use `WASD` to move the beacon, `1`/`2`/`3` for the 64/256/512 population presets, `Space` to pause
or resume scripted phases, and `R` to restart at patrol. Agent hosts stay stable across phase changes;
the overlay shows their created/destroyed counts, committed state counts, profiles, logical updates,
attacks, a current 0.5-second Runtime FPS average, and public scheduler-frame disposition/budget
telemetry. Runtime FPS is shown in both Editor and Player for live orientation; it deliberately is not
a Player performance result.

## What runs where

The authored [`swarm-arena.aibt.json`](Trees/swarm-arena.aibt.json) is one reactive priority tree.
Its three sample-generated nodes are:

- `within-radius`: the observer guard used for danger, target sensing and attack range;
- `steer`: emits public `Float3` move and `Int32` visible-state effects toward or away from a goal;
- `attack-with-cooldown`: keeps instance-local logical-clock memory and emits attack/state effects
  only at the configured cooldown boundary.

All agents receive `agent-position`, `beacon-position`, `danger-position` and `patrol-point` through
`ProductionTreeHost.TryResolveExternalTreeWrite`/`TryWriteExternalTreeValue`. The sample resolves
each handle once, writes only during `ProductionTreeWorld`'s safe idle phase, and no node writes
those slots.

The Unity-side `SwarmArenaAgent` only stages those facts and consumes committed
`IProductionTreeEffectReceiver` effects. It does not read generated command buffers, access Jobs,
or run a duplicate C# behavior state machine. One half of the agents uses the unchanged
zero-configuration `SchedulingProfile.Normal`; the other half uses the explicitly disclosed
PipelinedJobs profile. Both share the one scheduler budget shown in the overlay. The visual facade
submits four instanced state groups rather than attaching one Renderer to every host; rendering does
not decide the displayed population limit.

The source generator's public configuration ABI accepts `Bool`, `UInt32`, `UInt64` and generated
handles. Range values are therefore authored as `UInt32` millimetres (for example `4000` = 4 m),
rather than adding a private float configuration path.

## Debugging one live agent

Select any `Swarm Agent NN` GameObject while playing, then use **AIBT > Samples > Swarm Arena >
Attach Selected Agent Trace**. It opens the existing read-only trace timeline on that host's
`TraceChannelOwner`; the sample does not create a debugger registry or a second tree instance.

## P7-035 handoff

P7-035 uses this exact fixed seed (`7034`), tree/catalog, scripted phases and five fixed population
points in a release Player. Its results record Player hardware/build settings and separately measure
P7-039 input writes plus immediate and pipelined latency. Editor overlay timing is not benchmark
evidence.

To reproduce the canonical matrix from an AIBT source checkout, run the following from the package
root and retain its JSON, build evidence, and Player log together:

```powershell
pwsh -File Benchmarks~/Phase7/SwarmArena/Run-SwarmArenaPlayerBenchmark.ps1
```

The default is the complete 60-cell matrix in a release Windows x64 IL2CPP/Burst Player and takes a
long-running unattended session. `-PlayerCellLimit` is a diagnostic smoke-run option only; it is not
canonical benchmark evidence.
