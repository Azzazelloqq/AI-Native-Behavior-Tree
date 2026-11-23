# ADR P11-013: Agent-scope memory of `NativeTreeAgent`

- Status: Accepted (2026-11-23). Decided by the orchestrator under the owner's delegation of 2026-11-23,
  after an independent read. Implemented by P11-013.
- Decision ID: AIBT-053
- Task: `Planning~/Tasks/P11/P11-013-native-agent-memory.md`
- Related: [ADR P11-004](ADR-P11-004-native-tree-value-read.md) (reading values back; D3 is amended here
  for the agent); P11-009 (mixed Tree and Agent scope in one native tree); normative text:
  `Documentation~/specifications/agent-shared-blackboard-v1.md`, section "Agent context".

File references in Context are relative to the package root at commit `d46fe21`, before P11-013.

## Context

The specification gives the Agent context to the host. It is "a host-created mutable runtime object"
(`agent-shared-blackboard-v1.md:236-247`); creation initializes every slot from its compiled default, with
versions and the context revision at zero (`:249-251`); a tree binds it only on an exact contract id,
version, schema hash and layout hash (`:253-262`); "rebinding, migration, reset, or disposal requires the
context and every bound tree to be quiescent" (`:271-272`); and reset is an explicit transaction
(`:283-292`). The specification does not tie the context's life to one run of one tree.

The standalone adapter keeps Agent slots in the instance's own value buffer: Tree slots first, Agent slots
past `_agentBaseOffset` (`Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs:21`; the
layout and its comment, "a single tree instance is also the implicit sole agent for this standalone host",
`:846-873`; created and filled with defaults at `:91-94`, `InitializeTreeDefaults` `:875-890`). Versions
are per binding slot in `_treeVersions`.

`NativeTreeAgent` builds a new host for every run: `StartHost` (`Runtime/Integration/NativeTreeAgent.cs:346-377`)
on enable, on `Restart` (`:265-271`) and on an automatic restart (`:341-342`); `StopHost` (`:379-388`)
destroys the old one. So every run's Agent values start again from their defaults, and an Agent key
behaves exactly like a Tree key. `MixedScopeNativeTreeTests.NativeTreeAgent_RunsTheMixedTree_InEverySchedulingProfile`
pinned that: every run after the first shows `agent-counter=41`, the default 40 bumped once
(`Tests/Integration/NativeRuntime/MixedScopeNativeTreeTests.cs:167`, `:219`). The user guide prescribes a
workaround: a key that nodes write lives as long as the run, so a tree whose cooldowns or counters must
last across rounds loops inside one run with a repeater at the root (`Documentation~/native-node-library.md:128-138`).

Reading: `NativeTreeAgent.TryGetValue` resolves through the host's public external read, which refuses an
Agent key with `BlackboardUnsupportedScope` (ADR-P11-004 D3, D6); the graph's live view uses the internal
type-erased read with the same rule (`Editor/Graph/GraphLiveDebugger.cs:243`).

A restart can be asked for while the host executes: a `PipelinedJobs` host holds its drive lock between
scheduler frames (ADR-P11-004, fact 3). The world's unregister already handles that:
`ProductionTreeWorld.TryUnregister` calls `ProductionTreeScheduler.TryUnregister` (`Runtime/Integration/ProductionTreeWorld.cs:85-95`),
which drains an in-flight pipelined run first (`Runtime/Scheduling/Production/ProductionTreeScheduler.cs:307-318`,
`DrainPipelinedRunContaining` `:1172-1184`, `DrainPipelinedRunAt` `:1186-1195`). The drain completes the
round on the main thread: `TryDrain` is `TryAdvance` without a next round
(`Runtime/Scheduling/Production/ProductionPipelinedGroupDriverV1.cs:114`), which completes the job handle
(`Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs:218`, `_dependency.Complete()`), releases the drive lock of every
member (terminal ones at `:152`, the rest through `ReleaseAll` at `:171` and `:271-275`, failed ones in
`FailAll` `:261-269`) and disposes the controller. Every other policy runs and releases a host inside
the scheduler's own frame. Teardown (`ProductionTreeHost.Stop` → `DisposeHost` → `RunTeardown`,
`Runtime/Integration/ProductionTreeHost.cs:943-958`) aborts active nodes, but an abort context has no blackboard access
(`Runtime/Nodes/Contracts/BurstNodeContracts.cs:514-537`), so values read before `Stop` are the run's
final values.

## Decision

Decided by the orchestrator under the owner's delegation of 2026-11-23, after an independent read.

