# P7-009 generic native-dispatch translator production implementation evidence

## Result

Done, with one acceptance criterion re-scoped by explicit owner decision after investigation
disclosed a real architectural constraint (see "Investigated before implementation" below).
`ADR-P6-022` is applied to production: `GenericNativeDispatchTranslatorV1`
(`Authoring/Registry/Generated/`) builds a `NativeBurstDispatchWorkspaceShapeV2` purely from a
shard's own `CanonicalDescriptorJson`, extended from the spike's single-case proof to translate a
real catalog's full `0..targetIndex` case prefix. `test-node` (`MCP/NodeDevelopment/`) is widened
to actually drive `ExecuteImmediate` for a node within the proven scope, and reports an honest,
structured "out of scope" result for one that isn't. The disclosed `generate_node` `Bool`-typed
condition-template bug is fixed.

## Investigated before implementation: the literal "non-index-0 via test-node" criterion is
## structurally unreachable

Before writing any code, tracing `StagingSlot.WriteNode` (always clears and stages exactly one node
file, in its own isolated one-node assembly) and `GeneratedMetadataEmitter.EmitShard` (dispatch
index assigned by ordering the nodes physically found *within one compiled shard*) found that a
staged node is always, structurally, dispatch index 0 -- `test-node` only ever reflects the staging
assembly, never a real applied catalog, since it is explicitly a pre-`apply-node` gate. This was
put to the owner directly (not guessed at): build the translator's full prefix support anyway
(matching what `ADR-P6-022` actually decided), proving that part against a dedicated, permanent,
real-compiled two-in-scope-plus-one-blocker fixture rather than through the live tool, which can
only ever exercise the index-0 case. Approved and implemented as described below.

## Implementation

- `Authoring/Registry/Generated/GenericNativeDispatchTranslatorV1.cs` (new, `internal`, `AIBT.Authoring`
  -- not `Runtime/Execution/Burst/Dispatch/` as the card's own text tentatively suggested:
  `AIBT.Runtime` cannot reference `AIBT.Authoring`'s `GeneratedShardMetadataMaterializer`/
  `GeneratedNodeDescriptor`, while `AIBT.Authoring` already references `AIBT.Runtime` and is already
  granted `InternalsVisibleTo` for its internal dispatch contracts -- confirmed against
  `architecture.md`'s dependency direction before choosing, per the card's own Allowed-changes note).
  Ports the spike's proven single-case mapping (name-to-name `GeneratedFieldEncoding`/
  `GeneratedBindingKind` translation, phase-mask bit-testing, the two-canonical-range-per-case/
  binding requirement) essentially verbatim, extended to flatten `0..targetIndex` cases into the
  shared arrays `NativeBurstDispatchCaseV2.FirstConfigurationField`/`FirstMemoryField`/`FirstBinding`
  index into -- confirmed by reading `NativeBurstDispatchBindingValidationV2`'s own real consuming
  code (not assumed): `NativeBurstDispatchFieldV2.FieldOrdinal`/`NativeBurstDispatchBindingV2.
  BindingOrdinal`/`ConfigurationFieldOrdinal` are all **local to their own case** (`binding.BindingOrdinal
  != localOrdinal` is asserted directly), while `FirstPrimaryValueField` is a **global** position in
  the separate, binding-flattened `valueFields` array. Every case in the prefix, not only the
  target, is scope-checked; an out-of-scope case anywhere in the prefix fails with a structured
  reason naming that case's own `TypeId`.
- `MCP/NodeDevelopment/GenericNodeDispatchRunner.cs` (new): drives the translator's output through a
  real `NativeBurstDispatchWorkspaceOwnerV2`, with a **generic, zero-initialized request** (no
  per-field-name knowledge -- sized purely from each field/binding's own declared byte size, so it
  works for any node shape within the proven scope, not only `ThresholdCondition`-like ones). Drives
  `Enter` then `Tick` (gated on the node's own declared phase mask), invoking the dynamically
  compiled catalog's `ExecuteImmediate` via reflection (`MethodInfo.Invoke` -- `BurstExecutionBatch`
  is a normal `public struct`, not a `ref struct`, so by-ref reflection invocation works). Reports the
  real observed `NodeStatus`/`BurstContextResult`, never a comparison against an expected value --
  the acceptance criterion is "reports a real tick result," not "matches a golden expectation."
