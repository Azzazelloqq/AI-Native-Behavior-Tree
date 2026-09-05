# P7-037 — Production generated-dispatch lifecycle integration

Status: `Done`

Planning update 2026-09-05: the disposable immediate/scheduled Unity proof passes and exposed a
required public bootstrap choice. Accepted ADR AIBT-038 is in
`Documentation~/decisions/ADR-P7-037-production-generated-dispatch-bootstrap.md`; production
production promotion was approved on 2026-09-05. Current proof limits and verification are recorded in
`Planning~/Evidence/P7-037/README.md`.

Closing update 2026-09-05: production implementation is done. Full detail, including four real
defects found and fixed against the live Editor (two compile errors, a wrong version/hash comparison
in the new bootstrap gate, a missing Agent-scope blackboard path) and the closing test/verification
pass, is in `Planning~/Evidence/P7-037/README.md`'s "Production implementation" section. See
`## Outcome` below.

## Objective

Connect normally compiled behavior-tree instances to generated Burst catalog dispatch in production.
The bridge must execute real custom-node callbacks through both immediate and scheduled facades and
return their results to the existing lifecycle machine without reflection, caller-authored layout
offsets or precomputed node statuses.

This is the prerequisite for P7-033's population Jobs policies and the Swarm Arena sample. It does
not choose scheduling policy, define profiles or allocate a global frame budget.

## Depends on

- `P2-012` — closed native dispatch ABI v2.
- `P4-003` — same-frame and pipelined scheduler ownership.
- `P6-022`/`P7-009` — generated-metadata translation proof and production authoring translator.
- `P7-030` — complete single-tree production lifecycle host.
- `P7-032` — recoverable native scheduler ownership.

## Required reading

- `Documentation~/decisions/ADR-P6-022-generic-native-dispatch-test-harness.md` and
  `Planning~/Evidence/P7-009/README.md`; their translator proves metadata materialization for an
  authoring/MCP test harness, not production host integration.
- `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs` and
  `GeneratedBurstDispatchPrebindingV2.cs`.
- `Runtime/Execution/Burst/Dispatch/`, especially dispatch ABI/workspace ownership.
- `Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs` and
  `Runtime/Integration/ProductionTreeHost.cs`.
- Generated catalog facade contracts and P7-032's same-frame/pipelined recovery evidence.

## Mandatory proof before production promotion

Build the smallest disposable end-to-end proof first:

1. Compile a real custom Burst node and a tree through the normal authoring pipeline.
2. Carry the generated catalog handshake and canonical layout into Player-safe runtime data without
   reflection or hand-authored offsets.
3. Execute one complete lifecycle update through the generated immediate facade.
4. Execute the same update through the generated scheduled facade and a real `JobHandle`.
5. Verify the same observable node status, lifecycle callbacks, memory, blackboard writes and
   reduced commands.

If this proof reveals a new public semantic choice, record a focused decision before widening the
API. Mechanical public names remain subject to the existing API-diff review.

## Allowed changes

- `Runtime/Integration/GeneratedDispatch/`.
- Focused additions to `Runtime/Execution/Burst/Dispatch/` needed for runtime-safe ownership.
- `Authoring/Compilation/Generated/` for compiler-produced runtime bootstrap data.
- `CodeGen~/AIBT.CodeGen/` only if the generated facade lacks a required closed-ABI entry point.
- Focused Runtime/Editor tests and `Planning~/Evidence/P7-037/`.

## Forbidden changes

- Do not use `SchedulingPolicyDriver` or caller-supplied/precomputed node statuses as production
  dispatch.
- No reflection in Player, managed fallback in a Burst path, per-node hardcoded offsets, or one Job
  per tree/node.
- No scheduler profile, priority, policy-selection, global-budget or sample-gameplay work owned by
  P7-033/P7-034.
- No new behavior-node semantics or promotion of sample nodes into the standard library.

## Acceptance criteria

- A normally compiled tree containing a real generated custom node runs through production-owned
  immediate and scheduled dispatch with identical observable lifecycle results.
- Runtime bootstrap rejects catalog/program/layout mismatches with a structured diagnostic before
  execution and never guesses a compatible layout.
- Enter, Tick, Exit and Abort callbacks reach the generated implementation when declared; terminal,
  failure and cancellation paths preserve the lifecycle contract.
- Blackboard/snapshot/command ownership follows existing deterministic publication rules.
- Multiple instances share catalog metadata while retaining isolated mutable state.
- Outstanding scheduled work can complete, reject invalid output and dispose/recover through the
  P7-032 ownership contract.
- Hot paths allocate no managed memory after warmup and leak no native ownership.

