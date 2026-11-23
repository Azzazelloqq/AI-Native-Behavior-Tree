# ADR P11-008: Reproducible random streams for native hosts

- Status: Accepted (2026-11-23). Proposed on 2026-11-22 by P11-008 (review finding F13); the open
  questions were decided by the orchestrator under the owner's delegation of 2026-11-23, after an
  independent read. Implemented by P11-012.
- Decision ID: AIBT-052
- Tasks: `Planning~/Tasks/P11/P11-008-review-fixes-batch-2.md` (proposal),
  `Planning~/Tasks/P11/P11-012-reproducible-random-streams.md` (decisions and implementation)
- Related: [ADR P11-002](ADR-P11-002-native-node-library.md) (random nodes on native hosts);
  normative text: `Documentation~/specifications/time-and-random-v1.md`, section "Stream identity".

File references in Context are relative to the package root at commit `0aa9a6a`, before P11-012.

## Context

A node that declares a random stream (`[AibtRandomStream]`) gets one PCG stream per host, derived
before the first update from four values (`Runtime/State/Native/NativeRandomStreamV1.cs:19-45`):
the root seed, the program's semantic hash, a 64-bit word the specification called `TreeInstanceId`
and the node's runtime index. The normative text is
`Documentation~/specifications/time-and-random-v1.md`: the rule names `TreeInstanceId` as an input
(`:22`), the byte layout mixes it after the hash (`:37`), zero is invalid for it (`:41`), and a
restart keeps it, so "a host that requires a distinct sequence supplies a distinct root seed or tree
instance identity according to its lifecycle policy" (`:102-108`). The specification leaves the
policy to the host.

The host passes its own instance id: the adapter seeds every slot with `_treeInstanceId`
(`Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:795-800`), and each slot
derives its stream from it (`:1535-1542`).

That id is process-wide: `ProductionTreeHost.s_nextTreeInstanceId` (`ProductionTreeHost.cs:43`) is
incremented by every bootstrap, generated or not (`:230`, `:271`), before the bootstrap can fail: a
failed bootstrap returns at `:231` or `:272-273` and has still consumed an id (shown at run time by
`NativeTreeAgentRandomSeedTests`: the host bootstrapped after a failed bootstrap has an id two above
the one before it). The streams are seeded at `:274-276`. `NativeTreeAgent` adds a new hidden host
for every run (`NativeTreeAgent.cs:307-313`): on enable, on `Restart` (`:238-239`) and on an
automatic restart (`:302-303`), each time with the serialized `RandomSeed` (`:44`, `:86`) through
`ProductionTreeProgram.TryBootstrap(host, randomSeed, ...)` (`ProductionTreeProgram.cs:33-41`).

