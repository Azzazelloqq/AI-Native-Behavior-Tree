# P11-008 evidence — review fixes, batch 2 (stale pin, F8, F9, F10, F11, F13 ADR)

Branch `feature/aibt-consumer-workflow`, base `45de034`. Raw files: `C:\UnityProjects\_aibt_acceptance\P11-008\`.
Unity 6000.6.9f1, the Modules Editor (Pipeline port 7810). Findings from `Planning~/Evidence/P11-REVIEW/README.md`.
Approved by the owner on 2026-11-22 ("Да, пакет 2"). Status: accepted by the orchestrator on `7773059`
("Acceptance run" at the end); the item is in `Review`. F9 and the native half of F10 wait for the owner.

## Commits

| Commit | Finding | Result |
| --- | --- | --- |
| `8c4cba2` | stale pin | refreshed: only `contentHash` and bytes 54-85 (`header.nodeRegistryHash`) |
| `18aca41` | F8 | fixed |
| — | F9 | **stopped, not changed**: the failing case is one the package claims to support, the cause looks small (see F9) |
| `028c7ce` | F10 | managed half fixed; **native half stopped**: a passing test asserts the silent behaviour (see F10) |
| `6d34b8d` | F11 | fixed |
| this commit | F13 | ADR proposed, no code; task card, work item, MASTER_PLAN, this evidence |

Every fix commit holds its test. "Before" runs were made with the test in place and the fix absent;
"after" runs with both. The Modules Editor was not running at the start; it was started with
`Start-UnityEditor.ps1` (no `-Restart`). The script exited with an error (a stderr hint of `unity open`
under `ErrorActionPreference = Stop`), but the Editor started and answered `editor_status` as ready.

## Stale pin (`Tests/Fixtures/P2/CodeGen/generated-compiled-v2.json`)

- **Bytes from the compiler now:** `pin\probe-actual-bytes.cs` (the P11-007 probe, the same compile as
  `GeneratedArtifactContractTests.EmittedMetadata_…`) run at `45de034` → `pin\actual-at-45de034.json`.
- **Only offsets 54-85 differ:** `pin\refresh_pin.py` compares the old pin with the new stream and writes
  the fixture only if the lengths are equal (861/861), the differing offsets are exactly 54..85 and those
  bytes equal the new `header.nodeRegistryHash`. Output `pin\diff.txt`: 32 differing offsets, first 54,
  last 85; old `f9a1e3c0…dc48`, new `9c189ffd…7b01`; content hash `60dc5c12…3712` → `426504d3…6595`.
  The new values equal the P11-007 investigation's `actual-at-c02d92f.json`.
- **After:** `pin\after-codegen-generation.txt`: `AIBT.CodeGen.GenerationTests` 14/14 (was 13/14).

## F8 — the host read let `T` and `expectedType` disagree

- **Cause verified:** `GeneratedTreeDispatchAdapterV2.TryResolveExternalTreeRead<T>` compared
  `expectedType` with the slot and only size and alignment of `T`.
- **Mapping checked:** each of the 17 built-in blackboard types has its own managed struct
  (`BuiltInBlackboardTypes`, `BlackboardTypeDescriptor.cs:244-260`): `bool`, `int`, `long`, `float`,
  `double`, `Float2Value`, `Float3Value`, `QuaternionValue`, `Enum32Value`, `FixedString32/64/128/512Bytes`,
  `AgentId`, `EntityId`, `OperationId`, `AssetId`. No two share a managed type, so the mapping from `T` is
  exact and no case needed a decision. Registered types were already refused by this read.
- **Fix** (adapter +39 −2): `BuiltInManagedType<T>.TypeId`, a static per-`T` value computed once; resolve
  refuses with `BlackboardTypeMismatch` when it differs from `expectedType.TypeId`. The doc comment of the
  public method says so. Signature unchanged; `TryReadExternalTreeValue` unchanged.
- **Test:** `ExternalTreeBlackboardReadTests.ResolveRefuses_ATypeArgumentThatIsNotTheManagedTypeOfExpectedType`:
  `int`/Float32 (the review's case), `uint`/Int32, `float`/Int32, `byte`/Bool, a struct of three floats/Float3
  are refused; `float`/Float32 and `Float3Value`/Float3 still read.
- **Before:** `F8\before.txt`: 10/11, the new test fails ("live-target … Expected: False But was: True").
  **After:** `F8\after.txt`: `ExternalTreeBlackboardReadTests` 11/11, `NativeTreeAgentValueReadTests` 10/10,
  `ExternalTreeBlackboardWrite*` 9/9.
- **Allocation-free read after resolve:** `ExternalTreeBlackboardReadTests.HandleRead_RepeatedAfterWarmup_AllocatesNoManagedMemory`
  (1000 reads through a warmed handle, `Is.Not.AllocatingGCMemory()`) passes in the after run. The resolve
  itself allocates once per `T` (static initializer); it is not the per-frame path.
- **For a user:** code that resolved with a type of the right size but not the built-in's own type
  (`uint` for Int32, `Unity.Mathematics.float3` or an own struct for Float3) now gets `BlackboardTypeMismatch`.
  `NativeTreeAgent.TryGetValue` is not affected (it derives the type from `T`).
- **Not changed:** the write API `TryResolveExternalTreeWrite<T>` has the same shape (review F8 says so);
  it was not in the brief.

## F9 — Agent-scope key in a native tree ends in AIBT3017: stopped

- **Exact condition** (`F9\probe-f9-condition.cs` → `F9\probe-f9-condition-output.txt`, Native Guard tree
  through `GeneratedCompiledProgramV2Compiler`): a native tree with at least one Tree-scope key **and** at least
  one Agent- or Shared-scope key fails with `AIBT3017 Generated compiled v2 construction failed: Tree-scope
  blackboard memory ranges overlap`. Tree keys only: compiles. Agent keys only: compiles (the fixture
  `Tests/Fixtures/P2/CodeGen/generated-tree-v2.aibt.json`, `GenerationTests` 14/14). Slot offsets from the
  probe: `gate-open[Tree off=0 size=1] … probe-agent[Agent off=0 size=4]`.
- **Cause (read, consistent with the probe):** `GeneratedScopeCompiler.BuildSlots` gives each scope its own
  zero-based offsets; `GeneratedCompiledProgramV2.BuildFinalSemanticProgram` builds the semantic slot records
  with the scope fixed to `BlackboardScope.Tree` and the scope-local offset
  (`Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs:679`), so `CompiledProgram`'s per-scope
  overlap check (`Runtime/Compiled/Model/CompiledProgram.cs:281`) sees two Tree ranges at offset 0. The
  serialized v2 stream uses the scope-correct slot list, not these records.
- **Why stopped:** the package claims the case. `GeneratedTreeDispatchAdapterV2.cs:803-810` hosts Agent-scope
  slots in the same buffer as Tree-scope ones past `agentBaseOffset` ("independently zero-based Offset space");
  `Documentation~/simulation-container.md:8` says generated nodes may use Tree/Agent-scope keys;
  `GeneratedScopeCompilerTests` compiles documents with Tree, Agent and Shared keys at the scope stage. The
  brief says to stop and report in that case instead of refusing. Nothing was changed for F9.
- **Not verified:** whether labelling the semantic record with its real scope (or offsetting it) is enough for
  the whole host/agent run; whether the review's other variant (Agent key bound to a library node,
  AIBT3015 + AIBT3018) is the same cause. Options for the owner are in the report; hand-over note
  `F9\F9-stop.md`.

## F10 — observers

- **What works today (checked before any change):** the reference executor runs observers with its
  evaluator registry (behaviour cases `patrol-react`, `subtree-reactive-observer`, the Editor preview);
  validation refuses an observer outside a v1 reactive composite and under a retaining one (AIBT2034,
  `TreeValidator.cs:874-895`); the generated compiler compiles the observer tables (the fixture
  `generated-tree-v2.aibt.json` has an `observer` block and its pinned bytes include them); the native
  machine has an observer-transition API used by low-level tests (`NativeParallelOuterAbortTargetTests`), but
  no host evaluates observers.
- **Managed agent — fixed.** `ManagedTreeInstance` binds `ReferenceObserverConditionRegistry.Empty`, so a
  tree with an observer loaded and then faulted with AIBT4002 when a watched key changed (read:
  `ManagedTreeInstance.cs:127`, `ReferenceExecutionMachine.cs:2543-2548`; the run-time fault was not
  reproduced here). Now `ManagedTreeProgram` (the compile every `BehaviorTreeAgent` load goes through) refuses
  such a tree with the new error **AIBT2051** `ObserverNotImplementedInManagedAgent`, one per node, at
  `/nodes/<id>/observer`, naming the node, the reason and the alternatives (reactive composite, the
  condition's `interrupt` field). `ProjectNodeCatalog.ValidateAuthoring` (graph, MCP) and
  `TreeCatalogEntry` (tree list) add the same error for trees they validate with the managed policy.
- **Native hosts — stopped.** `Tests/Integration/NativeRuntime/NativeLibraryReactiveGuardTests` (P11-002)
  runs every guard case with and without an `observer` block and asserts that the block "compiles, runs and
  changes nothing" (`withObserverBlock = true` cases). A native refusal at validation, compile or load would
  break these passing tests, so per the brief the native path is unchanged and still documented as such
  (`production-host.md:46-54`, `native-node-library.md`, `README.md`).
- **Specification:** one note in `Documentation~/specifications/execution-semantics-v1.md` (end of "Abort
  observers"): observer re-evaluation is implemented by the reference executor only; the managed agent refuses
  the block at load (AIBT2051); native hosts compile it and it changes no outcome. No rule changed.
- **Test:** `Tests/Editor/Agents/ManagedObserverRefusalTests.cs`: a tree with the block is refused at load
  (AIBT2051, Error, node `guard`, pointer, message); the same tree without the block loads and the reactive
  guard fails the branch at the next tick; `ProjectNodeCatalog.ValidateAuthoring` reports AIBT2051.
- **Before:** `F10\before.txt`: 1/3 (the refusal and the Editor validation fail; the control passes).
  **After:** `F10\after-class.txt` 3/3; `F10\after-neighbours.txt`: `AIBT.Tests.Editor.Agents` 119/119,
  `TreeCatalog*` 5/5, `AgentConsumerToolsTests` 14/14, `TreeValidatorTests` 42/42, `SubtreeValidatorTests`
  11/11, `McpVerificationToolDispatcherTests` 23/23, `GraphEditorWorkflowTests` 15/15,
  `ProductionConsumerWorkflowTests` 12/12; `F10\after-docs.txt`: `McpDocumentationGeneratorsTests` 14/14.
- **Not covered:** a tree with only built-in nodes is validated in the Editor with the native/reference
  policy (the Editor decides "managed" from managed nodes), so such a tree shows no AIBT2051 in the Editor
  but is refused when a `BehaviorTreeAgent` loads it.

## F11 — AIBT2042 and AIBT2043 each had two meanings

- **History** (`git log -S`): compiler `MissingScopeContract` AIBT2042 and `ScopeContractMismatch` AIBT2043
  were added in `360bbe7` (2026-08-18); validator `MigrationApplied` AIBT2042 in `0b29f78` (2026-09-03) and
  `TreeScopeSlotNeverWritten` AIBT2043 in `5050f67` (2026-09-07). By the brief's rule the validator codes,
  the later meanings, move: **`MigrationApplied` → AIBT2052, `TreeScopeSlotNeverWritten` → AIBT2053**. The
  review had suggested renumbering the compiler codes; the brief's rule decides the opposite. AIBT2053 is the
  input-key note users see on native trees.
- **Free numbers:** AIBT2051 (F10), 2052, 2053 follow the highest validator code AIBT2050; the gap AIBT2009
  was not used.
- **Updated:** `TreeValidationDiagnostics.cs`, comments in `NodeMigrationOutcome.cs`,
  `GeneratedBindingWriteIndex.cs`, `TreeValidator.cs`, `McpVerificationToolDispatcher.cs`; the generators
  `McpMigrationsDocumentGenerator.cs`, `McpRecipesDocumentGenerator.cs` and their pages `generated/migrations.md`,
  `generated/recipes.md`, `generated/api-reference-authoring.md` (regenerated with the generator calls of
  `McpDocumentationGeneratorsTests`, raw script `regenerate-docs.cs`); `README.md`, `editor-and-layout.md`,
  the code reference in `specifications/blackboard-v1.md`; dated notes in ADR-P7-005, ADR-P7-039,
  ADR-P11-001; `CHANGELOG.md` (all under `[Unreleased]`; the earlier entries now show AIBT2053 and a new
  entry states the renumbering). Tests: the literal codes in `TreeScopeSlotNeverWrittenNativeBindingTests`
  (a check that the note is absent and a JSON filter now look for AIBT2053) and three comments. The compiler
  meanings, the normative table in `specifications/agent-shared-blackboard-v1.md` and `Spikes~` are unchanged.
  Historical planning and evidence files keep the old numbers.
- **Test:** `Tests/Editor/Diagnostics/DiagnosticCodeUniquenessTests.cs`: every static `DiagnosticCode` field in
  the non-test AIBT assemblies; a code declared under two different field names fails.
- **Before:** `F11\before.txt`: 0/1, exactly `AIBT2042: …MissingScopeContract, …MigrationApplied` and
  `AIBT2043: …ScopeContractMismatch, …TreeScopeSlotNeverWritten`. **After:** `F11\after.txt`:
  `DiagnosticCodeUniquenessTests` 1/1, `TreeScopeSlotNeverWritten*` 24/24, `NativeBindingInputNoteHostRuleTests`
  1/1, `McpDocumentationGeneratorsTests` 14/14, `*Migrat*` 50/50, `McpVerificationToolDispatcherTests` 23/23,
  `SubtreeImportAndBakeTests` 6/6, `GeneratedScopeCompilerTests` 14/14, `SubtreeValidatorTests` 11/11,
  `UserGuideSnippetTests` 6/6.
- **Seen, not changed:** `AIBT3027` is also written inline in `Editor/Graph/SubtreeGraphEdits.cs:331`
  (unpack would add existing node IDs); same meaning as `SubtreeQualifiedIdentity` (ID collision), not a field,
  so the test does not count it.

## F13 — ADR only

`Documentation~/decisions/ADR-P11-008-reproducible-random-streams.md`, status Proposed, decision ID AIBT-052
reserved (not yet in `decisions.md`, which records accepted direction). Stream identity = stable agent id +
run index; sources for `NativeTreeAgent`, a bare `ProductionTreeHost` and scheduler-driven hosts; what changes
for users; the reproducibility test; five open questions. Numbered after its work item, as ADR-P11-001/-002/-004
are (P11-003 had no ADR). No production change.

## Protected paths

`git diff --stat 45de034 HEAD` is empty for `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`,
`CodeGen~` and `package.json`; under `Tests/Fixtures` only the pin changed. Specifications: the F10 note and
the F11 code reference. Public API additions only: `TreeValidationDiagnosticCodes.ObserverNotImplementedInManagedAgent`;
the values of two existing public codes changed (F11, approved).

## Tests run

All EditMode through `Invoke-UnityTests.ps1`, by `-Filter` or `-Assemblies`; raw output in the files named.

| Run (file) | Result |
| --- | --- |
| `pin\after-codegen-generation.txt` | `AIBT.CodeGen.GenerationTests` 14/14 |
| `F8\before.txt` | `ExternalTreeBlackboardReadTests` 10/11 |
| `F8\after.txt` | `ExternalTreeBlackboardReadTests` 11/11, `NativeTreeAgentValueReadTests` 10/10, `ExternalTreeBlackboardWrite*` 9/9 |
| `F10\before.txt` | `ManagedObserverRefusalTests` 1/3 |
| `F10\after-class.txt` | `ManagedObserverRefusalTests` 3/3 |
| `F10\after-neighbours.txt` | 119/119, 5/5, 14/14, 42/42, 11/11, 23/23, 15/15, 12/12 (classes above) |
| `F10\after-docs.txt` | `McpDocumentationGeneratorsTests` 14/14 |
| `F11\before.txt` | `DiagnosticCodeUniquenessTests` 0/1 |
| `F11\after.txt` | 1/1, 24/24, 1/1, 14/14, 50/50, 23/23, 6/6, 14/14, 11/11, 6/6 (classes above) |

Not run: the full ten assemblies (the orchestrator's run), `NativeLibraryReactiveGuardTests` (nothing touched it).

## Console

`console-after-F11.txt`: Unity's own "Release of invalid GC handle" and "Failed to load 'VirtualArtifacts/…'"
errors (also seen in P11-007), one "Failed to handle /api/exec request: Main thread operation timed out after
5000ms" from the documentation-regeneration eval (it completed; its files were checked), and the expected logs
of tests. No error from AIBT code.

## Not done / not verified

- F9: no change; the owner decides between fixing the semantic projection and refusing the case.
- F10: no native refusal; the native observer block still compiles and is ignored, as documented.
- F10: the managed AIBT4002 run-time fault was established by reading, not reproduced.
- F8: the write API keeps the size-only check.
- The full ten assemblies were not run here.

## Acceptance run (orchestrator, 2026-11-22)

Read by the orchestrator on `7773059` (clean working tree): this file, the session's report, the
whole diff `45de034..7773059` under `Runtime/`, `Agents/`, `Authoring/`, `Editor/`,
`MCP/Verification`, `Documentation~/specifications` and `Tests/Fixtures`, and ADR-P11-008.

- **Pin.** Recomputed from git: `bytesHex` of `45de034` and of `7773059` are both 861 bytes and
  differ in 32 bytes, offsets 54-85; `contentHash` is `426504d3...`.
- **Protected paths.** `git diff --stat 45de034..7773059` is empty for
  `Tests/Editor/CodeGen/Contracts`, `CodeGen~` and `package.json`. Under `Tests/Fixtures` only the
  pin. Under `Documentation~/specifications`: the implementation-status note in
  `execution-semantics-v1.md` (7 lines, no rule changed) and one code reference in
  `blackboard-v1.md` (`AIBT2043` -> `AIBT2053`).
- **F8.** One added condition in the adapter's `TryResolveExternalTreeRead<T>`
  (`BuiltInManagedType<T>.TypeId != expectedType.TypeId`) and a static generic class with the 17
  built-in managed types; no signature changed.
- **F11 direction.** Confirmed by `git log -S`: the compiler's `AIBT2042`/`AIBT2043` are from
  `360bbe7` (2026-08-18), the validator's from `0b29f78` (2026-09-03) and `5050f67` (2026-09-07). So
  by the approved rule the validator codes moved; the note users see on an input key is now
  `AIBT2053`. This is the opposite of the review's suggestion and is reported to the owner.
- **Stops.** Both are stops the brief asked for. F9: the semantic slot records are built with
  `BlackboardScope.Tree` and scope-local offsets (`GeneratedCompiledProgramV2.cs:679`, read by the
  orchestrator). F10 native half: `NativeLibraryReactiveGuardTests` states in its summary that an
  `observer` block "compiles, runs and changes nothing" and runs every case with and without one.
- **Full run of ten assemblies** on `7773059`, 2026-11-22 22:10-22:27; Editor-log counters did not
  move (unknown-command 0, main-thread-timeout 3 before and after; the Editor had been started
  anew by the session, so the counters are lower than in the previous run). Raw output:
  `C:\UnityProjects\_aibt_acceptance\P11-008\acceptance\full-run-7773059.txt`.

| Assembly | `42253ac` | `7773059` | New tests |
|---|---|---|---|
| `AIBT.Runtime.Tests` | 815/815 | 815/815 | - |
| `AIBT.Integration.Tests` | 440/440 | 441/441 | `ExternalTreeBlackboardReadTests` +1 |
| `AIBT.Editor.Tests` | 842/842 | 846/846 | `ManagedObserverRefusalTests` 3, `DiagnosticCodeUniquenessTests` 1 |
| `AIBT.BehaviorCases.Tests` | 65/65 | 65/65 | - |
| `AIBT.NativeBurstDispatch.Tests` | 79/79 | 79/79 | - |
| `AIBT.NativeTreeAgentBlackboard.Tests` | 33/33 | 33/33 | - |
| `AIBT.NativeSharedBlackboard.Tests` | 36/36 | 36/36 | - |
| `AIBT.CodeGen.ContractTests` | 6/6 | 6/6 | - |
| `AIBT.CodeGen.GenerationTests` | 13/14 | 14/14 | - |
| `AIBT.PlayMode.Tests` (PlayMode) | 2/2 | 2/2 | - |

All ten assemblies pass with no failure for the first time in this phase.

Not done by the orchestrator: the new test files and the documentation changes outside the
specifications were not read line by line; the F9 probe was not run again; nothing was looked at
in the Editor window.

To know:
- The Modules Editor was not running when the session started (cause unknown); the session started
  it without `-Restart`.
- A tree with only built-in nodes shows no `AIBT2051` in the Editor, but a `BehaviorTreeAgent`
  refuses it at load.
- The external write (`TryWriteExternalTreeValue<T>`) still checks only size and alignment of `T`:
  the same class of defect as F8, on the write side, not in this batch.
- Open for the owner: F9 (fix the semantic projection or refuse the case), the native half of F10
  (refuse the block, which breaks the P11-002 tests and documentation, or keep the documented
  silence), ADR-P11-008 (five questions), F6 and F14 (not in any batch).
