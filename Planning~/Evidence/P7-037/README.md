# P7-037 evidence

Status: mandatory lifecycle/dispatch proof passed; production promotion waits on AIBT-038.

## Fresh audit findings

- `ProductionTreeHost.DispatchRequest` contains node index, phase, update/time and lifecycle reasons,
  but no config, instance-memory, blackboard, completion, transaction or command storage.
- `NativeBurstDispatchWorkspaceOwnerV2` requires exact per-request storage views;
  `NativeBurstDispatchBatchOwnerV2` owns grouped copies and can execute generated batches, but exposes
  only test-oriented individual result readers rather than an atomic production commit view.
- Generated catalog facades expose static `ExecuteImmediate`/`Schedule`. No common retained executor
  type exists for a production coordinator, and reflection is confined to the MCP test harness.
- `GeneratedBurstDispatchPrebindingV2.CatalogPlan` already derives the exact handshake, cases, fields,
  bindings and canonical rules from generated shard metadata. It is the correct build-time authority;
  caller-authored offsets are unnecessary.
- `GeneratedCompiledProgramV2.SemanticProgram` already contains the generated node identities and
  final config/memory offsets. The missing work is catalog identity/bootstrap and state commit, not a
  second tree compiler.

## Disposable proof

`Tests/Integration/NativeRuntime/GeneratedDispatchLifecycleProofTests.cs` currently:

1. materializes the real `GenerationShard` emitted by the packaged source generator;
2. parses the existing canonical `generated-tree-v2.aibt.json` fixture;
3. compiles it through `GeneratedCompiledProgramV2Compiler`;
4. derives all dispatch layout and offsets from compiler/generated metadata;
5. drives Enter/Tick/Exit through one real lifecycle machine using both generated immediate and
   scheduled facades;
6. compares root status, callback phases and complete post-lifecycle instance memory, and checks at
   the Tick commit boundary that the callback wrote `Count=38` after reading the bound value 37.

The first Unity run failed closed with `RegistryMismatch`. That exposed a production blocker in
`GeneratedBurstDispatchPrebindingV2`: it hashed every Authoring built-in, including
`aibt.stdlib.*`, while the source-generated facade correctly hashes the frozen `aibt.core.*`
runtime authority plus the selected generated shards. Prebinding now uses the same core authority,
checks canonical and numeric collisions, and the generated facade accepts the derived handshake.

The proof is deliberately test-local and currently uses a copied live blackboard value. It does not
yet prove production blackboard ownership, writes, command reduction, multi-instance batching or
failure recovery. Those remain mandatory before P7-037 can be Done.

## Verification

- `dotnet build AIBT.Integration.Tests.csproj --no-restore`: passed with 0 compiler errors. Existing
  Unity assembly-reference version warnings remain.
- Live Unity EditMode proof through MCP: 1/1 passed, including a real scheduled `JobHandle`.
- Generated prebinding catalog-plan regression through Unity MCP: 1/1 passed.
- Runtime core-authority contract through Unity MCP: 1/1 passed.
- A combined multi-assembly Unity job did not start within its 120-second initialization timeout;
  the three focused tests above were run separately and passed.
- `Verify-Static.ps1`: passed; 142 work items.
- `git diff --check`: passed.

## Proposed production contract

ADR AIBT-038 records the concrete generated executor, explicit catalog ownership, compiled runtime
definition and atomic dispatch-wave commit model revealed by the proof. Production implementation
waits on that public semantic decision, not on the disposable test mechanics.