- **A real, disclosed compile-time discovery mid-implementation, found empirically (not assumed):**
  `[AibtCatalogSet]` cannot reference a shard (`typeof(...)`) declared in the *same* compiled
  assembly -- confirmed by a real first attempt failing `AIBT5011` ("lacks a usable generated shard
  authority"), then confirmed by inspecting `Samples~/BurstNodes/`'s own asmdef split
  (`AIBT.BurstNodes.Sample` for the shard, `AIBT.BurstNodes.Sample.Catalog` referencing it by name
  for the catalog set) -- a shard's generated `IsUsable`/`AbiVersion` authority members are only
  visible to a catalog-set generator pass in an assembly that references the shard's own assembly.
  `StagingSlot.WriteCatalogSet` now stages the companion `[AibtCatalogSet]` file into its own
  `Pending/Catalog/` sub-assembly (`AIBT.Generated.Staging.Catalog`, referencing
  `AIBT.Generated.Staging` and `Unity.Burst` by name), written by `GenerateNode` right after the node
  itself so every staged generation always has one. `StagingSlot.ListStagedFiles` became recursive
  (so preview/hash reflect the full generation); `TryGetStagedNodeFile` (new) isolates the node file
  specifically for callers (`generate-node-tests-and-manifest`) that need it, not "whatever sorts
  first" across the now-recursive listing. `apply-node`'s own `MoveTo` is deliberately untouched
  (still top-level-only) -- the catalog-set file is a staging-time verification artifact, never
  something a project ships.
- `GeneratedNodeReflectionHarness.cs`: `TryFindCatalogSetType`/`TryReflectHandshake`/
  `TryGetExecuteImmediate`/`TryMaterializeArtifact` (new), mirroring the existing shard-reflection
  methods' own style.
- `MCP/NodeDevelopment/NodeTemplateGenerator.cs`: the `Bool`-typed condition-template fix
  (`ComparisonOperator` -- `==` for `Bool`, unchanged `>=` for every numeric type) plus
  `GenerateCatalogSet` (the new companion-file template).

## Verification

```text
Live Unity MCP execute_code, calling the real production AIBT.Mcp.McpToolDispatcher.Dispatch
  directly against the real open Editor (generate_node -> compile -> analyze_and_compile_node ->
  test_node, exactly the real tool sequence, staged files deleted afterward):
  - A real UInt32-threshold condition node: dispatchProven=true, enteredSuccessfully=true,
    tickStatus="Success" (0 >= 0, consistent with the fully zero-initialized synthetic request) --
    real generated ExecuteImmediate actually executed, not simulated.
  - A real async-write action node (AsyncOperation/Completion bindings): dispatchProven=false, with
    a structured reason naming the exact out-of-scope construct -- never a false pass.
  - A real Bool-typed condition node: generated source uses "current == config.Minimum" (not the
    previously-broken "current >= config.Minimum"), compiled clean through the real Roslyn analyzer,
    and dispatchProven=true, tickStatus="Success".
Live Unity MCP run_tests (EditMode), GenericNativeDispatchTranslatorV1Tests (new,
  Tests/Editor/CodeGen/Dispatch/, a real 3-node compiled shard via the packaged analyzer -- not an
  isolated project, mirroring Tests/Editor/CodeGen/Generation/GeneratedArtifactContractTests.cs's
  own already-established in-assembly-analyzer pattern): 4/4 passing
  - ThreeNodeCatalog_MatchesExpectedAlphabeticalDispatchOrder
  - PrefixTranslation_TargetNotAtIndexZero_BuildsAStructurallyValidWorkspace
  - PrefixTranslation_OutOfScopeCaseBlocksALaterInScopeTarget_NamingTheBlockingCase
  - PrefixTranslation_TwoInScopeCasesOnly_BuildsAStructurallyValidWorkspace (proves the flattened
    per-case cursor bookkeeping via the real production TryCreate/ValidateShape path)
Live Unity MCP run_tests (EditMode), McpNodeDevelopmentToolDispatcherTests: 19/19 passing (18
  pre-existing plus 1 new Bool-template source-text regression test; several existing assertions
  updated for the now-2-file staged generation -- node + companion catalog-set -- disclosed, not
  silently weakened: file-count expectations, PreviewNodeDiff's returned file list).
Live Unity MCP run_tests (EditMode), full suite: 1597 total, 1594 passed, 3 failed -- all 3
  pre-existing and unrelated (2x GeneratedArtifactContractTests "CodeGen test assembly must belong
  to the AIBT package" -- a package-path environment assumption; 1x LocalSaveSystem.Tests.
  SaveStoreTests -- an unrelated non-AIBT package).
Tools~/Verification/Verify-Static.ps1 -- passed (121 work items, 6 schemas)
git diff --check -- clean
```

`Run-UnityTests.ps1 -Mode EditMode -Scope Full` was not additionally invoked as an external
process -- the live Unity MCP `run_tests` call above covers the exact same EditMode suite via the
already-open Editor instance.

## Scope and limitations

- `Registered`-encoded fields and `AsyncOperation`/`Completion` bindings remain explicitly unproven
  and undesigned, per the ADR's own scope -- confirmed reported honestly by the widened `test-node`,
  never silently skipped.
- `test-node`'s own live dispatch proof can only ever exercise a staged node at dispatch index 0,
  a real, disclosed structural limit of today's one-node-at-a-time staging architecture (see
  "Investigated before implementation" above) -- not a limitation of the translator itself, which
  is proven against a real, non-index-0, multi-case prefix by a dedicated permanent fixture instead.
- The generic dispatch runner's zero-initialized request never asserts a specific tick outcome
  (e.g. "Success" vs "Failure") -- proving real execution happened is the acceptance criterion, not
  matching a golden value; a project author driving `test-node` against their own node should not
  read a specific `tickStatus` as meaningful without also inspecting their own node's actual
  configured behavior.
- `apply-node` does not move the companion catalog-set file into a project's real, applied location
  -- whether an applied custom node needs its own shipped catalog-set wiring (mirroring
  `Samples~/BurstNodes/Catalog/`) is a separate, undecided question this card's own scope does not
  cover.