1. **Agent-scope keys belong to the component, not to a run.** They start at their defaults when the
   component is enabled and keep their values across automatic restarts, `Restart()` and `Stop()`
   followed by `Restart()`. They return to their defaults when the component is disabled, after a run
   whose host faulted (its values may be mid-update), when `Tree` is set to an asset whose Agent contract
   differs (contract id, version, schema hash or layout hash), and on a new `Restart(bool resetAgentMemory)`
   overload called with true. `Restart()` keeps its signature (it is `Restart(false)`). Tree-scope keys
   still start from their defaults in every run.
2. **Mechanism.** When a run ends, the component copies the Agent byte range (past the Tree range) and
   the Agent slots' versions (by scope-local slot index) out of the idle host; the next host receives
   them after its bootstrap and before its first update (before it joins the world), on the main thread,
   through internal adapter and host methods. Bytes are relative to the Agent range, so a tree with other
   Tree keys and the same Agent contract matches. No compiled-format, ABI or generator change.
3. **A restart asked for while the host executes** does not read a buffer that jobs may write. The copy
   happens after the agent has left its world: that unregister is the point where the host becomes idle,
   because the scheduler completes the in-flight pipelined round on the main thread and releases the drive
   lock (Context). The host is read only through a gate that refuses while it executes
   (`CanReadTreeValues`); if that gate ever refused, the memory would be reset with a warning instead of
   read. No frame-level deferral is needed, and the restart stays synchronous.
4. **Reading.** `NativeTreeAgent.TryGetValue<T>` and the live values of the AIBT Graph also read
   Agent-scope keys. This amends ADR-P11-004 D3 for the agent. The public host read
   (`ProductionTreeHost.TryResolveExternalTreeRead`) stays Tree-only; Shared keys stay unreadable (no
   storage).
5. **Not changed, but documented:** `TreeSimulation` (its reset makes a fresh agent with fresh Agent
   values), `SetFact` (Tree keys only) and library nodes, which bind Tree keys only (AIBT3018 for an Agent
   key), so Agent memory is used from project nodes. The user guide gets one table of what survives a
   restart in `BehaviorTreeAgent`, in `NativeTreeAgent` for Tree keys and for Agent keys, and in
   `TreeSimulation`.

## Amendment 2026-11-24 (P11-014)

What the decision left unstated, from the independent review of P11-013:

- **A start that fails.** A start whose bootstrap fails leaves the memory untouched: the next start
  restores the values the last run left. A start refused after the bootstrap (no world, or the world
  refuses the registration) has already restored them into its host; stopping that host captures them
  again, unchanged. Neither consumes a run index (ADR-P11-008). Pinned by
  `MixedScopeNativeTreeTests.ABootstrapFailure_ConsumesNoRunIndex_AndLeavesTheAgentMemory`.
- **A host stopped by user code.** `agent.Host.Stop()` called by the game, then `Restart()`: the stopped
  host refuses to be read (`CanReadTreeValues` is false), so the capture resets the memory and the next
  run starts from the Agent defaults. It is one more reset cause next to the four of decision 1.
- **Handle-like values.** The copy is bytes. An Agent key holding a handle into one host's runtime (an
  `OperationId`, for example) is copied into a host whose operations are other ones, where it names
  nothing or something else; keep such values in Tree keys or node memory.

## What changes for existing users

- A tree with Agent-scope keys on a `NativeTreeAgent` now keeps those values from run to run. A tree that
  relied on them being reset every run (nothing in the package or its samples does; library nodes cannot
  bind them) calls `Restart(true)`, or moves the key to Tree scope.
- Trees without Agent keys behave exactly as before; the copy is skipped.
- `TryGetValue` reads Agent keys instead of logging "cannot be read: BlackboardUnsupportedScope".
- The graph shows live values of Agent keys of a native agent.

## Tests

Through the public API (`TryGetValue`, `Restart`, `Stop`, `Tree`, enable and disable), in the seven
scheduling kinds the mixed-scope tests cover (Interactive, Background, forced Immediate, Budgeted,
`BatchedJobsSameFrame`, `PipelinedJobs`, `JobsAgentFrame`): the mixed-scope expectation now grows by one
per automatic restart; `Restart()`, `Stop` then `Restart`, a tree with the same Agent contract and other
Tree keys (another Agent offset) keep the values; `Restart(true)`, disable and enable, a tree with
another contract version, and the way back reset them; a faulted run resets them; a restart while a
`PipelinedJobs` host executes carries what the completed round left. A warmed update of a looping tree
that bumps an Agent key, with an Agent-key read every frame, allocates nothing (GC.Alloc recorder and
`AllocatingGCMemory`). The graph shows an Agent key and keeps it across a restart.
`MixedScopeNativeTreeTests`, `GraphNativeLiveBlackboardTests`; evidence in
`Planning~/Evidence/P11-013/README.md`.
