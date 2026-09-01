# P7-008 per-project leaf-registration mechanism implementation evidence

## Result

Done. `ADR-P6-017` (`AIBT-031`, Accepted) is applied to production: a project can now register its
own reference-executor leaf node through a genuinely public surface, and that registration is
visible to `aibt_search_nodes`/`aibt_get_node_contract` in the same live Editor session it was
registered in -- closing both the ADR's own deferred design question and the concrete
`aibt_apply_node`-then-invisible gap the `P6-012` gate reproduced live.

## Implementation

- `Runtime/Execution/Reference/Leaves/Public/ReferenceLeafBehavior.cs` (new, `AIBT.Runtime`):
  `IReferenceLeafBehavior` (public equivalent of the internal `IReferenceLeafHandler`) and
  `ReferenceLeafContext` (public `readonly ref struct`, holds a private by-value copy of the
  internal `ReferenceNodeContext` -- safe because `Configuration`/`Memory` are span views over
  arrays already held by reference, and blackboard I/O is forwarded through the internal context's
  own captured service interface, not mutable struct state). `ProjectReferenceLeafHandlerAdapter`
  (internal) drops a project's `IReferenceLeafBehavior` into the existing
  `ReferenceLeafBinding`/`ReferenceLeafRegistry` machinery unchanged.
  - `Runtime/Core/Execution/NodeStatus.cs`: `NodeExitReason`/`NodeAbortReason` widened from
    `internal` to `public` -- a required, minimal consequence of the new public `Abort`/`Exit`
    signatures (both enums are stable, behavior-free reason codes; no other change).
  - v1 scope: no async-operation support (`TryStartOperation`/`TryConsumeCompletion`/
    `TryCancelOperation`) on the public context. Not required by any accepted deliverable.
- `Authoring/Registry/ReferenceLeafBehaviorProvider.cs` (new, `AIBT.Authoring`):
  `IReferenceLeafBehaviorProvider` pairs a project's `NodeManifest` with an
  `IReferenceLeafBehavior` factory -- the discovery-facing shape, mirroring `ICustomMcpToolProvider`
  (`P6-010`).
- `Authoring/Registry/NodeRegistryBuilder.cs`: new `AddProjectExtension(NodeManifest, IReferenceLeafBehavior)`
  attaches a real handler binding under the existing `NodeManifestSource.UserExtension` source and
  stashes the behavior in a new `TryGetProjectLeafBehavior` side-table. `ValidateBinding`'s
  `UserExtension` case changes from "any binding is an error" to "binding is optional; a present one
  is validated exactly like a built-in/fixture binding" -- `AddUserExtension` itself is **untouched**
  and still always attaches `null`, so the ADR's own "unchanged negative test"
  (`NodeRegistryBuilderTests.UserExtension_IsUnboundAndAdvertisedAsCapability`) passes unmodified.
- `MCP/Authoring/ProjectLeafExtensionDiscovery.cs` (new, `AIBT.Mcp.Authoring`, Editor-only):
  `DiscoverViaTypeCache` mirrors `CustomMcpToolProviderDiscovery`'s own P6-010 split exactly
  (`UnityEditor.TypeCache.GetTypesDerivedFrom<IReferenceLeafBehaviorProvider>()`, skip
  abstract/interface/no-parameterless-ctor, per-instantiation try/catch isolation).
  `BuildWithBuiltInsAndProjectExtensions` folds discovered providers into
  `NodeRegistryBuilder.CreateWithBuiltIns()` and degrades to a built-ins-only build if the combined
  build itself fails validation (a malformed project registration must not break discovery for the
  built-in catalog).
- `MCP/McpToolDispatcher.cs`: `SearchNodes`, `GetNodeContract`, and `GetProjectManifest` (all three
  bare `NodeRegistryBuilder.CreateWithBuiltIns().Build()` call sites in this file) now call
  `ProjectLeafExtensionDiscovery.BuildWithBuiltInsAndProjectExtensions()` instead. Every other
  `CreateWithBuiltIns()` call site (`McpVerificationToolDispatcher.cs`,
  `McpAuthoringToolDispatcher.cs`, `McpDocumentationRegenerateCommand.cs`, and -- per this card's own
  Forbidden-changes clause -- `Authoring/Execution/ReferencePreviewDriver.cs`/
  `ReferencePreviewFixtureEnvironment.cs`, `P6-007`/`P6-008`) is untouched.

## A real regression this card's own live verification caught and fixed

