# Native node library

The package ships ready-made native (Burst) leaves under `aibt.native.*`, so a tree for
`NativeTreeAgent`, `ProductionTreeHost` or `TreeSimulation` needs no Burst code for blackboard
logic, flags, counters, distances, timers, shared cooldowns, signals to the game or seeded random
choices. Together with
the `aibt.core.*` composites and decorators they cover what most guard, patrol and cooldown trees
do. The decision record is `decisions/ADR-P11-002-native-node-library.md`; the full contract of
each node (parameters, keys, statuses) is in the [node catalog](generated/node-catalog.md).

The library nodes are **native-only**. They do not run in a `BehaviorTreeAgent`; there the
`aibt.unity.*` nodes do the same work (table below). Each node's "when not to use" names its
managed counterpart.

## Opting in

The nodes live in the package shard `aibt.native.standard` (assembly `AIBT.StandardNodes`, which
is referenced automatically). The palette and MCP `search_nodes` list them in every project, under
`Native/Blackboard`, `Native/Flow`, `Native/Timing`, `Native/Signals` and `Native/Random`. A native tree can use
them once your project's catalog set lists the shard. From the **Native Guard** sample
(`Catalog/NativeGuardCatalog.cs`):

```csharp
[AibtCatalogSet("aibt.samples.native-guard.catalog", 1u, typeof(StandardNativeShard))]
public static partial class NativeGuardCatalog { }
```

List it next to your own shards, for example
`typeof(StandardNativeShard), typeof(MyGameShard)`. If a tree uses a library node and the set does
not list the shard, Prepare Runtime fails with a message that names the shard to add.

When a package update adds nodes to the shard, the catalog fingerprint of every catalog set that
lists it changes. The Editor prepares those runtime assets again after the next domain reload; a
Player built before that reload would carry stale assets (bootstrap fails with `TypeMismatch`).

## What a library node may do

Library nodes read and write **Tree-scope** blackboard keys, read the **update time point**, draw
from **their own seeded random stream** (the `Native/Random` nodes), and emit on **one package effect
channel**, `aibt.native.signal`, with an `Int32` code. They never touch
the scene: the game writes facts in (`NativeTreeAgent.SetFact`) and reacts to signals
(`NativeTreeAgent.OnEffect`). Anything that moves, animates or queries physics stays game code.

"Now" is the update time point in microseconds: the host clock on `ProductionTreeHost` and
`NativeTreeAgent` (scaled Unity time, or the clock passed to `ProductionTreeProgram.TryBootstrap`),
`tick * TickMicroseconds` in
`TreeSimulation`. Every node in one update sees the same "now".

Integer parameters are `UInt32` (native parameters cannot be negative). A value the node cannot
store (an int constant above 2147483647, a non-finite float, a deadline past `long.MaxValue`)
makes the node fail without writing. A key that cannot be read makes the node fail.

## Managed → native

