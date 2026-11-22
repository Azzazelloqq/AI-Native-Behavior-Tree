# P11-007 — Review fixes, batch 1 (F1, F2, F3, F4, F5, F7, F12, W5)

Status: `In Progress` — the fixes are committed on `feature/aibt-consumer-workflow`; the item awaits
the orchestrator's acceptance.

## Objective

Fix the first batch of findings of the P11 production-readiness review
(`Planning~/Evidence/P11-REVIEW/README.md`): F1 (one key on two handles of a native node), F2 (format 3
without subtree features refused by the native compiler), F3 (inspector staleness cost per repaint),
F4 (one project scan per subtree-using tree in a tree-list refresh), F5 (`AssetDatabase.Refresh()` after
every recompile), F7 (MCP does not see `NativeTreeAgent` hosts), F12 (three stale tests) and W5
(`NativeTreeAgent.TryGetValue` true for a faulted run until `LateUpdate`).

For each finding: a test that fails on the code before the fix (a measurement for performance), the
smallest sound fix, the test passing, one commit with fix and test.

## Depends on

- `P11-006` (the review).

## Scope

- F1: `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs` (allowed by the owner:
  a configuration that faulted starts to work).
- F2: `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs` (allowed by the owner: the native
  compiler's format gate for a format-3 document without subtree features).
- F3, F4, F5: `Editor/Graph/` (`ProductionTreeAssetStaleness.cs`, both inspectors, `TreeCatalogEntry.cs`,
  `BehaviorTreeGraphWindow.Chrome.cs`, `ProjectTreeIndex.cs`).
- F7: `MCP/McpRuntimeTools.cs` (additions to output only), the sentences in
  `Documentation~/ai-and-mcp.md`, `MCP/Documentation/McpRecipesDocumentGenerator.cs` and
  `Documentation~/generated/recipes.md` that said the opposite.
- W5: `Runtime/Integration/NativeTreeAgent.cs` (allowed by the owner).
- F12: two test files only.
- Tests under `Tests/Integration/NativeRuntime/`, `Tests/Editor/Consumer/`, `Tests/Editor/Mcp/`;
  `CHANGELOG.md`; `Documentation~/subtrees.md`.

## Forbidden changes

- `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`, the compiled program format, `CodeGen~`,
  execution semantics, `Documentation~/specifications/`, `package.json`, existing public APIs (only
  additions), and other behaviour of `ProductionTreeHost`, `NativeTreeAgent`, `ProductionTreeScheduler`.

## Acceptance criteria

- Each of the eight findings fixed or stopped with a reason, in its own commit with its test.
- The affected tests green, the Console free of new errors, both checkouts clean.
- `Planning~/Evidence/P11-007/README.md` with the proof per finding.
- `Tools~/Verification/Verify-Static.ps1` passes.

## Evidence

`Planning~/Evidence/P11-007/README.md`; raw files in `C:\UnityProjects\_aibt_acceptance\P11-007\`.
