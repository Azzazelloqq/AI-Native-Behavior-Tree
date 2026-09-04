# P7-018 real Agent/Shared blackboard scope — evidence

## Result

Done. Reopened `ADR-P6-014`'s "not implemented, deferred" conclusion at the owner's explicit
request, narrowed down mid-planning to: make the MCP authoring/verification surface (which the
reference executor exclusively backs) actually usable for Agent/Shared blackboard scope, by fixing
one real, previously-unfixed compiler gap — without building new reference-executor storage
architecture and without touching the already-working native backend.

## Two real re-scopings, both before any code was written

1. **`P6-014`'s own bookkeeping drift.** Its task-card file was still `Status: Draft` despite its
   evidence being accepted 2026-08-31 and `work-items.json` already correctly recording it `done` —
   same class of drift `P7-016`'s gate fixed on four other cards. Fixed on sight.
2. **The card's original premise ("promote v2 to default unblocks Agent/Shared") contradicted
   `ADR-P6-014`'s own already-accepted decision.** `ADR-P6-014` found `ReferenceCompiler.cs`'s
   `AIBT3012` check and `ReferenceBlackboardStorage.cs`'s matching check both reject Agent/Shared
   scope *unconditionally* and decided not implemented, deferred. Since `TreeDocument.CreateVersion2`'s
   only real difference from v1 is the Agent/Shared scope contracts, promoting v2-to-default without
   real support would have been pure format churn. Put to the owner; they chose to reopen the
   deferral and commission real implementation.
3. **Investigating the actual implementation found the picture bigger than `ADR-P6-014` itself
   mapped.** `ReferenceBlackboardStorage.cs` (read in full) is architecturally a flat,
   single-tree-instance byte arena with no shared/cross-instance concept at all — building real
   Agent/Shared runtime support there means new storage architecture from scratch, needed by nobody
   today. Meanwhile `Authoring/Compilation/Generated/GeneratedScopeCompiler.cs` and `Runtime/
   Blackboard/Native/Shared/NativeSharedContextOwnerV1.cs` were found (by direct reading, not
   assumption) to **already fully implement** Agent/Shared blackboard scope for the **native**
   backend — unconditional compilation of Tree/Agent/Shared scope with real reduction semantics, and
   a complete runtime (contribution streams, reduction leases, multiple `TreeInstanceId` bindings to
   one shared context) — both already covered by passing test suites. Put to the owner again: build
   the missing reference-side storage from scratch, or wire up only the already-working native path.
   **The owner chose native-only.**

## What was actually missing