So the same seed draws other numbers when anything else bootstraps a host first: another start
order, Enter Play Mode without a domain reload (the counter keeps counting), an Edit-mode host, a
failed bootstrap, or a run that ends at another frame and so shifts every later id. The user guide
said so (`Documentation~/native-node-library.md:224-232`). The tests that pinned sequences set the
private counters by reflection (`Tests/Integration/NativeRuntime/NativeTreeAgentRandomSeedTests.cs:158`,
`Tests/Integration/Simulation/HostRandomSeedingTests.cs:21`, and the container's counter at `:22-23`),
so they proved the derivation, not reproducibility.

`TreeSimulation` already avoids this: its ids are local to the container, 1, 2, ... in the order
agents are added or restarted (`Runtime/Simulation/TreeSimulation.cs:203-211`, used at
`Runtime/Simulation/TreeGroup.cs:506-508`).

The id has other users that need uniqueness, not stability: read and write handles check it
(`ProductionTreeHost.cs:379`, `:505`), traces and MCP name hosts by it, and the scheduler uses it as
the final tie-break of its update order (`Runtime/Scheduling/Production/ProductionTreeScheduler.cs:1008`).

## Decision

Decided by the orchestrator under the owner's delegation of 2026-11-23, after an independent read.

1. **Identity is opt-in.** `NativeTreeAgent` gets a serialized `RandomStreamId` (`ulong`, an
   Inspector field and a property). `0` means automatic: exactly the behaviour before this ADR (the
   process-wide host instance id; agents differ, runs are not repeatable across sessions). Nonzero
   means repeatable: the stream identity of a run is a function of (`RandomStreamId`, run index)
   only. There is no generated GUID and no refusal: a serialized id is shared by prefab instances
   and would make clones act in unison, and only the game knows what "the same agent" is.
2. **Run index.** 0 on every enable, +1 for each run that started while the component stays enabled
   (automatic restart, `Restart`, a change of `Tree`); a start that failed does not consume one. One
   agent life lasts from enable to disable, so a pooled agent that is re-enabled with the same id
   repeats run 0. It is exposed read-only as `NativeTreeAgent.RunIndex`, so a bug report can carry
   (seed, id, run).
3. **Host API, additive.** `RandomStreamSeeding` carries the seed and the stream identity as one
   value type (not two adjacent `ulong` parameters); identity 0 means automatic. New overloads
   `ProductionTreeHost.TryBootstrap(definition, catalog, traceCapacity, clock, RandomStreamSeeding, out failure)`
   and `ProductionTreeProgram.TryBootstrap(host, RandomStreamSeeding, out failure, ...)` take it;
   `ProductionTreeHost.RandomStreamIdentity` reports the word used. The existing overloads keep
   their behaviour; their XML docs say that a seed alone is not repeatable across sessions.
   `InstanceId` keeps every other use (handles, traces, MCP, scheduler order) and stays process-wide
   even when a stream identity is given.
4. **`TreeSimulation` is not changed.** Its identity stays the container-local instance id. The
   package's host/container equivalence test now passes identity 1 through the new overload instead
   of setting the container's private counter by reflection.
5. **The derivation keeps its name** (`AIBT-PCG-XSH-RR32-v1`): the mix and the byte layout are
   unchanged, so every stream derived from the same four words is unchanged. The specification is
   amended: the mixed word is the "stream identity"; it states the host's default policy and the
   agent's policy, and defines the combine function (id, run) to a nonzero word with test vectors:
   `z = Mix64(Mix64(id xor 0x414942545354524d) + run)`, and `0x414942545354524d` (ASCII `AIBTSTRM`)
   when `z` is 0. It is neither `id + run` nor `id xor run`, which collide ((1, 1) and (2, 0)); for one
   id it is a bijection of the run index, and it never yields 0 (the bijection claim is corrected by
   the amendment of 2026-11-24 below). Public as
   `RandomStreamSeeding.CombineStreamIdentity`, and `RandomStreamSeeding.ForRun(seed, id, run)` gives
   a bare host the agent's rule.
6. **Stated non-goals** (here, in the specification and in the user guide): the scheduler's update
   order still follows start order, and draws inside a run follow frame time, so equal streams do
   not make two sessions behave identically; lockstep replay is `TreeSimulation`'s job.

The Native Tree Agent inspector shows a help box when the assigned tree has random nodes and the id
is 0.

## Amendment 2026-11-24 (P11-014)

Decision 5 called the combine function a bijection of the run index for one id. It is not quite one:
`Mix64(Mix64(id xor D) + run)` is, but the one run whose mix is 0 is mapped to `D`, which one other
run reaches by itself, so for one id exactly two run indexes share a value (for id 1, runs
`0x0fa0ecdca6488920` and `0x7d6af27209acafb0` both give `0x414942545354524d`). Across ids, collisions
exist and are not adjacent: (a, r) equals (b, r + Mix64(a xor D) - Mix64(b xor D)) for every r, so id
1 with run r equals id 2 with run r + `0x5bb7ebdb0fec06fe`. The decision stands: the function never
yields 0, neighbouring pairs do not collide, and a game reaches neither case by counting runs. The
specification's "Stream identity" and the doc comment of `RandomStreamSeeding.CombineStreamIdentity`
are corrected, and `RandomStreamSeedingTests` pins both examples.

The specification also said a host's `InstanceId` is assigned by every bootstrap, including one that
fails. A bootstrap the host refuses at once (already bootstrapped, disposed, or called while it drives
an update) returns before the counter and takes no id; a failure after it (the catalog cannot be
retained or does not match, instance, scheduling or trace capacity cannot be allocated) has taken one.
The managed-dispatch overload takes its id first, so it consumes one even when refused, and a refused
effect-delivery configuration in `ProductionTreeProgram.TryBootstrap` fails before the host bootstrap
and takes none.

## What changes for existing users

- Nothing unless they opt in. With `RandomStreamId` 0 (the default, and every existing scene) and
  the seed-only overloads, the streams are derived exactly as before.
- With a nonzero id, a run's draws depend only on the seed, the tree, the id and the run index:
  another start order, other hosts, a failed bootstrap or Enter Play Mode without a domain reload no
  longer change them. Two agents with one id and one seed draw the same numbers; a spawner that wants
  different agents gives each its own id.
- A restart draws a new sequence (the next run index), now the same one in every session.
- The three tests that set private counters by reflection use the new overloads and
  `RandomStreamId`.

## Test of reproducibility

Through the public API only (values the tree's random nodes write, read with
`NativeTreeAgent.TryGetValue` when a run completes), under the four scheduling policies the seed
tests cover (Immediate, `JobsAgentFrame`, forced `BatchedJobsSameFrame`, forced `PipelinedJobs`):
agents A (id 11) and B (id 22) run three times each with one seed and are recorded; they are
destroyed, unrelated hosts are bootstrapped with a failed bootstrap between them, and B is started
before A: every (id, run) pair draws the same values. A control agent with id 0 draws other values in
the second session. Further cases: disable and enable repeat run 0; `Restart()`, `Stop()` then
`Restart()` and a change of `Tree` take the next run index; a start the world refuses does not
consume one; two agents with one id and seed draw the same, two with id 0 do not; every run's host
derives its streams from the combined identity. `NativeTreeAgentRandomSeedTests`,
`RandomStreamSeedingTests` (combine vectors, a derived stream row, the overloads),
`HostRandomSeedingTests`; evidence in `Planning~/Evidence/P11-012/README.md`.
