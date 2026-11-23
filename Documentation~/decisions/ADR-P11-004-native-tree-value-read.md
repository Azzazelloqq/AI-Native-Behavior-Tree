# ADR P11-004: Reading a Tree-scope value back from a running native tree

- Status: Accepted (2026-11-21). Proposed 2026-11-20; accepted by the orchestrator on 2026-11-21 under the
  owner's approval of the task (2026-11-20), with the changes listed under "Decided"; open to the owner's revision.
- Decision ID: AIBT-051
- Task: `Planning~/Tasks/P11/P11-004-*.md` (written in phase B)
- Mirror of: [ADR P7-039](ADR-P7-039-external-tree-blackboard-write.md) (external Tree-scope write).
- Amended by: [ADR P11-013](ADR-P11-013-native-agent-memory.md) (AIBT-053, 2026-11-23): D3 for
  `NativeTreeAgent` and the graph, which also read Agent-scope keys.

File references are relative to the package root at commit `0038a3b`.

## Context

Game code can write a Tree-scope key of a running native tree (`ProductionTreeHost.TryResolveExternalTreeWrite`
/ `TryWriteExternalTreeValue`, `Runtime/Integration/ProductionTreeHost.cs:327-399`; `NativeTreeAgent.SetFact`,
`Runtime/Integration/NativeTreeAgent.cs:130-146`) but cannot read one. UI, animation and debugging have to
mirror the tree's state by hand. The editor graph shows `live: —` for every key of a native agent because
`GraphLiveDebugger.TryFormatValue` (`Editor/Graph/GraphLiveDebugger.cs:202-209`) reads only a managed instance.

Facts the design rests on (checked in code, not docs):

1. Storage. A generated-catalog host keeps Tree-scope values in `GeneratedTreeDispatchAdapterV2._treeValues`
   (`NativeArray<byte>`, canonical bytes at `slot.Offset`) and `_treeVersions` (`GeneratedTreeDispatchAdapterV2.cs:22-23`).
   Agent-scope values sit in the same array past `_agentBaseOffset` (`:640-667`). Shared scope is not stored.
   A delegate-bootstrapped host has no blackboard storage at all (ADR-P7-039 context).
2. Defaults. `_treeValues` is filled with compiled defaults at creation (`:91-94`, `InitializeTreeDefaults` `:670-687`).
3. Safety gate. `ProductionTreeHost._driving` (`ProductionTreeHost.cs:48`) is held for every drive/round entry
   point, across a `JobsAgentFrame` job (`BeginAgentFrame`/`FinishAgentFrame`, `:663-715`) and, for a
   `PipelinedJobs` host, between scheduler frames (`ProductionTreeWorld.cs:141-146`). Outside it no code and no
   job touches `_treeValues`. Writes use exactly this gate (`CanAcceptExternalTreeWrites`, `:133-134`).
4. Lifetime. A fault keeps storage (`ProductionTreeAgentCore.Fail` only clears `_ready`, `:645-658`);
   `Stop` disposes it (`ProductionTreeHost.cs:778-803`). A finished root keeps it until `Stop`.
5. Agent runs. `NativeTreeAgent` makes a new host per run (`StartHost`, `NativeTreeAgent.cs:240-267`). With
   `RestartMode.Always` the old host is stopped and the new one started in the agent's `LateUpdate`, right
   after `Completed` is raised (`ProcessRootResult`, `:213-238`). Facts reach the new host at the next scheduler frame's
   `FlushIdleInputs` (`ProductionTreeScheduler.cs:405-408`), before its first update.
6. Protected surface. `ExpectedPublicAbiV2.txt` pins only public types of namespace `AIBT.Burst`
   (`PublicBurstAbiContractTests.cs:113`; 53 types). `ProductionTreeHost` and `NativeTreeAgent` are in `AIBT`.
   `Documentation~/generated/api-reference-runtime.md` lists public members and is pinned by
   `McpDocumentationGeneratorsTests.cs:234`; it is regenerated, it is not a protected file.

Stop condition checked: the read needs no change to the ABI file, the compiled program format, `CodeGen~`,
execution semantics or any existing member. It only adds members.

## Decision

### D1. Host API (public, mirror of the write pair)

```csharp
// nested in ProductionTreeHost, same shape as ExternalTreeWriteHandle<T>: private fields, internal ctor
public readonly struct ExternalTreeReadHandle<T> where T : unmanaged { }
public bool CanReadTreeValues { get; }
public bool TryResolveExternalTreeRead<T>(string stableKey, NativeBlackboardTypeIdV2 expectedType,
    out ExternalTreeReadHandle<T> handle, out NativeRuntimeFailureV1 failure) where T : unmanaged;
public bool TryReadExternalTreeValue<T>(ExternalTreeReadHandle<T> handle,
    out T value, out NativeRuntimeFailureV1 failure) where T : unmanaged;
```