`UnityEditor.TypeCache` scans every loaded assembly, test assemblies included. The first version of
this card's own proof test defined its project-style `IReferenceLeafBehaviorProvider` fixture with
an implicit public parameterless constructor -- which got itself live-discovered by
`ProjectLeafExtensionDiscovery`, breaking the pre-existing
`McpToolDispatcherTests.ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog` (it started seeing 12
entries instead of 11 in a supposedly clean environment). Fixed by giving the test fixture an
explicit `internal` constructor (blocks `Type.GetConstructor(Type.EmptyTypes)`'s default
public-only binding, so discovery skips it) -- the fixture is only ever constructed directly within
its own test file; TypeCache-based discovery itself is proven separately, by a test that supplies
its own explicit provider list rather than relying on a live scan. Full regression was re-run after
the fix and confirmed clean (see Verification).

## Verification

```text
Live Unity MCP run_tests (EditMode), ProjectLeafRegistrationTests (new,
  Tests/Editor/NodeRegistry/): 4/4 passing
  - ProjectExtension_AttachesABindingAndIsDiscoverableThroughTheSameRegistry
  - AddUserExtension_StillNeverAttachesABinding_UnchangedFromBeforeThisCard
  - ProjectRegisteredLeaf_TicksCorrectlyThroughTheRealUnmodifiedMachine (a project-style leaf,
    defined using only the new public contract, ticked through a real, unmodified
    ReferenceExecutionMachine for a full three-tick lifecycle)
  - McpDiscoveryCombination_FoldsAProjectProviderIntoTheBuiltInRegistry
Live Unity MCP run_tests (EditMode), full suite: 1592 total, 1589 passed, 3 failed -- all 3 failures
  pre-existing and unrelated to this card (2x AIBT.Tests.CodeGen.Generation.
  GeneratedArtifactContractTests, "The CodeGen test assembly must belong to the AIBT package" --
  a package-path environment assumption; 1x LocalSaveSystem.Tests.SaveStoreTests.
  SaveStore_AutoSave_WritesToDisk -- an unrelated non-AIBT package). Re-run after the TypeCache-
  contamination fix above; the failure count dropped from 4 to 3 (McpToolDispatcherTests.
  ZeroCustomNodesReturnsExactlyThePhase1BuiltInCatalog now passes).
Live Unity MCP execute_code, calling the real production AIBT.Mcp.McpToolDispatcher.Dispatch
  directly (exactly the entry point MCP~/Server/ relays into) against a real temporary script added
  outside AIBT/, in a default auto-referenced assembly, implementing only the new public contract
  (Assets/_P7008LiveVerification/, deleted afterward -- disposable, matching this project's own
  spike-then-delete convention):
  - search_nodes with no keyword: 12 entries (11 built-ins + the live-registered node), correctly
    ordered.
  - search_nodes keyword "p7008": 1 entry, the live-registered node.
  - get_node_contract for the live-registered typeId: found=true, full manifest returned.
  - All three calls succeeded in the same live Editor session the registration's own domain reload
    produced -- no additional domain reload needed between registration and discovery.
  - After deleting the temporary script and recompiling: search_nodes with no keyword returns
    exactly 11 entries again (the built-in catalog, unchanged) -- confirms no residue.
Tools~/Verification/Verify-Static.ps1 -- passed (121 work items, 6 schemas)
git diff --check -- clean
```

`Run-UnityTests.ps1 -Mode EditMode -Scope Full` (this card's own listed Required verification
command) was not additionally invoked as an external process -- the live Unity MCP `run_tests` call
above covers the exact same EditMode suite via the already-open Editor instance.

## Scope and limitations

- Reference-executor backend only, per the ADR's own scope -- no change to the Burst/native leaf-node
  authoring path (`AibtBurstNode` et al.). Native per-project leaves remain a separate, undecided
  question.
- No async-operation support on the public leaf context in this v1 (see Implementation).
- `P3-009`/`P6-007`/`P6-008` still build their own registries from the fixed Phase 1 fixture/built-in
  set, unchanged, per this card's own Forbidden-changes clause -- migrating them onto the new public
  surface remains a future, dedicated follow-up (per the card's own Handoff notes).
- `McpVerificationToolDispatcher.cs`/`McpAuthoringToolDispatcher.cs`/
  `McpDocumentationRegenerateCommand.cs` still build a built-ins-only registry -- only the two
  discovery tools named in this card's acceptance criteria (`aibt_search_nodes`/
  `aibt_get_node_contract`) plus `get_project_manifest` (same file, same root cause) were wired to
  see project extensions.
