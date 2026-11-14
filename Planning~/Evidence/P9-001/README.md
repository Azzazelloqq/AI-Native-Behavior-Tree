# P9-001 — Managed agent layer

Status: Review, verified in the `Modules` host project on Unity 6000.7.0b1, 2026-11-14. Local
commit only, no push.

## Result

A consumer adds `BehaviorTreeAgent` to a GameObject, assigns an `.aibt` tree (or `.aibt.json`
text) and writes leaves as ordinary C# classes (`BtAction`, `BtCondition`, `[BtNode]`). No
generated catalog, runtime asset or host wiring is needed. Implemented in two steps:

1. Stage 1 (`0f4b4f6`): assembly `Agents/` (`AIBT.Agents`): `BehaviorTreeAgent`,
   `BehaviorTreeSource`, `BtAction`/`BtCondition`, `BbKey<T>`, `AgentBlackboard`,
   `ManagedTreeInstance`, `ManagedNodeCatalog`; managed-only parameter types `int32`, `string`,
   `blackboard-key` (`ADR-P9-001`, schema `node-manifest.schema.json`); idle-boundary external
   blackboard read/write and `TryRequestReevaluation` on the reference machine; 14 `aibt.unity.*`
   nodes; `Samples~/SimpleAgent`; managed nodes added to the editor's `ProjectNodeCatalog`.
2. Stage 1 tail (this commit):
   - MCP test expectations updated for package-provided managed nodes (see below).
   - New focused tests: `ManagedParameterValidationTests` (17: `int32` range and type, `string`
     type, `blackboard-key` empty/non-string/undeclared/wrong type/non-Tree scope/missing),
     `ReferenceReevaluationRequestTests` (5: accepted request; refused before the first update,
     after completion, off the active path, for a reactive composite and for a leaf; a request
     under a reactive composite defers to the normative reactive reset and leaves the update
     identical to one without it), `BehaviorTreeAgentComponentTests` (5, Edit Mode, manual `Tick`:
     restart on complete, no restart when disabled, `Restart` and `Stop` called from inside a
     node, a node exception raises `Faulted` exactly once), one Preview window test.
   - Test seams: `BehaviorTreeSource.FromJson(json, name, ManagedNodeSet)` (internal) compiles
     against an explicit node set, because discovery deliberately skips node classes in test
     assemblies; `ManagedTreeInstance.Machine` (internal) exposes the reference machine.
   - Reference Preview (`Editor/Preview/BehaviorTreePreviewWindow.cs`): a tree containing a
     `BtAction`/`BtCondition` node is not compiled for preview; the window shows
     "This tree uses managed C# nodes (BtAction/BtCondition), which run only in a
     BehaviorTreeAgent. Preview cannot step it: add the tree to a GameObject's Behavior Tree Agent
     and enter Play Mode." and draws the graph with the project catalog so the nodes resolve.
     Main-thread project leaf extensions with their own reference behavior still preview as
     before. Before this change such a tree did not reach `HostOnlyLeafBehavior`: the preview
     compiles against built-ins, test fixtures and project leaf providers only, so it failed with
     an unknown-node-type compilation error.

### Why the Preview does not run managed trees

The Preview window drives `ReferencePreviewDriver` (in `AIBT.Authoring`), which builds its own
registry and leaf behaviors per `IReferenceLeafBehaviorProvider`, and exposes atomic stepping,
breakpoints and inspection over that machine. A managed node needs a `ManagedTreeInstance` to
create the node object, bind authored parameters and `BbKey<T>` slots and supply the agent;
`AIBT.Authoring` cannot reference `AIBT.Agents` (the dependency runs the other way). Running
managed trees in the Preview means a second driver over `ManagedTreeInstance` with the same
stepping/inspection surface; that belongs to the graph-editor stage (P9-003, live debugging), not
to this card.

## Verification

Environment: Windows 11, Unity 6000.7.0b1, `Modules` host project, Unity CLI
(`com.unity.pipeline` 0.7.0-exp.1). Commands run from `C:\UnityProjects\Modules`.

```
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityRecompile.ps1
  -> {"errors":[],"status":"up_to_date","compilationFailed":false,"failed":false}
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityTests.ps1 -Assemblies AIBT.Editor.Tests
  -> AIBT.Editor.Tests 549/551 (failed=2 skipped=0)
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity/Invoke-UnityTests.ps1 -Assemblies AIBT.Runtime.Tests,AIBT.Integration.Tests,AIBT.BehaviorCases.Tests
  -> AIBT.Runtime.Tests 707/707, AIBT.Integration.Tests 58/58, AIBT.BehaviorCases.Tests 65/65
powershell -NoProfile -ExecutionPolicy Bypass -File Assets/AIBT/Tools~/Verification/Verify-Static.ps1
  -> AIBT JSON Schema verification passed. Schemas: 7.
     AIBT static verification passed. Work items: 164.
```

| Assembly | Before P9 | Now |
| --- | --- | --- |
| `AIBT.Runtime.Tests` | 707/707 | 707/707 |
| `AIBT.Editor.Tests` | 497/499 | 549/551 (+24 stage 1, +28 tail) |
| `AIBT.Integration.Tests` | 58/58 | 58/58 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 |

`McpDocumentationGeneratorsTests` pass, so the committed `Documentation~/generated/api-reference-*.md`
match the generator.

### The two remaining failures are host-only

Both are the same tests that failed before P9, for the same reason: the host project has imported
the Swarm Arena and Simple Agent samples under `Assets/Samples/AIBT`, whose nodes join the project
catalog. In a clean consumer both expectations hold.

- `McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` now expects the
  built-in nodes plus the `[BtNode]` classes declared in the `AIBT.Agents` assembly itself (read
  from the attributes, not from the discovery under test; all must be `aibt.unity.*`). Its failure
  message reports the difference: `Extra: [aibt.samples.swarm.attack-with-cooldown,
  aibt.samples.swarm.steer, aibt.samples.swarm.within-radius, samples.simple.next-waypoint,
  samples.simple.pick-random-point, samples.simple.set-alert] Missing: []`.
- `McpVerificationToolDispatcherTests.CompileReturnsTheSameContentHashAsADirectReferenceCompilerCall`
  now compiles directly against `ManagedNodeSet.Create(<AIBT.Agents [BtNode] classes>).Registry`,
  the registry an empty project gets. The `compile` tool uses
  `ProjectNodeCatalogCache.Current.Registry`. An Editor probe (`unity command eval_file`) compared
  the two registries in this host: the current catalog has 33 entries, the package registry 27,
  the extra entries are exactly the six sample type IDs above and none are missing; compiling the
  test tree gives `b1b5ced9…932f1` with the host catalog and `8cc9d048…8ab1e` with the package
  registry, so the hash difference comes only from the sample nodes.

## Known limits

- No Player build of a managed agent yet; only Edit Mode tests and the Editor were exercised in
  this tail. A live Play Mode run of `Samples~/SimpleAgent` through the Unity CLI (card
  verification item) was not repeated here.
- The generated API reference (`Documentation~/generated/api-reference-*.md`) does not cover
  `AIBT.Agents` yet.
- Condition interrupts (`ConditionInterrupt` on `BtCondition`) are managed-only; the native
  (Burst) path has no equivalent.
- The reference Preview window does not run managed trees (see above); it explains where they run.
- `Editor/Preview/BehaviorTreePreviewWindow.cs` is outside the card's original owned paths; the
  change was requested for this card's tail.