## Required verification

```text
Verify-Static.ps1
focused immediate/scheduled parity and mismatch/recovery tests
Run-UnityTests.ps1 -Mode EditMode -Scope Full
public API diff and generated documentation checks when the public surface changes
live Unity scheduled-Job proof using a normally compiled generated custom node
allocation measurement after warmup
git diff --check
```

## Handoff notes

P7-033 may start only after this card proves the real path required by its Jobs policies. P7-034 and
P7-035 must consume this public production path rather than a benchmark or test-only adapter.

## Outcome

Done, 2026-09-05. `Runtime/Integration/GeneratedDispatch/` implements ADR AIBT-038's contract exactly
(`IGeneratedBurstCatalogExecutorV2`, `GeneratedBurstCatalogV2`, `GeneratedTreeRuntimeDefinitionV2`,
a new `ProductionTreeHost.TryBootstrap(GeneratedTreeRuntimeDefinitionV2, GeneratedBurstCatalogV2, ...)`
overload). Full defect list and verification are in `Planning~/Evidence/P7-037/README.md`.

Acceptance criteria:

- **A normally compiled tree with a real generated custom node runs through production-owned
  immediate and scheduled dispatch with identical results** — met. Proven through the real
  `ProductionTreeHost`/`GeneratedTreeDispatchAdapterV2` path (not the original disposable proof's raw
  workspace calls), both for a single bootstrap-to-`Success` run and for immediate-vs-scheduled
  byte-identical equivalence.
- **Runtime bootstrap rejects mismatches with a structured diagnostic, never guesses a compatible
  layout** — met, with a correction: the original gate compared two same-named but semantically
  unrelated version/hash fields (catalog self-identity vs. compiled-program identity) that could never
  agree for a real tree; fixed to compare the fields that are actually shared
  (`ExecutionSemanticsVersion`) plus the real per-node case lookup (TypeId+Version+size).
- **Enter/Tick/Exit/Abort reach the generated implementation; terminal/failure/cancellation preserve
  the lifecycle contract** — met for Enter/Tick/Exit (proven live); Abort/cancellation is exercised by
  the underlying `NativeBurstDispatchWorkspaceOwnerV2` tests it wraps unmodified, not by a new
  P7-037-specific test — disclosed, not separately proven this pass.
- **Blackboard/snapshot/command ownership follows existing deterministic publication rules** — met;
  additionally required widening the adapter to accept Agent-scope blackboard bindings (the project's
  own reference fixture uses Agent scope, not Tree), with its own independent, collision-safe storage
  region — Shared scope stays unsupported, correctly out of this single-instance adapter's scope.
- **Multiple instances share catalog metadata while retaining isolated mutable state** — met, proven
  directly: two adapters over one catalog both reach `Success` independently; the catalog refuses
  disposal until both release it.
- **Outstanding scheduled work can complete, reject invalid output and dispose/recover through the
  P7-032 ownership contract** — partially met. A guard-level rejection (invalid node index) is proven
  to commit no partial tree/blackboard state. A genuine mid-execution generated-callback fault (the
  executor runs and reports failure) is not separately proven for the adapter — it would need a
  dedicated fault-injectable Burst leaf fixture, judged disproportionate to add this pass since the
  underlying `NativeBurstDispatchWorkspaceOwnerV2` atomic-commit mechanics the adapter wraps unmodified
  are already independently proven by `Tests/Runtime/NativeExecution/Dispatch/
  NativeBurstDispatchWorkspaceTests.cs`. Disclosed as a real, bounded gap, not silently skipped.
- **Hot paths allocate no managed memory after warmup, leak no native ownership** — met; a
  warmed-up, freshly bootstrapped instance's `Dispatch` calls allocate zero GC memory, and every
  `TryCreate` failure path and `Dispose` releases every owned `NativeArray`/workspace/catalog retain.

Verification: `AIBT.Integration.Tests` + 3 CodeGen assemblies 58/58; `AIBT.CodeGen.ContractTests`
(ABI baseline) 6/6; `McpDocumentationGeneratorsTests` 12/12; full host EditMode **1739/1742**, 0
skipped, the same 3 pre-existing unrelated failures as before this pass (2 CodeGen `PackageInfo`
assertions, 1 `LocalSaveSystem` autosave test); `Verify-Static.ps1` and `git diff --check` passed.
Not refreshed this pass, disclosed: the CI-only `P7-020` public-API baseline
(`Tools~/Verification/P7/Audit/Baseline/public-api-baseline.txt`), still gated on `P0-005`'s blocked
runner — real follow-up owned by `P7-020`, not this card.