MCP's authoring/verification surface is deliberately, exclusively reference-executor-bound
(`McpVerificationToolDispatcher.cs`'s own doc comment: "no second validator/compiler/executor exists
here"). Even a project opting in via `.aibt/policy.json` couldn't compile an Agent/Shared document
through MCP, because `Authoring/Compilation/ReferenceCompiler.cs`'s own `BuildBlackboardSlots` scope
check was *unconditional* — never consulted `SupportsAgentScope`/`SupportsSharedScope` at all, unlike
`TreeValidator.ValidateBlackboardScope`, which already did.

## Implementation

- **`Authoring/Compilation/ReferenceCompiler.cs`**: `BuildBlackboardSlots`'s scope check made
  policy-aware, mirroring `TreeValidator`'s exact pattern (Tree always allowed; Agent/Shared allowed
  only when the compiling policy's own flag is `true`). Live-discovered while testing, not assumed:
  `ReferenceCompiler.Compile` already calls `TreeValidator.Validate` first, using the *same*
  policy-derived `ValidationOptions` — that pre-existing check already gates Agent/Shared correctly
  once a policy opts in (`AIBT2030` when it doesn't). This fix's own `AIBT3012` check is the
  *second* gate, reached only once validation already passes — exactly the two-layer shape
  `ADR-P6-014`'s own spike found ("the exact same opt-in policy that made `TreeValidator` accept the
  document still makes `ReferenceCompiler.Compile` fail").
- **`MCP/Authoring/McpAuthoringToolDispatcher.cs`**: `BuildRegistryAndOptions` now reads `.aibt/
  policy.json` (mirroring `McpVerificationToolDispatcher.Validate`'s already-established pattern)
  instead of hardcoding `ReferenceCompilationPolicy.Phase1` — a project without a policy file, or
  with both flags `false`, keeps today's exact behavior. `CreateTree` picks `TreeDocument
  .CreateVersion2` specifically when the document declares an Agent/Shared entry (matching
  `GeneratedScopeCompiler.cs`'s own already-enforced v2 requirement — `hasExtended &&
  formatVersion != 2` fails); an ordinary tree-scope-only document still writes v1, no blanket
  "default to v2".
- **`MCP/Verification/McpVerificationToolDispatcher.cs`**: `Compile` gets the same policy-read
  treatment (previously the one verification tool that didn't read policy, unlike `Validate`).
- **`MCP/Authoring/McpAuthoringJson.cs`**: `ReadBlackboardKey`/`WriteBlackboardKey` widened to
  accept `scope`/`default`/`reduction`; new local default-value and vector-shape (`{x,y,z,w}` object,
  not array — matched to the real canonical writer, not assumed) reader/writer mirroring
  `CanonicalTreeJson`/`CanonicalTreeJsonWriter`'s own (private) shapes exactly, scoped to the 15
  built-in scalar types this MCP surface already supports (not Enum32/Registered — unrelated,
  pre-existing, still-disclosed limitation) — matches `P6-014`'s own investigation-pass-1
  recommendation precisely. New `ReadScopeContract`/`WriteScopeContract` for `create_tree`'s own
  `agentContract`/`sharedContract` args.
- **`simulate` (`ReferencePreviewDriver`-backed) needed no code change.** It hardcodes
  `ReferenceCompilationPolicy.Phase1` (confirmed by reading `ReferencePreviewDriver.TryCreate`,
  never reads project policy), so it already, automatically fails an Agent/Shared document at
  `TreeValidator`'s own pre-existing `AIBT2030` check with a clear, specific diagnostic — the plan's
  own "give simulate a clear diagnostic" deliverable was satisfied for free by the compiler fix,
  proven live rather than assumed or built speculatively.
- **`ReferenceCompilationPolicy.Phase1`'s own shared default is untouched** (`SupportsAgentScope`/
  `SupportsSharedScope` both still `false`) — every other call site using it directly is unaffected.

## Verification

- `Verify-Static.ps1` — passed (7 schemas, 133 work items).
- `AIBT.Editor.Tests` full assembly (live `run_tests`): **392/392 passed**, 0 failed (up from 386
  baseline — 6 new tests, all passing).
- Whole host-project regression (live `run_tests`, no assembly filter — 1645 tests across every
  package in `Modules`, not just AIBT): **3 pre-existing, unrelated failures only** — 2 known
  `GeneratedArtifactContractTests` host-embedded-layout failures (same ones disclosed since
  `P7-016`'s gate, environment-dependent, not caused by this card) and 1 completely unrelated
  `LocalSaveSystem.Tests.SaveStoreTests` failure (a different package in the `Modules` host project,
  untouched by this card). Zero regressions attributable to this change.
- Native-path capability **reconfirmed live, not re-built**: `GeneratedScopeCompilerTests` 11/11,
  `AIBT.NativeSharedBlackboard.Tests` (`NativeSharedContextTests` etc.) 36/36 — both pre-existing,
  both still passing, proving the native Agent/Shared implementation this card deliberately did not
  touch still works exactly as before.
- New tests, all passing:
  - `ReferenceCompilerTests.Compile_AgentAndSharedScopeBlackboard_RequiresPolicyOptIn` — rejected
    without opt-in (`AIBT2030`, `TreeValidator`'s own pre-existing check), accepted with a real
    opt-in policy, `Phase1`'s own defaults confirmed untouched.
  - `ReferenceCompilerTests.Compile_AgentScopeAllowedButSharedRejected_OnlyAgentSlotCompiles` —
    per-scope granularity (Agent allowed, Shared not, in the same compile).
  - `McpAuthoringToolDispatcherTests.CreateTreeWithAgentScopeKeyRequiresPolicyOptInAndWritesV2` —
    real end-to-end MCP proof: rejected without a project policy, accepted and written as
    `formatVersion: 2` once `.aibt/policy.json` opts in.
  - `McpAuthoringToolDispatcherTests.CreateTreeWithOnlyTreeScopeKeysStillWritesV1` — no blanket v2
    default.
  - `McpVerificationToolDispatcherTests.CompileWithAgentScopeBlackboardRequiresPolicyOptIn` — same
    proof through the `compile` verification tool.
  - `McpVerificationToolDispatcherTests
    .SimulateOnAgentScopeTreeReturnsAClearUnsupportedCapabilityDiagnosticNotAConfusingFailure` —
    confirms `simulate` still rejects Agent scope even with a project policy opted in (it never reads
    one), with the real `AIBT2030` diagnostic, not a confusing deep failure.

## Scope and limitations

- The reference (managed) executor's own runtime storage (`ReferenceBlackboardStorage`) still
  supports Tree scope only — deliberately, per the owner's own explicit choice. `simulate`
  (reference-backed preview) cannot run an Agent/Shared document; real execution goes through the
  native backend, which already supports it.
- MCP's own default-value JSON reader/writer covers the 15 built-in scalar types the authoring
  surface already supported (not Enum32/Registered) — an unrelated, pre-existing, still-disclosed
  limitation, not widened by this card.
