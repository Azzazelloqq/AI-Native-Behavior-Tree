# P11-013 — Agent-scope memory of `NativeTreeAgent` (ADR-P11-013, AIBT-053)

Status: `In progress` — committed on `feature/aibt-consumer-workflow`, waiting for the orchestrator's
acceptance run.

## Objective

Make the Agent-scope keys of a `NativeTreeAgent` belong to the component instead of to one run, with the
decisions the orchestrator took on 2026-11-23 under the owner's delegation, after an independent read:

1. Agent keys start at their defaults when the component is enabled and keep their values across
   automatic restarts, `Restart()` and `Stop()` followed by `Restart()`. They return to their defaults on
   disable, after a run that faulted, when `Tree` is set to an asset with another Agent contract (id,
   version, schema or layout) and on a new `Restart(bool resetAgentMemory)` called with true. `Restart()`
   keeps its signature. Tree keys still start from their defaults in every run.
2. Mechanism: the component copies the Agent byte range and the slot versions out of the finished host
   and into the new host after its bootstrap and before its first update, on the main thread, through
   internal adapter/host methods. No compiled-format, ABI or generator change.
3. A restart asked for while the host executes (a manual `Restart` under `PipelinedJobs`) must not read
   a buffer that jobs may write: the copy waits for the point where the host is idle; stop and report if
   that cannot be made simple and provably safe.
4. `NativeTreeAgent.TryGetValue<T>` and the AIBT Graph's live values also read Agent keys (amends
   ADR-P11-004 D3 for the agent). The public host read stays Tree-only.
5. Not changed, documented: `TreeSimulation`, `SetFact` (Tree keys only), library nodes (Tree keys only).
   One table in the user guide: what survives a restart in `BehaviorTreeAgent`, in `NativeTreeAgent` for
   Tree and for Agent keys, and in `TreeSimulation`.

First verify and cite: the specification's Agent context (`agent-shared-blackboard-v1.md`), the adapter's
Agent range past `_agentBaseOffset`, the per-run host and the mixed-scope test that pinned the reset, the
user guide's workaround.

## Depends on

- `P11-009` (native trees mix Tree and Agent keys).
- `P11-012` (the agent's run lifecycle as it stands, run index).

## Scope

- `Runtime/Integration/NativeAgentScopeMemory.cs` (new, internal), `NativeTreeAgent.cs`,
  `ProductionTreeHost.cs` (internal capture/restore and Agent-aware reads),
  `GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs` (internal copy and reads).
- `Editor/Graph/GraphLiveDebugger.cs`.
- Tests: `Tests/Integration/NativeRuntime/MixedScopeNativeTreeTests*.cs`,
  `Tests/Editor/Graph/GraphNativeLiveBlackboardTests.cs`.
- `Documentation~/decisions/ADR-P11-013-*.md` (new), `ADR-P11-004-*.md` (amendment), `decisions.md`,
  `consumer-quickstart.md`, `native-node-library.md`, `production-host.md`,
  `generated/api-reference-runtime.md` (through its generator), `CHANGELOG.md`, `Planning~/`.

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format, `CodeGen~`,
  `package.json`, pins and fixtures (including the P11-009 fixture nodes), existing public signatures
  (only additions), `TreeSimulation`, `SetFact`.

## Acceptance criteria

- Tests first (failing before), through the public API: the mixed-scope expectation grows over runs;
  automatic restart, `Restart()`, `Restart(true)`, `Stop` then `Restart`, disable/enable, a faulted run, a
  tree swap with the same and with another Agent contract, a manual restart during execution, in every
  scheduling kind the mixed-scope tests cover; a warmed update allocates nothing (GC.Alloc recorder or
  `AllocatingGCMemory`); `TryGetValue` and the graph formatter on an Agent key.
- One commit per logical change; the touched tests green with counts; the Console free of new errors;
  the submodule checkout clean; `Tools~/Verification/Verify-Static.ps1` passes with 192 work items.
- `Planning~/Evidence/P11-013/README.md` with raw files in `C:\UnityProjects\_aibt_acceptance\P11-013\`.

## Evidence

`Planning~/Evidence/P11-013/README.md`.
