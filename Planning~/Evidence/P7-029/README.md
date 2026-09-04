# P7-029 verification

Completed 2026-09-04 after owner approval of the [implementation plan](implementation-proposal.md).
Base commit: `fa14161`; the previously verified, uncommitted P7-030 work remains present.
No commit or push. [Machine-readable results](verification-results.json).

## Implementation

- DocumentMigrator uses the existing full constructors, preserving blackboard, description,
  revision, Agent/Shared contracts, tags, metadata and node bindings. Only the migration rule's
  parameters and node version change. No-change paths retain the original document object.
- Native migration copies CooldownInitialized by stable NodeId in the same eligibility branch
  as instance memory. Excluded/incompatible nodes retain fresh state.
- A structurally changed active composite queues cancellation of its old descendant path on the
  fresh machine. The existing native cancellation/composite-reset path delivers the callbacks;
  migration does not fabricate successful acknowledgements or modify the old instance.
  The outermost changed owner handles nested changes once. Terminal-pending children retain
  their actual Exit reason; their old result does not advance the new cursor.
- The new internal helper is confined to NativeLifecycleMachineV1 and uses the existing
  descendant cancellation/reset mechanism. Public APIs, node ABI, ownership IDs, classification
  rules and Runtime assembly dependencies are unchanged. Active-instance migration is retained.

The accepted callback sequence is recorded in ADR-P7-011's P7-029 addendum.

## Automated verification

- Added 19 behavior tests: four document-preservation/no-rule cases and fifteen native cases.
  Coverage includes both active child positions, unchanged order, nested Sequence, two changed
  levels, terminal Success/Failure awaiting Exit, cooldown at 20/110, excluded cooldown and
  replacement with a different node type. Document tests cover canonical v1/v2 data, real scoped
  keys/contracts, bindings, revision and source immutability.
- Pre-fix red run `4e46df151f014358a02f767bd2af9196`: 9/13 passed, with key-preservation and reorder
  callback expectations failing. The v2 fixture was subsequently made canonical-valid with real
  Agent/Shared entries before the final verification; no preservation expectation was relaxed.
- Focused migration/native lifecycle/decorator/host regression: **90/90 passed**, job
  `8ce840e08de644c59d066d6e7697b772`.
- Full host-project EditMode: **1685/1688 passed, 3 failed, 0 skipped**, job
  `6ba39022dbeb4847a56fa2724bbc2957`. AIBT alone: **1318/1320 passed**.
  The two GeneratedArtifactContractTests fail the existing Assets-layout PackageInfo assertion;
  LocalSaveSystem autosave expects 9 and reads 0. Exact names/messages are in JSON and match
  the preceding P7-030 run. These files were not modified by P7-029.
- The full run lost MCP callbacks during a domain reload. Counts/failures were read from the
  fresh `C:/Users/User/AppData/LocalLow/DefaultCompany/Modules/TestResults.xml`, timestamped
  13:05:38–13:06:19 UTC, before clearing the orphaned MCP job. Zero-progress tool status was
  not interpreted as a test result.
- Unity script compilation passed. Verify-Static.ps1 passed: **7 schemas, 137 work items**.
  git diff --check passed. No generated public API update is needed for an internal helper.

## Separate live probes

The two adjacent `*-live-probe.cs.txt` files are C# method bodies executed through Unity MCP's
Roslyn execute_code in the open Unity 6000.5.8f1 Windows Editor. They invoke the real migrator,
native machine and callback-completion protocol outside NUnit test execution. Reflection accesses
internal native types and existing fixture builders; it is probe-only, not production code.
Every native instance is disposed in finally blocks.

- Sequence initially produces a.Enter, a.Tick(Running). After `(a,b) -> (b,a)`, the continuation is
  **a.Abort(HotReload), a.Exit(Aborted), b.Enter, b.Tick, b.Exit(Success), a.Enter, a.Tick,
  a.Exit(Success)**, followed by terminal root Success.
- Cooldown started at 10 with duration 100 retains initialized flag 1 after unchanged migration.
  At 20: Failure and no child callbacks. At 110: Enter/Tick/Exit and Success.
- v1 document retains one blackboard key and compiles successfully after migration. v2 retains
  all three Tree/Agent/Shared keys, both scope contracts, bindings, revision 23 and descriptions;
  canonical serialization/parse succeeds. Source bytes remain unchanged in both cases.
  The v2 probe does not claim generated-binding compilation.

## Remaining limits

ParallelBranches, suspended parallel frames and their control bookkeeping remain a separate,
previously disclosed migration gap; these fixes do not certify arbitrary parallel/observer/budget
state migration. The preparation inventory is in the accepted proposal. No population/host hot
reload integration or general dispatch adapter was added.

No Standalone, Android or Web build was run. This is migration correctness evidence in the Editor,
not a new Player or zero-GC performance claim. The added helper executes during migration setup;
the ordinary per-Tick path is unchanged. P7-031 and P7-032 remain separate work.
