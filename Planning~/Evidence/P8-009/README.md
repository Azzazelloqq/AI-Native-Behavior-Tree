# P8-009 consumer workflow handoff

## Outcome

Implementation is complete on `feature/aibt-consumer-workflow`; no commit/tag/publish was requested.
The task remains in Review because Unity 6000.5.8f1 and an independent fresh-user walkthrough are
not available in this session. This is not a 1.0 or platform-support promotion.

- Selective asset opening handles AIBT source paths and declines unrelated assets, including the
  EntityId callback on Unity 6000.7. The previously local commented handler was explicitly in scope.
- `Prepare Runtime` compiles against a selected generated catalog's shards and saves a versioned,
  catalog-pinned `ProductionTreeAsset`. A shared explicitly owned `ProductionTreeProgram` bootstraps
  hosts through the existing production adapter, without runtime semantic JSON compilation/reflection.
- Finite Float32 configuration works through manifest/schema, generator, reference/generated packing,
  ABI v2 bridge and Inspector. Existing enum values and existing-contract bytes are preserved.
- Preview and verified native trace annotate the same authoring graph, preserve its node objects and
  positions, and detach on document changes. Port connections reparent atomically and append children
  in explicit semantic order. Document/layout history remains separate.
- `Stop()` exposes the host's existing idempotent permanent cancellation/disposal path. The new First
  Agent sample demonstrates the complete Editor-to-Player path and explicit shared ownership.
- Narrow profiler markers and a reproducible bounded Player probe attribute input, VM, immediate
  dispatch, trace and effect-delivery costs without changing scheduling behavior.

## Verification

See [machine-readable summary](verification.json), [public API dump](public-api.txt) and
[Player report](Player/README.md).

- Unity **6000.7.0b1** clean `file:` UPM consumer, full EditMode suite: **1497/1497**, zero skipped.
  The clean project intentionally imports no optional samples; discovery baseline tests require that.
- After final Editor attachment refinements, consumer/graph/trace regression: **45/45**. This includes
  the actual rendered EditorWindow tests; graphics were enabled. An earlier `-nographics` run exposed
  its unsuitability for those tests and is not reported as acceptance.
- Pre-change source `567a29c` plus the exact pre-existing local commented importer: **1481/1481** on
  the same Unity toolchain. Both baseline and full new suite report the identical 112 persistent native
  allocations on shutdown. The new consumer/UI subset and actual Player do not report that warning.
  This distinguishes the pre-existing full-suite issue; it is not an assertion that the entire library
  has no possible native leak.
- Source generator rebuilt to the packaged binary's exact SHA-256; standalone Roslyn matrix passed
  **1415 assertions**, including Float32 node/catalog generation. The verifier's stale surrogate type
  shapes were updated for the already-existing production catalog/executor seam; Unity tests continue
  to validate the real registry/authority handshake.
- ABI v1 fixture is unchanged. ABI v2 adds exactly its Float32 configuration reader; existing ABI
  signatures/layout exceptions are still independently pinned. The accepted full public API baseline
  has **zero removals**. The 213 additions since that older baseline also include earlier P8 work.
- Generated public API reference refreshed from the loaded production assemblies; full documentation
  drift and member-coverage tests passed. Static/schema verification and diff whitespace checks passed.
- Windows x64 **Development IL2CPP**, Burst enabled: build succeeded with zero errors. The actual
  serialized runtime asset moved the sample agent to its goal, six population/policy cells passed,
  changed facts reached Success, and hosts/catalogs shut down cleanly. See the diagnostic limits.

## Corrections made during verification

The archive reader now explicitly catches `InvalidDataException` for malformed table counts.
The sample reloads its persistent prepared asset after creating a new scene before assigning it;
the first Player attempt exposed the missing serialized reference. Neither failing attempt is counted
as accepted Player evidence.

The graphics-enabled window tests establish behavior programmatically. Computer Use was attempted
for a separate interactive check, but its app/window inventories returned no targetable verification
window. Manual mouse interaction and an independent newcomer walkthrough are not claimed.

## Reproduction and boundaries

- Use `Tools~/Verification/Run-UnityTests.ps1` with the installed Unity executable, a clean consumer
  project's path, EditMode, Full scope and `-EnableGraphics` for rendered window tests. The additive
  switch preserves the existing headless default. The accepted run uses a graphics-enabled Editor.
- Run `Tools~/Verification/Verify-Static.ps1` and the standalone CodeGen verifier. This workstation has
  .NET 8 in the system SDK location; Unity's bundled SDK hosts .NET 10, so the verifier was run with the
  system `dotnet`, not Unity's apphost. Analyzer compilation used Unity's bundled SDK.
- Run `Tools~/Verification/P8/Run-ConsumerWorkflow.ps1` for the isolated Player path.
- Runtime assets are explicit compiled snapshots. Re-prepare after source/catalog edits and restart
  hosts. No automatic hot reload, pooling, new Agent/Shared authoring or scheduler default was added.
- Unchanged concerns: the exact older Unity baseline, broader platform verification, rich managed-node
  preview providers for native-only custom nodes, and separate optimization of production dispatch.

Recommended next acceptance: follow [First production agent](../../../Documentation~/consumer-quickstart.md)
in a new project, then profile a real game tree. Preserve measured latency and work counts when choosing
between Immediate and Jobs policies.