| Managed (`BehaviorTreeAgent`) | Native (`aibt.native.*`) | Category | Notes |
| --- | --- | --- | --- |
| `aibt.unity.is-true` | `bool-is-true` | Native/Blackboard | no `expected` parameter: wrap in `aibt.core.inverter` for "is false" |
| `aibt.unity.compare-float` | `float-at-least` | Native/Blackboard | `>=` only; "below" is an inverter over it |
| `aibt.unity.compare-int` | `int-at-least`, `int-equals` | Native/Blackboard | the constant is `UInt32`; for a negative bound compare two keys |
| `aibt.unity.compare-int-keys` | `int-keys-at-least` | Native/Blackboard | `left >= right`, any sign |
| `aibt.unity.compare-float-keys` | `float-keys-at-least` | Native/Blackboard | `left >= right` |
| `aibt.unity.is-within-distance` | `float3-within-distance` | Native/Blackboard | two Float3 keys (facts) instead of the transform; `flat` ignores Y |
| `aibt.unity.is-vector3-set` | `float3-is-set` | Native/Blackboard | set means not (0, 0, 0) |
| `aibt.unity.set-bool` | `set-bool` | Native/Blackboard | |
| `aibt.unity.set-int` | `set-int` | Native/Blackboard | 0 to 2147483647; a negative value is copied from a key with `copy-int` |
| `aibt.unity.set-float` | `set-float` | Native/Blackboard | |
| `aibt.unity.set-vector3` | `set-float3` | Native/Blackboard | |
| `aibt.unity.copy-bool` | `copy-bool` | Native/Blackboard | |
| `aibt.unity.copy-int` | `copy-int` | Native/Blackboard | |
| `aibt.unity.copy-float` | `copy-float` | Native/Blackboard | |
| `aibt.unity.copy-vector3` | `copy-float3` | Native/Blackboard | |
| `aibt.unity.increment-int` | `increment-int` | Native/Blackboard | wraps; an amount of 4294967295 subtracts one |
| `aibt.unity.increment-float` | `increment-float` | Native/Blackboard | the amount may be negative |
| `aibt.unity.toggle-bool` | `toggle-bool` | Native/Blackboard | |
| `aibt.unity.distance` | `float3-distance` | Native/Blackboard | `flat` ignores Y |
| `aibt.unity.succeed` | `succeed` | Native/Flow | |
| `aibt.unity.fail` | `fail` | Native/Flow | |
| `aibt.unity.idle` | `idle` | Native/Flow | runs until a parent aborts it |
| `aibt.unity.wait-seconds` | `wait-microseconds` | Native/Flow | host time; `aibt.stdlib.wait` counts ticks |
| `aibt.unity.wait-until` | `wait-until-true` | Native/Timing | waits for `true`; `timeout-microseconds` (0 = none) fails it |
| `aibt.unity.start-cooldown` | `set-deadline` | Native/Timing | Int64 key := now + `duration-microseconds` |
| `aibt.unity.is-cooldown-ready` | `deadline-passed` | Native/Timing | now >= Int64 key; a key never set (0) has passed |
| `aibt.unity.raise-event` | `signal` | Native/Signals | `code` 0 to 2147483647 on `aibt.native.signal` |
| `aibt.unity.random-chance` | `random-chance` | Native/Random | succeeds when a draw in [0, 1) is below `chance`; `chance` outside 0 to 1 fails |
| `aibt.unity.random-float` | `random-float` | Native/Random | Float32 key := uniform in [`min`, `max`); `min` > `max` fails (the managed node swaps them) |
| `aibt.unity.random-int` | `random-int` | Native/Random | Int32 key := uniform in [`min`, `max`], both included; `UInt32` bounds up to 2147483647 |
| `aibt.unity.wait-random` | `wait-random-microseconds` | Native/Random | duration in [`min-microseconds`, `max-microseconds`] of host time, drawn once at start |
| `aibt.unity.random-point` | `random-float3-in-radius` | Native/Random | Float3 `result` := `center` key + offset with `min-radius` <= distance <= `radius`; `flat` keeps Y; the centre is a key, not the transform |

These 33 nodes are the whole library today. Managed nodes with no native counterpart:
`wait-frames` (use `aibt.stdlib.wait`), `time-since-changed`, and everything that touches the scene
(movement, navigation, animation, audio, GameObject, `line-of-sight`, `log`).

## Recipes

### "Is false", "below" and "not equal"

There is one direction per comparison. Wrap the node in `aibt.core.inverter`:

```json
"cooling": { "type": "aibt.core.inverter", "typeVersion": 1, "children": ["ready"] },
"ready": { "type": "aibt.native.deadline-passed", "typeVersion": 1, "bindings": { "key": "weapon-ready-at" } }
```

"Health below 30" is an inverter over `float-at-least` with `threshold` 30; "not state 2" is an
inverter over `int-equals` with `value` 2. For a negative bound keep it in a key and use
`int-keys-at-least` or `float-keys-at-least`.

### A cooldown shared by several branches

`aibt.core.cooldown` is private to the branch it decorates. To make several branches (or game code)
respect one cooldown, keep the deadline in an Int64 key: `set-deadline` starts it and
`deadline-passed` checks it. In the Native Guard sample the zap and the flare share
`weapon-ready-at`:

```json
"zap": { "type": "aibt.core.reactive-sequence", "typeVersion": 1, "children": ["is-close", "zap-ready", "zap-fire"] },
"zap-ready": { "type": "aibt.native.deadline-passed", "typeVersion": 1, "bindings": { "key": "weapon-ready-at" } },
"zap-fire": { "type": "aibt.core.memory-sequence", "typeVersion": 1, "children": ["zap-cooldown", "zap-signal", "zap-hold"] },
"zap-cooldown": { "type": "aibt.native.set-deadline", "typeVersion": 1, "parameters": { "duration-microseconds": 1500000 }, "bindings": { "key": "weapon-ready-at" } }
```

The flare branch has the same shape with its own signal code. After a zap, no flare fires until
1.5 s have passed. Declare the key as `"type": "Int64"`. The game may also read it, or write it as a
fact, as long as no node in the tree writes the same key (a key a node writes refuses external
writes).

