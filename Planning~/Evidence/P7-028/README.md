# P7-028 — Production-ready built-in node library

## What shipped

Two new built-in leaf nodes, each with both a real native Burst execution path and a real
reference-executor path:

- `aibt.stdlib.wait` (Action) — remains `Running` for a configured `ticks` count, then succeeds.
- `aibt.stdlib.random-condition` (Condition) — succeeds with a configured `success-chance-percent`
  probability, drawn from the native side's real per-instance deterministic Burst random stream and
  a `System.Random` instance on the reference side (disclosed as non-bit-identical between the two
  backends — see the manifest's own `whenNotToUse` text).

Files:

- `Authoring/Registry/Generated/BuiltInLeaves/Runtime/BuiltInLeafNodes.cs` (new) — the two
  `[AibtBurstNode]` structs, real native execution.
- `Authoring/Registry/Generated/BuiltInLeaves/Runtime/AIBT.Authoring.BuiltInLeaves.asmdef` (new).
- `Authoring/Registry/Generated/BuiltInLeaves/Catalog/BuiltInLeafCatalog.cs` (new) — the
  `[AibtCatalogSet]` that makes the shard's nodes real, compiled, native-executable output.
- `Authoring/Registry/Generated/BuiltInLeaves/Catalog/AIBT.Authoring.BuiltInLeaves.Catalog.asmdef`
  (new) — references `Unity.Burst` directly, mirroring `Samples~/BurstNodes/Catalog/`'s own
  already-established pattern (the generated catalog facade requires `[BurstCompile]`).
- `Authoring/Model/Nodes/BuiltInLeafManifests.cs` (new) — the two reference-side
  `IReferenceLeafBehaviorProvider` implementations.
- `Authoring/Registry/NodeRegistryBuilder.cs` — new `AddBuiltInLeaf` method (source `BuiltIn`, real
  reference handler binding); `CreateWithBuiltIns()` folds in `BuiltInLeafManifests.All`;
  `ValidateSource`'s `BuiltIn` case now accepts `aibt.stdlib.` alongside `aibt.core.`.
- `Authoring/Registry/Generated/RuntimeBuiltInCatalogAuthorityVerifier.cs` — narrowed to rebuild only
  the `aibt.core.` subset of the live registry (see "Two architectural walls" below for why).
- `Tests/Editor/NodeRegistry/BuiltInLeafManifestsTests.cs` (new, 4 tests).
- Golden-fixture updates for the now-13-entry built-in registry: `Tests/Editor/NodeRegistry/
  NodeRegistryHashTests.cs`, `NodeRegistryBuilderTests.cs`, `Tests/Editor/NodeRegistry/
  ProjectLeafRegistrationTests.cs`, `Tests/Editor/Mcp/Discovery/McpToolDispatcherTests.cs`,
  `Tests/Fixtures/Trees/Compilation/minimal-compiled-v1.golden.json`.
- `Documentation~/generated/` — regenerated via the real `AIBT/MCP/Regenerate Documentation` menu
  command.

## Two architectural walls found mid-implementation (both put to the owner)

**1. A native blackboard-reading leaf cannot have a working reference-executor counterpart today.**
The only blackboard-access mechanism available to an `[AibtBurstNode]` struct is a `GeneratedHandle`
config field bound via `[AibtBlackboardBinding]`. The reference compiler
(`ReferenceCompiler.BuildBlackboardSlots`/`BuildNodeTables`) has no support for `GeneratedHandle`
fields at all — it only resolves blackboard access through literal `NodeManifest.Reads` key-name
strings. A manifest shaped to satisfy the native side's own compile-time canonical-JSON parity
requirement (below) would therefore have an empty `Reads` array, and the reference-side behavior
could never actually observe the bound value — it would always report `Failure`, non-functional by
construction. Confirmed live by reading the shard's own `AibtGeneratedMetadata`-generated JSON for
a trial `aibt.core.blackboard-bool-condition` node. Owner chose to drop this node from the card's
scope rather than either build the reference-compiler feature this would require, or ship a
reference-only leaf (breaking the "both mechanisms" decision for just this one node).

**2. `aibt.core.` is permanently locked against any new native-Burst-declared node.**
`AIBT.CodeGen`'s `BurstNodeGenerator` (diagnostic `AIBT5012`, "Catalog handshake mismatch") checks
every `aibt.core.`-prefixed manifest a live `[AibtCatalogSet]` shard declares against
`RuntimeBuiltInCatalogAuthority` — a compile-time-embedded, permanently frozen snapshot of only the
original 11 structural composites/decorators (the ones with no shard at all). Two outcomes were
tried, both fail:
- Leave the authority untouched → the shard's own `aibt.core.wait`/`aibt.core.random-condition`
  claims are rejected: "A public shard uses the Runtime built-in namespace."
- Amend the authority to include the new nodes → the generator's registry-merge step
  (`builtIns + shardManifests`) now finds the same type ID in both lists → "Complete registry
  contains a duplicate or incompatible node identity."
There is no way through this generator pathway for a new `aibt.core.*` Burst-declared node — the
namespace is permanently reserved for the original 11. `RuntimeBuiltInCatalogAuthorityTests`
(exercising the same generator-facing verifier) independently confirms this: it asserts the frozen
authority's JSON is byte-identical to `NodeRegistryBuilder.CreateWithBuiltIns()`'s own canonical
output, which only holds if the authority tracks exactly (and only) the `aibt.core.` subset of the
live registry — `RuntimeBuiltInCatalogAuthorityVerifier.RebuildRegistry()` (renamed
`RebuildAuthorityEntries()`) was narrowed accordingly. Owner chose a new namespace, `aibt.stdlib.`,
for always-on built-in leaves that do carry a real native declaration — outside this frozen gate
entirely, since the generator's `aibt.core.` prefix check does not apply to it.

## Compile-time canonical-JSON parity requirement (the mechanism behind wall #2)

Once a node claims `aibt.core.`/is checked by the generator at all, its reference-side `NodeManifest`
(read by `NodeRegistryBuilder.CreateWithBuiltIns()`) and its native `[AibtBurstNode]`-derived manifest
(emitted by the generator into the shard's own `AibtGeneratedMetadata.ManifestRegistryJson`, readable
live via reflection) must serialize to **byte-identical** canonical JSON — not merely "the same
observable contract," which is what this card's own plan originally assumed. This drove several
concrete corrections during implementation: `whenToUse`/`whenNotToUse`/`summary`/`category` text
copied verbatim from the `[AibtNodeDocumentation]` attribute; parameter names matched exactly
(`success-chance-percent`, kebab-case, not the camelCase first attempted); no `minimum` on either
parameter (the native side has no such concept, so the reference-side `NodeParameterContract`
constructor's `minimum` argument had to be omitted, not defaulted); `WaitMemory`'s real reflected
size (4 bytes, one `UInt32` field) mirrored in the reference manifest's own `NodeMemoryDescriptor`
rather than assumed zero; and the auto-derived single example (title = the doc attribute's slug,
parameters = zero-valued, `expectedBehavior` = the summary text repeated) reproduced exactly.
`BuiltInLeafManifestsTests.ReferenceManifests_MatchTheNativeShardsOwnGeneratedCanonicalJsonExactly`
locks this down going forward by reading the shard's live generated JSON via reflection and comparing
it against the reference registry's own serialization, rather than re-typing either by hand.

## Live verification

- `Verify-Static.ps1`: passed, 7 schemas, 137 work items.
- `AIBT.Editor.Tests` (live `run_tests`, full assembly): 396/396 passed (392 baseline + 4 new).
- Whole host-project regression (live `run_tests`, no assembly filter, ~1653 tests across every
  package in `Modules`): the same 3 pre-existing, unrelated failures already disclosed in `P7-018`'s
  own evidence (2 `GeneratedArtifactContractTests` host-layout failures, 1 unrelated
  `LocalSaveSystem.Tests.SaveStoreTests` failure) — zero regressions attributable to this card.
- `Get-FullPublicApi.ps1 -BaselinePath Tools~/Verification/P7/Audit/Baseline/public-api-baseline.txt`:
  passed, purely additive — 10 new public members, zero removals or renames.
- Live proof, real `McpVerificationToolDispatcher.Validate`/`.Compile` (via reflection, matching the
  real MCP `validate`/`compile` tool bodies exactly) against a real tree file scanned from the actual
  project, using `aibt.stdlib.wait`, validated against the project's own real, already-adopted
  `.aibt/policy.json`: `valid: true`, `success: true`, zero diagnostics. (`random-condition` was
  proven separately, deterministically, in `BuiltInLeafManifestsTests`, since the project's own
  policy forbids non-deterministic nodes by default — proving it live against that same policy would
  require a policy change out of scope here.)
- Live proof, native side: `BuiltInLeafCatalog.IsUsable == true` (reflection), confirming the
  `[AibtCatalogSet]` compiled and the generator accepted both `aibt.stdlib.*` declarations as real,
  usable native output.
