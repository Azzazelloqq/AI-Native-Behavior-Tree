# P7-031 remaining recipe blockers: proposed scope extension

Status: Accepted by owner on 2026-09-04, including commits and autonomous continuation. These are pre-existing P7-009 integration defects
found by P7-031's required real TCP recipe, not failures of the new compile-attempt tracker.

## Evidence

1. `GenericNodeDispatchRunner.cs:62` passes Allocator.Temp to the translator from the bridge's
   background thread. TCP test_node returned AIBT9013: "Could not allocate native memory ...
   managed thread outside of a job ... Allocator.Persistent or Allocator.TempJob." The same
   request on the Editor main thread returned valid=true, dispatchProven=true, Tick Success.
2. Apply successfully moved the node/tests/asmdef into Assets/P731-LiveVerification. Its temporary
   Catalog/LiveConditionNodeShardCatalog.cs remained in staging and failed the following compile
   with AIBT5011 and CS0246, because its selected shard had moved to another assembly. After
   removing only the owned staging fixture, the applied assembly loaded its node and shard.

See live-verification.json. All owned source fixtures were subsequently removed.

## Proposed bounded implementation

1. Add a real background-thread regression for native test dispatch, with an actual supported
   generated artifact, deterministic result assertions and disposal on success/failure.
2. Use a background-thread-compatible allocator for the translator's owned shape and preserve
   existing disposal. Check all allocations in that path; no transport-wide scheduling layer.
   File: MCP/NodeDevelopment/GenericNodeDispatchRunner.cs, plus focused tests.
3. Add an apply behavior test requiring the temporary companion catalog to be removed after a
   successful move, while invalid destinations/hash/registry refusal preserve the whole staging
   generation. Do not move the verification catalog into the destination assembly.
4. Remove the staging-only companion after successful apply. Validate the complete affected
   source path against reparse ancestry before mutation. Preserve existing assembly layout,
   content-hash and registry checks. File: StagingSlot.cs and focused tests.
5. Re-run the complete TCP generate/preview/tests/start/check/test/apply sequence including the
   post-apply compile, then focused/full EditMode and static verification. Only then mark Done.

This extends the approved file/scope boundary to GenericNodeDispatchRunner and staging companion
cleanup. The current card forbids unrelated packaging changes; no such change has been made.
No new node behavior, CodeGen redesign, generic queue, runtime change, commit or push is proposed.