A key that nodes write lives as long as the run. Every run of a `NativeTreeAgent` is a new host
that starts from the tree's key defaults (facts the game set are applied again), so the cooldown
ends with the run. `zap-hold` (`idle`) keeps the run going after a zap: the reactive sequence drops
the branch at the next update, when `zap-ready` fails, and the guard goes on with the cooldown
running. Without the hold the root would succeed, the agent would start a new run with
`weapon-ready-at` = 0, and the zap would fire at every update while the intruder is close.
A finished patrol ends the run too. In the sample a patrol usually takes longer than the 1.5 s
cooldown (the gate takes about a second to open, the rest at least one more), but a patrol that
starts with the gate still open can end sooner, and an intruder back within 1.5 s of a zap is then
zapped at once. A tree whose cooldowns or counters must last across rounds loops inside one run
with a repeater at the root, as the tree of the [MCP and CLI recipes](generated/recipes.md) does.

### Signals to the game

`signal` emits its `code` on `aibt.native.signal`, once per tick of the node. The game handles all
codes in one place. From `Runtime/NativeGuard.cs` in the sample:

```csharp
_agent.OnEffect("aibt.native.signal", (int code) => OnSignal(code));
```

Pick a code per meaning (the sample uses 1 zap, 2 flare, 3 open the gate, 4 sweep and close). A
signal is delivered on the main thread after its update commits, exactly once. A `code` above
2147483647 makes the node fail and emits nothing. On a `ProductionTreeHost` bootstrapped with an
effect capacity the same effect arrives through `TryDrainEffects` with the stable id of
`aibt.native.signal`; in `TreeSimulation` read it
with `group.GetCommands(CommandChannel<int>.Create("aibt.native.signal"))`. The channel is imposed
by the package: for a float or position payload, or your own channel name, write a node with its
own `[AibtCommand]`.

### Waiting for the game

`wait-until-true` stays running until a Bool key is true. The game sets the fact when it is done:

```csharp
_agent.SetFact("gate-open", true);
```

With `timeout-microseconds` above 0 it fails once that much time has passed since it started; if
the key turns true in the same update, the key wins. Each new start (after an abort or a restart)
counts the timeout from that start.

### Guarding a branch

Native hosts do not run observer re-evaluation today: an `observer` block on a native condition
compiles and changes nothing. The graph, the tree list and MCP `validate` show the warning
`AIBT2054` on each such block, naming the node. Guard a branch with a reactive composite instead. Put the condition
first; the composite checks it again at every update and aborts the branch at the next update after
the key changes.

Choose the reactive composite by what the guarded branch does:

- `aibt.core.reactive-sequence` / `reactive-selector` (v1) abort and **re-enter** their running
  child at every update. A `wait-until-true` or `wait-microseconds` directly under them starts again
  at every update, so its timeout or duration never runs out.
- `aibt.core.reactive-sequence-retaining` / `reactive-selector-retaining` check the earlier children
  at every update and **keep** the running child while they do not take over. Use them above waits
  and multi-step branches.

The Native Guard tree uses a retaining reactive selector at the root: the zap, flare and watch
branches are checked at every update, and the patrol below them (open the gate, wait until it is
open, light a random spot, sweep, maybe whistle, rest) keeps its progress until one of them takes
over.

`deadline-passed` reads time as well as a key, so it is not an observer condition. Use it as a
reactive guard as above.

### Randomness

The five `Native/Random` nodes draw from a random stream of their own. The host derives each
node's stream from a **root seed**, the tree's semantic hash, a **stream identity** and the node's
index, as `TreeSimulation` does (`specifications/time-and-random-v1.md`). The nodes are declared
deterministic, so they pass the default `Phase1` policy: one seed, one stream identity and one
sequence of updates and facts give one sequence of results.

The Native Guard patrol picks one of three spots, whistles 40 % of the time and rests 1 to 3 s.
A picked value leaves the tree through branches (`int-equals` on `sweep-spot`, then a signal per
spot), because game code reads signals, not keys:

```json
"pick-spot": { "type": "aibt.native.random-int", "typeVersion": 1, "parameters": { "min": 0, "max": 2 }, "bindings": { "key": "sweep-spot" } },
"whistle-chance": { "type": "aibt.native.random-chance", "typeVersion": 1, "parameters": { "chance": 0.4 } },
"rest": { "type": "aibt.native.wait-random-microseconds", "typeVersion": 1, "parameters": { "min-microseconds": 1000000, "max-microseconds": 3000000 } }
```

