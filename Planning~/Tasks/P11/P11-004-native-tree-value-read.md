# P11-004 — Read a Tree-scope value back from a running native tree

Status: `Review` — implemented on `feature/aibt-consumer-workflow` after `0038a3b` and accepted by the
orchestrator on `8d03467` (full run of all nine assemblies on `1eb2359`, then the review fix `ac4201c`).

## Objective

Game code can ask a running native tree "what is the value of key X now" (UI, animation, debugging):
the mirror of the external write of ADR-P7-039. On `ProductionTreeHost` a resolve-once handle and an
allocation-free per-frame read; on `NativeTreeAgent` a by-name `TryGetValue<T>` that survives the
new host of every run; an internal type-erased read that the next item uses for the graph's live
values of a native agent.

## Depends on

- `P7-039` (external Tree-scope write), `P9-004` (`NativeTreeAgent`).

## Decision

- `Documentation~/decisions/ADR-P11-004-native-tree-value-read.md` (`AIBT-051`), proposed `2ae7e06`,
  accepted 2026-11-21 by the orchestrator under the owner's approval of the task (2026-11-20) with
  three changes (`0ca665c`); open to the owner's revision.

## Scope

- `Runtime/Integration/ProductionTreeHost.cs`, `Runtime/Integration/NativeTreeAgent.cs`,
  `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs`: additions only.
- Tests under `Tests/Integration/NativeRuntime/` and one case in `Tests/Editor/Documentation/UserGuideSnippetTests.cs`.
- `Documentation~/consumer-quickstart.md`, `Documentation~/production-host.md`, `CHANGELOG.md`, the
  regenerated `Documentation~/generated/api-reference-runtime.md`, `Documentation~/decisions.md`.

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format, `CodeGen~`,
  execution semantics, `Documentation~/specifications/`, `package.json`, existing low-level APIs, and
  the behaviour of existing members of `ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`.
- `Editor/Graph/` (the graph's use of the read is the next item).

## Acceptance criteria

- Host: `TryResolveExternalTreeRead<T>` / `TryReadExternalTreeValue<T>` / `CanReadTreeValues` as ADR D1,
  D5, D6; zero managed allocation per read, measured.
- Agent: `TryGetValue<T>` / `TryGetVector3` as ADR D2 and D4, across runs; one warning per agent for a
  read refused while the host executes; misuse logged once per key and type.
- Internal `ProductionTreeHost.TryReadTreeValue(string, out BlackboardValue, out NativeRuntimeFailureV1)` (D7).
- Live check in Play Mode with a node-written and a fact key, raw log kept.
- No existing test expectation changes (the regenerated API reference is the expected exception).

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Filter ExternalTreeBlackboardReadTests,NativeTreeAgentValueReadTests,ExternalTreeBlackboardWriteTests,NativeTreeAgentTests,NativeTreeAgentRandomSeedTests,ProductionTreeWorldEffectTests,UserGuideSnippetTests,McpDocumentationGeneratorsTests
Tools/Unity/Save-UnityClip.ps1 -Scene Assets/AibtNativeGuardDemo/NativeGuard.unity -Play -Source window -Window Game
Tools~/Verification/Verify-Static.ps1
```

## Handoff notes

- Evidence: `Planning~/Evidence/P11-004/README.md`; raw test results, the live log, the clips and the
  probe source in `C:\UnityProjects\_aibt_acceptance\P11-004\`.
- The full run of all test assemblies was not made here; the orchestrator makes it.
