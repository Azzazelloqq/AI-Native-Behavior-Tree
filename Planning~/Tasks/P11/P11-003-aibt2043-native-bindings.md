# P11-003 — AIBT2043 counts writes through native `bindings`

Status: `In Progress` — implemented on `feature/aibt-consumer-workflow` after `360538b`, awaits the
orchestrator's acceptance. Two deviations from the brief, each allowed by the owner on 2026-11-20
(see "Handoff notes").

## Objective

The Info note `AIBT2043` ("Input key ...: no node in this tree writes it") marks a Tree-scope key that
game code feeds. Before this item it also appeared for keys that native (Burst) nodes write through
`bindings`, because the validator only read the node manifest's `writes` (empty for generated nodes)
and managed `BlackboardKey` parameters. After it, a key bound through a `Write` or `ReadWrite` member
of a generated node counts as written; a key bound only through `Read` members keeps the note. The
keys without the note on a native tree are then exactly the keys
`ProductionTreeHost.TryResolveExternalTreeWrite` refuses (`blackboard-v1.md:80`).

## Depends on

- `P11-002` (open question 2 of stages 1–4; the owner approved this item on 2026-11-20).

## Required reading

- `Planning~/Evidence/P11-002/README.md` (open question 2), `Planning~/Tasks/P11/P11-002-native-node-library.md`.
- `Authoring/Validation/TreeValidator.cs` (`ValidateNodes`, `ValidateBlackboardWriteCoverage`).
- `Authoring/Registry/Generated/GeneratedNodeContracts.cs` (`GeneratedBindingDescriptor.Kind`).
- `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs` (`CompileDocument`, `CreateSemanticProjection`).
- `Runtime/Integration/GeneratedDispatch/GeneratedTreeDispatchAdapterV2.cs` (`TryResolveExternalTreeWrite`).

## Scope

- `Authoring/Validation/**`: the validator reads Tree-scope `Write`/`ReadWrite` binding members of
  generated nodes from an internal index keyed by the manifest instance (`GeneratedBindingWriteIndex`).
- One line in the body of the long `GeneratedNodeDescriptor` constructor that registers the
  descriptor's bindings in that index (owner, 2026-11-20, «Да, вариант B»).
- One place in `GeneratedCompiledProgramV2Compiler.CompileDocument` that adds the semantic
  projection's diagnostics: it drops only the validator's Info `AIBT2043` (owner, 2026-11-20,
  «Да, разрешаю»).
- Tests under `Tests/Editor/Validation/` and `Tests/Integration/NativeRuntime/`.
- `MCP/Documentation/McpRecipesDocumentGenerator.cs` and the regenerated `Documentation~/generated/recipes.md`;
  `CHANGELOG.md`.

## Forbidden changes

- `Runtime/`, `CodeGen~`, `Schemas~`, `Agents`; any other change in `Authoring/Compilation`;
  `Authoring/Registry/Generated/StandardNative/`; `Samples~` code and trees; `package.json`;
  `Documentation~/specifications/`; `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt`.
- Any other signature or member of `GeneratedNodeContracts.cs`; any public API.
- Compiled programs, content hashes, prepared-asset hashes, catalog fingerprints; any diagnostic other
  than whether `AIBT2043` is present; a new diagnostic code; the wording of `AIBT2043`.

## Acceptance criteria

- A key bound through a `Write` or `ReadWrite` member of a generated node has no `AIBT2043`; a key bound
  only through `Read` members keeps it; managed key parameters and manifest writes count as before; a
  manifest without a registered generated descriptor keeps the earlier behaviour.
- The same on every path that shows the note: graph (`ProjectNodeCatalog.ValidateAuthoring`), MCP/CLI
  `validate` and edits, the generated compiler and the baker (prepare_runtime), subtrees (private `r:key`
  and interface keys).
- Host rule: on a native tree the Tree keys without the note are exactly the keys
  `TryResolveExternalTreeWrite` refuses.
- The compiler's error `AIBT2043` (`ScopeContractMismatch`) is never filtered; the managed agent and the
  direct `ReferenceCompiler.Compile` path report the same as before.
- Hashes of Native Guard, of the recipe tree and of a tree with subtrees are unchanged.
- `AIBT.CodeGen.ContractTests` 6/6; no existing test expectation changes except an `AIBT2043` pin on a
  natively written key.

## Required verification

```text
Tools/Unity/Invoke-UnityRecompile.ps1
Tools/Unity/Invoke-UnityTests.ps1 -Filter TreeScopeSlotNeverWrittenNativeBindingTests,NativeBindingInputNoteHostRuleTests,<affected classes>
Tools/Unity/Invoke-UnityTests.ps1 -Assemblies AIBT.CodeGen.ContractTests  (6/6)
Tools~/Verification/Verify-Static.ps1
Save-UnityWindow.ps1 -Window 'AIBT Graph' (Native Guard, before and after)
Recipe 2 steps run live through the CLI (before and after)
```

## Handoff notes

- Evidence: `Planning~/Evidence/P11-003/README.md`; raw runs, probes, notes and screenshots in
  `C:\UnityProjects\_aibt_acceptance\P11-003\`.
- Step 0 found that the access of a binding member exists only in `GeneratedBindingDescriptor.Kind`;
  the manifest the registry holds has empty `reads`/`writes` (`CodeGen~/AIBT.CodeGen/GeneratedMetadataEmitter.cs:171`).
  The validator cannot reach the descriptor, so the brief's "only `Authoring/Validation`" could not be
  met. Deviation 1 (owner, 2026-11-20, «Да, вариант B»): one registration line in the
  `GeneratedNodeDescriptor` constructor.
- Implementing it showed that the generated compiler validates a semantic projection of the tree (native
  nodes replaced, bindings dropped) and adds its diagnostics, so the note came back for every key.
  Deviation 2 (owner, 2026-11-20, «Да, разрешаю»): `CompileDocument` drops the projection's Info
  `AIBT2043`; the real document's validation stays the source of the note.
- `blackboard-v1.md:80` says `AIBT2043` is the compile-time signal of `(AccessFlags & Write) == 0`; it
  was untrue for native nodes before this item and is true after it. Not edited.
