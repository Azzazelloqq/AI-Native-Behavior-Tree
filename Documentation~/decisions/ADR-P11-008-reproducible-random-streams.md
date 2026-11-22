# ADR P11-008: Reproducible random streams for native hosts

- Status: Proposed (2026-11-22). No production code changes with this ADR; it waits for the owner.
- Decision ID: AIBT-052 (reserved; added to `Documentation~/decisions.md` when accepted)
- Task: `Planning~/Tasks/P11/P11-008-review-fixes-batch-2.md` (review finding F13, `Planning~/Evidence/P11-REVIEW/README.md`)
- Related: [ADR P11-002](ADR-P11-002-native-node-library.md) (random nodes on native hosts).

File references are relative to the package root at commit `6d34b8d`.

## Context

A node that declares a random stream (`[AibtRandomStream]`) gets one PCG stream per host, derived
before the first update from four values (`Runtime/State/Native/NativeRandomStreamV1.cs:19-45`):
the root seed, the program's semantic hash, the host's tree instance id and the node's runtime index.
The host passes its own instance id (`GeneratedTreeDispatchAdapterV2.cs:759-764`, `:1499-1506`).

That id is process-wide: `ProductionTreeHost.s_nextTreeInstanceId` (`ProductionTreeHost.cs:43`) is
incremented by every bootstrap, generated or not (`:230`, `:271`), and seeds the streams at `:272-275`.
`NativeTreeAgent` adds a new hidden host for every run (`NativeTreeAgent.cs:307-313`): on enable, on
`Restart` (`:238-239`) and on an automatic restart (`:302-303`), each time with the serialized
`RandomSeed` (`:44`, `:86`) through `ProductionTreeProgram.TryBootstrap(host, randomSeed, ...)`
(`ProductionTreeProgram.cs:33-41`).

So the same seed draws other numbers when anything else bootstraps a host first: another start order,
Enter Play Mode without a domain reload (the counter keeps counting), an Edit-mode host, or a run that
ends at another frame and so shifts every later id. The user guide says so
(`Documentation~/native-node-library.md:224-232`). The tests that pin sequences set the private
counter by reflection (`Tests/Integration/NativeRuntime/NativeTreeAgentRandomSeedTests.cs:158`,
`Tests/Integration/Simulation/HostRandomSeedingTests.cs:21`), so they prove the derivation, not
reproducibility.

`TreeSimulation` already avoids this: its ids are local to the container, 1, 2, ... in the order agents
are added or restarted (`Runtime/Simulation/TreeSimulation.cs:203-211`, used at
`Runtime/Simulation/TreeGroup.cs:506-508`).

The id has other users that need uniqueness, not stability: read and write handles check it
(`ProductionTreeHost.cs:376`, `:502`), traces and MCP name hosts by it.

## Proposal

1. **Stream identity = stable agent id + run index.** The stream of a node is derived from the root
   seed, the semantic hash, a *stream identity* and the node index. The stream identity is computed from
   a stable agent id (a `ulong` the agent owns) and the run index (0 for the first run of that agent,
   +1 for each new host). The host's `InstanceId` stays process-wide and keeps its other uses; it no
   longer enters the stream.
2. **Where the stable id comes from.**
   - `NativeTreeAgent`: a serialized field next to `RandomSeed` (Inspector and a property), set before
     the agent is enabled. The run index is a field of the agent, incremented in `StartHost`.
   - Bare `ProductionTreeHost`: an added `TryBootstrap(..., randomSeed, streamId, out failure)`
     overload (and the same on `ProductionTreeProgram`). The caller owns the run index by passing a new
     id per run; the overloads without it keep today's behaviour (process-wide id), so existing callers
     compile and run unchanged.
   - Hosts driven by `ProductionTreeScheduler`: the streams are seeded at bootstrap, before the host is
     registered (`ProductionTreeWorld.TryRegister`, `Runtime/Integration/ProductionTreeWorld.cs:65-80`),
     and registration order is not stable either, so the scheduler supplies nothing: these hosts take the
     id from whoever bootstraps them (`NativeTreeAgent` or the caller of the overload above).
   - `TreeSimulation` is unchanged.
3. **Derivation.** The stream identity replaces the tree instance id word in the existing mix, so
   `NativeRandomStreamDerivationV1` keeps its layout; the identity must be nonzero (`:27`).

## What changes for existing users

- Every recorded seeded behaviour of `NativeTreeAgent` changes once (sequences of agents with
  random nodes). Trees without random nodes are not affected. `TreeSimulation` is not affected.
- Two agents with the same seed and the same stable id draw the same sequence. Today they never do.
  Spawned prefab instances share their serialized id, so a spawner has to set a distinct id per agent,
  or the default rule below has to make one.
- Sequences no longer depend on start order, domain reload or other hosts. A restart still draws a new
  sequence (new run index), now the same one in every session.
- The two tests that set `s_nextTreeInstanceId` by reflection are replaced by the test below.

## Test of reproducibility

Without touching any private counter: start agents A and B (stable ids 1 and 2, one seed), roll N times
over three runs each, record the streams. Destroy them, bootstrap unrelated hosts to move the
process-wide counter, start B before A, roll again. Each (agent, run) pair must give the same
sequence in both sessions, under the four scheduling policies the current seed tests cover. A second
case: two agents with one id draw the same sequence; with different ids, different ones.

## Open for the owner

1. The default when no stable id is set: refuse to start a random tree, use 0 (all such agents equal),
   or derive one (for example a GUID serialized on `Reset`, which prefab instances share).
2. Whether the run index restarts at 0 when the agent is disabled and enabled again, or only when the
   component is created.
3. Whether the overloads without a stream id keep the process-wide id (compatible, not reproducible) or
   use a fixed identity.
4. Whether `TreeSimulation` should take the same identity (agent handle plus restart count) so that one
   agent draws the same numbers in a container and in a scene.
5. Whether to bump the derivation's name (`AIBT-PCG-XSH-RR32-v1`, `NativeRandomStreamV1.cs:30`) so that
   the change is visible in data, or keep it because the mix itself does not change.