`random-float` and `random-float3-in-radius` write Float32 and Float3 keys for later nodes, for
example `float-at-least` on a random threshold or `float3-within-distance` against a random point.

Where the seed and the stream identity live and what they mean:

- **`NativeTreeAgent.RandomSeed`** (Inspector: *Random Seed*, serialized, default `0`). It is read
  when a run starts (enable, `Restart`, an automatic restart); a change applies from the next run.
- **`NativeTreeAgent.RandomStreamId`** (Inspector: *Random Stream Id*, serialized, default `0`),
  read when a run starts. `0` is automatic: every run uses its host's process-wide instance id, so
  agents and runs differ, but nothing repeats across sessions. A nonzero id makes the draws of a run
  depend only on the seed, the tree, the id and the run's index (`NativeTreeAgent.RunIndex`, read
  only): run 0 after the agent is enabled, then +1 for each run that starts while it stays enabled
  (an automatic restart, `Restart`, a change of `Tree`; a start that fails does not count). Disable
  and enable start again at run 0, so a pooled agent re-enabled with the same id repeats run 0.
- **`ProductionTreeHost`**: `ProductionTreeProgram.TryBootstrap(host, randomSeed, out failure, ...)`
  or the generated `ProductionTreeHost.TryBootstrap(..., randomSeed, out failure)` use the host's
  instance id (automatic). Pass a `RandomStreamSeeding` instead, for example
  `new RandomStreamSeeding(seed, streamIdentity)` or `RandomStreamSeeding.ForRun(seed, id, run)` (the
  agent's rule), to choose the identity; `0` is automatic. The overloads without a seed use
  `ProductionTreeHost.DefaultRandomSeed` (`0`); `host.RandomSeed` and `host.RandomStreamIdentity`
  report what was used.
- **`TreeSimulation`**: `TreeSimulationSettings.RandomSeed` (default `0`), as before; its instance
  ids are local to the container (1, 2, ... in the order agents are added or restarted).

Agents that share a nonzero id and a seed draw the same numbers. Prefab instances share their
serialized values, so a spawner that wants different agents sets a distinct id per agent from code
(an entity number, a save-game id): only the game knows what "the same agent" is. With id `0` the
automatic id is process-wide and given in start order from 1 after a domain reload, so sequences
change with another start order, without a domain reload (Enter Play Mode Options), or after another
host started first; the inspector shows a hint when the tree has random nodes and the id is `0`.

Repeatable streams do not make a session repeat. Agents still update in start order, and nodes draw
as frame time lets them run: a branch that runs earlier or later, for example after an interruption
timed by an object that moves with frame time, consumes the same stream at another moment, and from
then on the run differs. For a replay that repeats exactly, tick by tick, use `TreeSimulation`
(lockstep is its job). A bug report can carry the seed, the stream id and the run index.

Each node's draws per tick are fixed, so tuning a range does not shift later draws: one draw for
`random-chance`, `random-float` and `random-int` (a bounded draw repeats only on the spec's
rejection), two for a flat `random-float3-in-radius` and three otherwise, and one for
`wait-random-microseconds` when it starts (none while it waits or when it is aborted). An invalid
configuration (`min` > `max`, `chance` outside 0 to 1, a negative radius, `max` above 2147483647
for `random-int`) fails without writing and without drawing.

Random nodes work in every dispatch mode: the default profiles, `Immediate`, `JobsAgentFrame`, a
profile that forces `BatchedJobsSameFrame` or `PipelinedJobs` (where agents of one catalog share a
job wave), a lone `ProductionTreeHost`, and `TreeSimulation` in place and with Jobs. One tree, seed
and stream identity draw the same sequence under every scheduling policy.

`aibt.stdlib.random-condition` (a whole-percent chance) is a built-in leaf declared
non-deterministic: the default `Phase1` policy refuses it (AIBT2040). Use `random-chance` in native
trees.

## See also

- **Native Guard** sample (`Samples~/NativeGuard`): the tree, the game script and a Create Demo
  menu.
- [Production tree host](production-host.md): clock, budget, facts and effects on the host.
- [Simulation container](simulation-container.md): command channels in `TreeSimulation`.
- [Burst node authoring](burst-node-authoring.md): write your own native node when the library does
  not cover it.
- [MCP and CLI recipes](generated/recipes.md): find the library nodes and build, validate and
  prepare a native tree from them with the authoring tools; [AI-native authoring](ai-and-mcp.md#native-only-nodes)
  says which tools can and cannot run native-only nodes.
