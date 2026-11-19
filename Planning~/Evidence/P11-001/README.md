# P11-001 evidence: subtrees

Decision: `Documentation~/decisions/ADR-P11-001-subtrees.md` (accepted 2026-11-19, AIBT-049, all §8
recommendations). Only stage 1 of ADR §7 is approved; stages 2–4 are not started.

## Stage 1 — format v3, validator, expander, managed path (2026-11-19)

Base: `feature/aibt-consumer-workflow` at `f02ad04`. Editor 6000.6.9f1. Tests run with the async
`Tools/Unity/Invoke-UnityTests.ps1` after `Invoke-UnityRecompile.ps1`, touched classes only (one
runner on this Editor). Raw outputs, the baseline and the harness project are in
`C:\UnityProjects\_aibt_acceptance\P11-001\` (outside the repository).

### What was done

- **Model and format.** `SubtreeInterface`, `SubtreeReference`, `SubtreeKeyBinding`
  (`Authoring/Model/Tree/`); `NodeDocument.Subtree`, `TreeDocument.Interface`. The reader accepts
  `formatVersion` 3 with `interface` and `subtree`; versions 1 and 2 reject both exactly as before
  (`AIBT1004`). The writer emits `interface` after `root` (immediately before `blackboardContracts`
  / `blackboard`) and `subtree` after `description`. `Schemas~/tree.schema.json` allows version 3:
  version 1 and 2 node shapes are unchanged; version 3 nodes are an ordinary node or a reference
  node; `interface` is forbidden below version 3.
- **Validator.** `AIBT2047` SubtreeReferenceShape, `AIBT2048` SubtreeKeyTarget, `AIBT2049`
  SubtreeReferenceRoot, `AIBT2050` SubtreeInterface, in `TreeValidator` and its catalog.
- **Expander** (`Authoring/Compilation/Subtrees/`): `ISubtreeResolver`, `DirectorySubtreeResolver`
  (indexes `*.aibt` / `*.aibt.json` under a directory by tree ID), `InMemorySubtreeResolver`,
  `SubtreeExpander.Expand` per ADR §4.3–4.5, `AIBT3020`–`AIBT3028` in the compiler catalog.
  Cycle detection runs before the depth check (depth ≤ 8, a constant). Diagnostics raised inside a
  subtree carry its document ID and a related location at the reference node.
- **Identity rule.** A document with no reference node and no `interface` is returned as the same
  object (`SubtreeExpansion.IsIdentity`), and the old `ReferenceCompiler.Compile` overload is now a
  call to the new one with a null resolver.
- **Compilation.** `ReferenceCompiler.Compile(document, registry, options, resolver)` expands then
  compiles; `Compile(SubtreeExpansion, registry, options)` compiles an existing expansion. The debug
  map's `SourcePath` of every inner node is the subtree's source ID.
- **Managed path.** Internal `ManagedTreeProgram.TryCompile` overloads with a resolver, and
  `TryCompileExpanded` for stored expanded JSON plus per-node sources.
  `BehaviorTreeSource.FromJson(json, name, resolver)` and an 8-argument `SetContent` that stores
  the per-node subtree sources in two hidden serialized arrays (they survive serialization; the
  7-argument `SetContent` clears them). `BehaviorTreeImporter` is **not** changed (stage 2 wires it
  to this `SetContent`).
- **Behaviour cases.** Both case runners (`AuthoringBehaviorCaseExecutorFactory`,
  `ReferenceBehaviorCaseAdapter`) expand a case tree that uses subtrees with a
  `DirectorySubtreeResolver` over the case tree's directory.
- **Specifications** per ADR §4.8: `canonical-json-v1.md` (new section "Format version 3", plus one
  sentence giving a reference node's member order, which the draft leaves open),
  `identity-and-hashing-v1.md` ("Expanded identities"), `compiled-program-v1.md` (header bullet,
  debug-map sentence), `blackboard-v1.md` ("Subtree interface", §4.3 bullets 1–4),
  `execution-semantics-v1.md` ("Subtree references"), `diagnostics-v1.md` (table of the 13 codes),
  `data-formats.md` (pointer to the ADR), `editor-layout-v1.md:145` (subtree-reference edge
  removed).

### Hashes before and after

A temporary dumper (two test classes, category `P11BaselineDump`, deleted before commit; copies in
the acceptance folder) wrote, for every `*.aibt` / `*.aibt.json` in the package (except `Tools~`,
`Results/`, `.aibt/`, layouts), the canonical bytes hash, semantic hash and, for each of three
registries × two policies, the compiled-content and canonical semantic hash or the failing code;
and, for every semantic-slice tree, the compiled hashes with the semantic-slice registry. The
baseline was taken on `f02ad04` before any change; the second run is after all stage 1 changes.

| Dump | Baseline lines (`f02ad04`) | Lines changed or removed after | Lines added after |
|---|---|---|---|
| Package trees × registries × policies | 409 (144 OK compiled) | **0** | 42 (the 6 new fixture trees) |
| Semantic-slice trees | 57 (18 OK compiled) | **0** | 6 (the 6 new fixture trees) |

The new parent fixtures report `AIBT3020` in the dump because it calls the old overload without a
resolver, as ADR decision 5 requires. Files: `baseline-f02ad04-hashes-*.txt`,
`after-hashes-*.txt`, `diff-*.txt`.

In addition, `SubtreeExpanderTests.IdentityRule_EveryExistingTreeIsReturnedAsTheSameObjectAndCompilesToTheSameHash`
checks the same object comes back for every package tree and compiles each through the old and the
new overload under two registries, comparing the content hashes.

### Changed expectations of existing tests

| Test | Before | After | Reason |
|---|---|---|---|
| `CanonicalTreeJsonTests` (unsupported-version `TestCase`, line 50) | JSON with `formatVersion: 3` → `AIBT1005` | JSON with `formatVersion: 4` → `AIBT1005` | 3 is now a supported version; the test still checks that the first unsupported version fails with the same code. |

No other expectation changed. `SemanticSliceBehaviorTests` only gained two `TestCase` entries for
the new golden cases. `Tests/PlayMode/AIBT.PlayMode.Tests.asmdef` gained a reference to
`AIBT.Authoring` (the PlayMode test parses the subtree document).

### Added public API (additions only; no existing signature changed)

- `AIBT.Authoring.SubtreeInterface`, `SubtreeReference`, `SubtreeKeyBinding` (new types).
- `NodeDocument`: constructor with a trailing `SubtreeReference subtree`; `CreateSubtreeReference`;
  `Subtree`; `IsSubtreeReference`; `WithSubtree`.
- `TreeDocument`: `SubtreeFormatVersion = 3`; constructor with a trailing `SubtreeInterface`;
  `Interface`; `SetInterface`.
- `TreeValidationDiagnosticCodes`: `SubtreeReferenceShape`, `SubtreeKeyTarget`,
  `SubtreeReferenceRoot`, `SubtreeInterface`.
- `ISubtreeResolver`, `SubtreeResolution`, `SubtreeResolutionStatus`, `DirectorySubtreeResolver`,
  `InMemorySubtreeResolver`, `SubtreeExpander` (`MaximumNestingDepth`, `RequiresExpansion`,
  `Expand`), `SubtreeExpansion`, `SubtreeExpansionDiagnosticCodes`.
- `ReferenceCompiler.Compile(TreeDocument, NodeRegistry, ReferenceCompilerOptions, ISubtreeResolver)`
  and `Compile(SubtreeExpansion, NodeRegistry, ReferenceCompilerOptions)`.
- `BehaviorTreeSource.FromJson(string, string, ISubtreeResolver)`,
  `BehaviorTreeSource.SetContent(..., IReadOnlyDictionary<NodeId, string> subtreeNodeSourceIds)`.

`ExpectedPublicAbiV2.txt` is unchanged and `AIBT.CodeGen.ContractTests` passes (the node ABI is not
touched).

### Tests

New classes:

| Class | What it proves |
|---|---|
| `SubtreeFormatTests` | v3 fixture round-trip; every v1/v2 fixture still round-trips byte-identically; exact member order; `interface`/`subtree` rejected below v3; malformed shapes → `AIBT1004`. |
| `SubtreeValidatorTests` | One negative per `AIBT2047`–`AIBT2050` (plus variants) and catalog registration. |
| `SubtreeExpanderTests` | Identity over all package trees (≥ 40); renaming, constants, nesting, twice-used subtree; hand-copy equivalence (same source → identical content hash; separate source → only `SourcePath` differs); one negative per `AIBT3020`–`AIBT3028`; related locations; `DirectorySubtreeResolver`. |
| `SubtreeHotReloadTests` | A parameter edit inside the subtree migrates both instances (5 nodes, state kept); retargeting one reference restarts only `b:root`. |
| `SubtreeManagedTests` | Managed program with per-instance private keys written through a `BlackboardKey` parameter; missing resolver → `AIBT3020`; `BehaviorTreeAgent` with `FromJson` + resolver; stored expanded content + sources compiles like the resolver path. |
| `SubtreeGoldenEquivalenceTests` | Both golden cases give the same result and trace on the subtree tree and on its hand-written expansion; the expansion is byte-equal to the hand copy. |
| `SubtreePlayModeTests` | A `BehaviorTreeAgent` runs a tree with the same subtree twice in Play Mode. |

Golden cases: `subtree-reactive-observer` (an observer outside the subtree aborts the running
subtree branch) and `subtree-twice-private-keys` (two instances keep separate private keys).

Runs (raw output in `runs/` of the acceptance folder):

| Run | Result |
|---|---|
| New classes, EditMode (`new-tests-final.txt`) | `SubtreeFormatTests` 25/25, `SubtreeValidatorTests` 11/11, `SubtreeExpanderTests` 26/26, `SubtreeHotReloadTests` 2/2, `SubtreeManagedTests` 4/4, `CanonicalTreeJsonTests` 20/20, `SubtreeGoldenEquivalenceTests` 3/3, `SemanticSliceBehaviorTests` 16/16 |
| PlayMode (`playmode-final.txt`) | `SubtreePlayModeTests` 1/1 |
| Regression of touched areas (`regression-1.txt`) | `ReferenceCompilerTests` 18/18, `TreeValidatorTests` 39/39, `DiagnosticCodeAndCatalogTests` 17/17, `ManagedAgentLayerTests` 24/24, `ManagedParameterValidationTests` 17/17, `BehaviorTreeAgentComponentTests` 5/5, `SemanticSliceCompilationTests` 15/15, `SemanticSliceBehaviorTests` 16/16, `McpTestingToolDispatcherTests` 8/8, `McpVerificationToolDispatcherTests` 23/23, `DocumentMigratorTests` 7/7, `BlackboardSchemaTests` 13/13, `TreeScopeSlotNeverWrittenValidatorTests` 5/5, `HotReloadStateMigrationTests` 21/21, `GeneratedArtifactContractTests` 4/6 |
| ABI (`abi-1.txt`) | `AIBT.CodeGen.ContractTests` 6/6 |
| dotnet harness outside Unity (`dotnet-harness/dotnet-test.txt`) | 64/64 (`SubtreeTestKit` + Format, Validator, Expander, HotReload tests compiled without `UNITY_EDITOR` against the Unity-built `AIBT.Runtime`/`AIBT.Authoring`, NUnit 3.14, .NET 8 roll-forward, `AIBT_PACKAGE_ROOT` set) |
| `Tools~/Verification/Verify-Static.ps1` (`verify-static-1.txt`) | passed (7 schemas, 181 work items) |

The two `GeneratedArtifactContractTests` failures are the known pre-existing host-layout failures
(`PackageInfo.FindForAssembly` returns null because AIBT lives under `Assets/`; recorded in P7-018,
P7-033, P7-041); the file is not touched by this change.

### Not run / not covered

- Full run of all assemblies (left to acceptance, as in previous tasks).
- Native executor, `NativeTreeAgent`, `TreeSimulation`, importer, production assets, graph editor,
  MCP/CLI: stages 2–4.
- The reference executor's golden case for private keys shows separation through
  `initialBlackboard` and the snapshot, because reference fixture nodes only use fixed-name keys;
  writes through renamed `BlackboardKey` parameters are proven in the managed and PlayMode tests.

### Deviations from the ADR and open questions

1. `TreeDocument.LatestFormatVersion` stays 2 (callers use it as "what the writer produces for new
   trees"); version 3 is `TreeDocument.SubtreeFormatVersion`.
2. Generated `bindings` values cannot contain `:` (binding-value grammar), so a subtree whose
   generated binding names a key that the expansion would rename to `r:key` reports `AIBT3026`
   instead of being renamed. Stage 2 should decide whether to widen the grammar.
3. The expansion is always format 2 (ADR §4.4's "format 1 if every input is format 1" cannot occur:
   a tree with references is format 3, and the expansion keeps v2-only members).
4. Constants of registered (unmanaged) blackboard types report `AIBT3026`.
5. `canonical-json-v1.md` got one sentence beyond the draft: a reference node's member order.
6. The behaviour-case runner indexes the whole directory of the case tree, so the subtree golden
   trees live in their own `Trees/Subtrees/` directory.
7. `BehaviorTreeImporter` is not wired (stage 2).
