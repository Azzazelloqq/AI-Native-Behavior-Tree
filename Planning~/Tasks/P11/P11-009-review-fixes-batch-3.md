# P11-009 — Review fixes, batch 3 (F9, F10 native half, F8 write side, F6)

Status: `In progress`. The fixes are committed on `feature/aibt-consumer-workflow`; the orchestrator's
acceptance run (the ten assemblies) is not part of this item.

## Objective

Fix the third batch of findings of the P11 production-readiness review
(`Planning~/Evidence/P11-REVIEW/README.md`), the two stopped in P11-008 and two follow-ups:

- F9: a native tree with Tree-scope keys plus Agent- or Shared-scope keys must compile and run; it ended
  in AIBT3017 "Tree-scope blackboard memory ranges overlap". The bytes and content hash of every program
  that compiles today stay the same.
- F10, native half: a Warning with its own code (AIBT2054) where a native tree is validated or compiled,
  naming the node, saying native hosts do not re-evaluate observers and what to use instead. The tree
  still compiles to the same bytes; every existing test stays green.
- F8 twin (write side): `ProductionTreeHost.TryResolveExternalTreeWrite<T>` and the adapter's write check
  that `T` is the managed type of the declared built-in type (`BuiltInManagedType<T>`); signatures kept,
  the write stays allocation-free; every caller in the package and the samples listed.
- F6: MCP and the Editor resolve subtrees against one file set, with the Editor's rule
  (`ProjectTreeIndex`) as the reference; first a probe that shows a layout where they disagree.

For each finding: a test that fails before the fix, the fix, one commit. Stop and report instead of
fixing when a fix needs a format change, a new ABI member, changed bytes of an existing program, a caller
that would be refused, or would make MCP lose trees it finds in a normal project.

## Depends on

- `P11-008` (batch 2).

## Scope

- F9: `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs` (the legacy semantic projection);
  a fixture node assembly `Tests/Editor/CodeGen/GenerationP11009/`; `Tests/Integration/NativeRuntime/MixedScopeNativeTreeTests.cs`;
  the reference in `Tests/Integration/AIBT.Integration.Tests.asmdef`.
- F10: a new code in `Authoring/Validation/TreeValidationDiagnostics.cs`; `Editor/Registry/ProjectNodeCatalog.cs`,
  `Editor/Graph/TreeCatalogEntry.cs`, `Editor/Graph/ProductionTreeAssetBaker.cs`,
  `MCP/Verification/McpVerificationToolDispatcher.cs`; `Tests/Editor/Validation/NativeObserverWarningTests.cs`;
  the generated API reference; README, `native-node-library.md` and the implementation-status note of
  `execution-semantics-v1.md` (the code only, no rule).
- F8: `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs`, `Runtime/Integration/ProductionTreeHost.cs`
  (body and doc comment of the write resolve), `Tests/Integration/NativeRuntime/ExternalTreeBlackboardWriteTests.cs`,
  `Documentation~/production-host.md`.
- F6: `MCP/AibtTreeDiscovery.cs`, `MCP/McpSubtrees.cs`, `MCP/McpRuntimeTools.cs`,
  `MCP/Authoring/McpAuthoringToolDispatcher.Subtrees.cs`, `Editor/Graph/ProjectTreeIndex.cs`,
  `Tests/Editor/Mcp/Authoring/McpSubtreeToolsTests.cs`.
- `CHANGELOG.md`, `Planning~/work-items.json`, `Planning~/MASTER_PLAN.md`, `Planning~/Evidence/P11-009/`.

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format, the bytes or
  content hash of any program that compiles today, execution semantics, `package.json`, existing public and
  low-level APIs (only additions), normative specifications beyond the AIBT2054 code in the note, files
  under `Tests/Fixtures`, behaviour of `ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`
  beyond the write-side type check.

## Acceptance criteria

- Each finding fixed or stopped with a reason, in its own commit with a test that failed before.
- The tests run listed with counts; the Console free of new errors; the submodule checkout clean.
- `Planning~/Evidence/P11-009/README.md` with commits, before/after per finding with raw file names, the
  tests run and what was not verified.
- `Tools~/Verification/Verify-Static.ps1` passes.