Resolve once (FNV-1a of the key, linear scan of the binding slots, the type checks of the write resolve, plus
`sizeof(T)`/alignment equal to the slot), then read every frame by handle: a few compares and one
`_treeValues.ReinterpretLoad<T>(offset)`. No `Allocator.Temp` buffer is needed, unlike the write. The handle
carries the host's `InstanceId` and is refused on any other host. **No public by-name read on the host**: the
host is the explicit low-level layer (the write has none either); the agent is the by-name layer.

### D2. Agent API (public)

```csharp
public bool TryGetValue<T>(string key, out T value) where T : unmanaged;   // the SetFact types
public bool TryGetVector3(string key, out Vector3 value);                  // Float3 key as a Unity vector
```

Supported `T`: exactly the `SetFact` set (bool, int, long, float, double, `Float2Value`, `Float3Value`,
`QuaternionValue`). `TryGetValue` throws only for a null or empty key (`ArgumentException`) and an unsupported
`T` (`NotSupportedException`). Reading one key as a second `T` is a type mismatch (one of the two types must differ
from the key's), so it is logged once and returns false, whatever the call order. The Vector3 form is named `TryGetVector3`, not a
`TryGetValue` overload: with `out var` the compiler would silently pick the `Vector3` overload. The agent keeps a
lazily created `Dictionary<string, ReadSlot>`; a slot holds the handle and the host it was resolved for, and
re-resolves when `Host` is a different object. So a new run re-resolves once, on the first read; the caller sees
nothing. `StartHost`/`StopHost` are not changed. A key that does not exist, has another type or another scope is
logged once per key and program (`Debug.LogError`, as a wrong fact is) and the read returns false. A read refused
because the host is executing (in practice a `PipelinedJobs` host between frames) logs one warning per agent
(not per key or frame) naming the cause and the profile, then keeps returning false. "No run" (stopped, faulted,
disabled) is documented behaviour and logs nothing.

### D3. Which keys

Every Tree-scope key with a built-in type: written by nodes, set as a fact, or written by nobody. Agent and
Shared scope are out: Shared has no storage in the host, and Agent scope on a host is the disclosed
"sole agent" approximation (`GeneratedTreeDispatchAdapterV2.cs:640-647`) that the write path also excludes, and no
native library node uses it. Registered (non-built-in) types are out, as for the write: their identity check needs
the registered-type tables the adapter does not hold. Each refusal returns a code (D6), never a wrong value.

*Amended by ADR-P11-013 (AIBT-053), decided by the orchestrator under the owner's delegation of 2026-11-23:*
`NativeTreeAgent.TryGetValue` and the graph's live view of a native agent also read Agent-scope keys with a
built-in type. Agent keys now belong to the agent and keep their values across its runs, so they are worth
reading. The public host read (`ProductionTreeHost.TryResolveExternalTreeRead`) stays Tree-only, the write path
(`SetFact`) stays Tree-only, and Shared scope stays out.

### D4. Lifetime

Host rule: **a host is readable from bootstrap until `Stop`, also after its root finished or it faulted, except
while it is executing.** Before the first update a read returns the compiled default (or a fact already flushed).
After a fault the values are whatever the storage held when the fault happened and may be mid-update.

Agent rule (one sentence for users): **a read shows the current run's tree: its defaults before the first update,
its live values while it runs, its final values until the next run starts, and false when the agent has no run.**
Consequences, documented: with `RestartMode.Always` the final values of a run are readable only inside
`Completed`; after `Stop()`, a fault, or while disabled `TryGetValue` returns false; a fresh run reads node-written
keys at their defaults. A fact key reads its default between an automatic restart (agent `LateUpdate`) and the
next scheduler frame's flush (Q1).

### D5. When a read is legal

Main thread only, as every `ProductionTreeHost` member (not enforced, same as the write). Legal exactly when
`CanReadTreeValues` is true: bootstrapped through the generated catalog, not disposed, not destroying, and not
`_driving`. Outside that the read touches no memory and returns `NativeLifetimeStateInvalid`. In practice: legal in
any `Update`/`LateUpdate`, in effect handlers and in `Completed` (all run while the host is idle); refused inside
the host's own round and while a `PipelinedJobs` job is in flight between frames (see Q2). Unlike the write,
`_ready` is not required, so finished and faulted hosts stay readable.

### D6. Failures (existing codes only)

| Case | Code |
| --- | --- |
| null or empty key | `ArgumentException` (as the write) |
| not bootstrapped, stopped, executing, default handle, handle of another host | `NativeLifetimeStateInvalid` |
| host bootstrapped through a delegate overload | `NativeCapacityPlanInvalid` (as the write) |
| unknown key | `BlackboardUndeclaredAccess` (as the write) |
| key exists in Agent or Shared scope | `BlackboardUnsupportedScope` (existing code, first use here) |
| type identity, registered type, or `sizeof(T)`/alignment differ | `BlackboardTypeMismatch` (as the write) |

Resolve is legal while the host executes (it reads only the managed binding), as the write resolve is.

### D7. Type-erased read for the editor (internal; the next task uses it)

```csharp
internal bool TryReadTreeValue(string stableKey, out BlackboardValue value,
    out NativeRuntimeFailureV1 failure);  // on ProductionTreeHost
```

By name on every call (editor-only, allocations allowed). Same gate and codes as D5/D6. The slot's built-in
descriptor is found by `TypeId` among `BuiltInBlackboardTypes`, then the bytes are decoded with the existing
`BlackboardBuiltInCodec.TryDecode`. `GraphLiveDebugger` would call `NativeAgent.Host.TryReadTreeValue(key, …)` and
keep the last text when it returns false. The graph is not touched in this task.

### D8. Cost

Per-frame host read: zero managed allocation, no string work. Agent read: one `Dictionary<string,…>` lookup,
zero allocation after the first read of a key; one re-resolve per run. Agents and hosts that never read pay
nothing: no field is touched on the drive path, the agent's dictionary is created on first read.

### D9. What does not change

`ExpectedPublicAbiV2.txt`, the compiled program format, everything under `CodeGen~`, execution semantics,
specifications, the package version, and every existing member of `ProductionTreeHost`, `NativeTreeAgent`,
`ProductionTreeScheduler` and `GeneratedTreeDispatchAdapterV2`. Additions: one handle type, one property, two
public and one internal method on the host; two internal methods on the adapter; two public methods and a private
read-slot class on the agent. `api-reference-runtime.md` is regenerated for the new public members.

## Test plan

`AIBT.Integration.Tests` (EditMode), new files next to `ExternalTreeBlackboardWriteTests.cs`:

- Host: default before the first update; node-written key after an update; key written externally; key nobody
  writes; unknown key; type mismatch; `sizeof(T)` mismatch; Agent-scope key (if a fixture can declare one);
  delegate host; handle of another host; default handle; after `Stop`; after a finished root; after a fault;
  refusal mid-round (held drive, as the write test does); zero allocations over 1000 reads after warm-up
  (`GcAllocIs.Not.AllocatingGCMemory()`, as the write test); `TryReadTreeValue` for bool/int/long/float/Float3
  and its failures.
- Agent: fact key round trip; node-written key; value inside `Completed`; new run reads defaults
  (`RestartMode.Always`); `Never` keeps the final value; `Stop` and fault return false; `TryGetVector3`; unsupported
  `T` throws; second `T` returns false and logs once; unknown key logged once; refusal while executing logs one
  warning per agent; zero allocations of `TryGetValue` after warm-up.
- Host, pipelined: a `PipelinedJobs` host between frames is refused, if an existing fixture makes it cheap.
- Docs: a `UserGuideSnippetTests` case pinning the new snippet to a compiled file; the regenerated API reference.

Live check: the Native Guard scratch scene in Play Mode; a scratch script reads `weapon-ready-at` (Int64, written
by `aibt.native.set-deadline`) and `intruder-seen` (Bool fact) every frame, logs them to
`C:\UnityProjects\_aibt_acceptance\P11-004\` and shows them with `OnGUI`; a screenshot or a short clip.

## Decided

Taken as recommended by the orchestrator on 2026-11-21 under the owner's approval of the task (2026-11-20),
open to the owner's revision. None needs a protected change.

- Q1. No fact flush at `StartHost`: a fact key reads its default between an automatic restart and the next
  scheduler frame. The user guide says so in plain words.
- Q2. `PipelinedJobs` hosts are refused for most of a frame because they hold the drive lock across frames;
  accepted and documented in the user guide. A "last committed copy" would be a separate task.
- Q3. Agent scope stays out (D3); the storage exists, so adding it later is small. (Added for the agent and the
  graph by ADR-P11-013.)
- Q4. Agent misuse is logged once and returns false (D2); no `out` failure overload. The host API gives codes.

Orchestrator changes folded in on acceptance: a second `T` for one key is a type mismatch, not an exception (D2);
one warning per agent for a read refused while the host executes (D2); fault values may be mid-update (D4).
