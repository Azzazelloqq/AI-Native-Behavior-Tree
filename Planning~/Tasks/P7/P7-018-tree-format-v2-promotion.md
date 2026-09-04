# P7-018 — Real Agent/Shared blackboard scope, and tree-format v2 as a working default

Status: `Done`

## Objective

**Rescoped 2026-09-04 — read this before the original framing below.** Re-verifying this card's own
required dependency (`P6-014`) before planning found the original objective ("promote tree-format
v2 to default, unblocking Agent/Shared capability flags") does not hold: `ADR-P6-014` (Accepted
2026-08-31) already investigated exactly this and decided **not implemented, deferred**, backed by
real evidence that flipping the capability flags alone unlocks nothing — two independent,
*unconditional* engine walls reject Agent/Shared-scope blackboard slots regardless of policy flags:
`Authoring/Compilation/ReferenceCompiler.cs`'s `BuildBlackboardSlots` (`if (key.Scope !=
BlackboardScope.Tree) throw Failure(UnsupportedCapability /* AIBT3012 */, "Phase 1 compilation
supports only Tree-scope blackboard slots.", ...)`, never consulting
`SupportsAgentScope`/`SupportsSharedScope`) and `Runtime/Blackboard/Storage/
ReferenceBlackboardStorage.cs`'s matching, separate Tree-only rejection. `TreeDocument.CreateVersion2`'s
only real difference from v1 is carrying `agentContract`/`sharedContract` — so promoting v2 to a
"default" without also making Agent/Shared scope actually compile and execute would be pure format
churn with zero functional benefit.

**The owner, informed of this contradiction, explicitly authorized reopening `ADR-P6-014`'s
"deferred" conclusion and commissioning the real implementation (2026-09-04)** — not a
misunderstanding to route around, a genuine decision to build what `ADR-P6-014` found missing. This
card's real objective is now: make `ReferenceCompiler`/`ReferenceBlackboardStorage` policy-aware
(mirroring `TreeValidator.ValidateBlackboardScope`'s already-correct pattern), so a document that
actually declares Agent/Shared blackboard entries can compile and execute end-to-end — and *then*
have real production writers emit `formatVersion: 2` for such documents (not blanket-forced onto
every ordinary tree-scope-only tree, which gains nothing from v2 and should keep writing v1, per
`P6-014`'s own investigation-pass-1 recommendation).

## Depends on

- `P6-014` (Done — its own investigation is the accurate, current map of both engine walls; this
  card builds directly on those findings rather than re-deriving them) and `ADR-P6-014`'s own
  Addendum (2026-09-04) recording this reopening.
- `P7-001`/`Planning~/Evidence/P7-GATE/p7-001-stability-decision.md` (the original owner decision
  this card was spun off from — still the reason a real v2 write path is required before `1.0`, now
  correctly scoped).

## Required reading

- `ADR-P6-014` in full, plus `Planning~/Evidence/P6-014/README.md` — the exact wall locations,
  diagnostic codes (`AIBT3012` at compile time, `BlackboardStorageDiagnosticCodes.UnsupportedScope`
  at the runtime layer), and the already-answered question of whether `ReferenceCompilationPolicy
  .Phase1`'s own shared default needs touching (answered: **no** — a distinct policy instance with
  the flags `true`, never the shared `Phase1` constant, is the already-proven, correct shape; every
  other `Phase1` call site must keep working exactly as before).
- `Authoring/Validation/TreeValidator.ValidateBlackboardScope` — the already-correct,
  policy-aware pattern `ReferenceCompiler`'s own check must be brought in line with.
- `Runtime/Blackboard/Storage/ReferenceBlackboardStorage.cs` in full, plus whatever
  `BlackboardScope`-aware machinery already exists there (`ADR-P6-014` notes real
  `AIBT4201`-`4209` diagnostics already exist, suggesting partial scope-awareness groundwork —
  confirm exactly how much before assuming this is greenfield).
- `Planning~/DECISION_BOUNDARIES.md` — this card touches persisted/compiled-format behavior
  (`Must escalate`); the owner has already authorized the core direction, but any specific design
  choice within it that turns out to be a genuinely new cross-cutting shape (e.g. Shared-scope
  cross-instance addressing, if it needs something structurally new) still needs to be surfaced, not
  invented silently.
- Every current production writer of `*.aibt.json` (`MCP/Authoring/McpAuthoringToolDispatcher
  .CreateTree`, `Editor/` authoring paths, etc.) — confirm the full list before assuming scope.

## Allowed changes

- `Authoring/Compilation/ReferenceCompiler.cs` — make `BuildBlackboardSlots`'s scope check
  policy-aware, mirroring `TreeValidator`'s pattern exactly (Tree always allowed; Agent/Shared
  allowed only when the compiling policy's own `SupportsAgentScope`/`SupportsSharedScope` is `true`).
- `Runtime/Blackboard/Storage/ReferenceBlackboardStorage.cs` — real runtime read/write support for
  Agent- and Shared-scope compiled slots, not just removing the rejection.
- Production writers of `*.aibt.json` — emit `formatVersion: 2` (with real `agentContract`/
  `sharedContract`) specifically for documents that declare Agent/Shared blackboard entries;
  ordinary tree-scope-only documents keep writing v1 unchanged.
- `Planning~/Evidence/P7-018/`.

## Forbidden changes

- **`ReferenceCompilationPolicy.Phase1`'s own default flags stay `false`.** `ADR-P6-014` already
  proved and decided this — every other `Phase1` call site across the codebase (MCP
  validate/compile, etc.) must keep behaving exactly as today unless that specific call site
  explicitly constructs/opts into a policy with the flags `true`. This is not this card's decision
  to revisit.
- Do not silently widen scope to `Enum32`/`Registered` blackboard value types for Agent/Shared keys
  — a separate, pre-existing, disclosed limitation (`P6-014`'s own investigation pass 1), out of
  this card's fence.
- Do not force `formatVersion: 2` onto documents that declare no Agent/Shared entries — that
  provides no capability and is pure format churn (the mistake the original card text made).
- Breaking v1-document round-tripping — v1 documents must keep reading, compiling, and executing
  exactly as before.

## Deliverables

- A real, working `formatVersion: 2` write path used specifically where Agent/Shared blackboard
  scope is actually declared.
- `ReferenceCompiler`/`ReferenceBlackboardStorage` both policy-aware for Agent/Shared scope,
  proven correct with the shared `Phase1` constant provably unaffected.
- `Planning~/Evidence/P7-GATE/p7-001-stability-decision.md`'s open item 3 updated to point at this
  card's own evidence once done.

## Acceptance criteria

- Full regression passes with zero v1-path regressions.
- A real document round-trip (author a document with a real Agent- or Shared-scope blackboard entry
  → written as v2 → read back → compiled with an explicit opt-in policy → executed through the
  reference executor) demonstrates the capability actually works end-to-end — not just that
  validation accepts it or that the flag compiles.
- A parallel proof that `ReferenceCompilationPolicy.Phase1`'s own existing call sites (e.g. MCP's
  validate/compile) are completely unaffected — still reject Agent/Shared exactly as before.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full
a real Agent/Shared-scope document authored, written as v2, read back, compiled (explicit opt-in
  policy), and executed through the reference executor, live proof
a parallel proof that Phase1-using call sites still reject Agent/Shared exactly as before
```

## Handoff notes

- Original text (spun off from `P7-016`'s gate session, `2026-09-03`) assumed promoting v2-to-default
  alone would unblock Agent/Shared capability. Re-verifying `P6-014` before planning (2026-09-04)
  found this contradicted `ADR-P6-014`'s own already-accepted "deferred" decision and that v2 has no
  standalone value without real Agent/Shared support. Put to the owner rather than silently
  resolved; the owner chose to reopen `ADR-P6-014` and commission the real implementation, which is
  this card's now-corrected scope. See `ADR-P6-014`'s own Addendum (2026-09-04).

## Outcome

Done, 2026-09-04. A second real re-scoping happened mid-planning, before any code was written:
investigating `ReferenceBlackboardStorage.cs` found it is architecturally a flat, single-tree-instance
byte arena with no cross-instance/shared concept at all -- giving it real Agent/Shared runtime
support would mean building genuinely new storage architecture from scratch. Separately,
`Authoring/Compilation/Generated/GeneratedScopeCompiler.cs` and `Runtime/Blackboard/Native/Shared/
NativeSharedContextOwnerV1.cs` were found to **already fully implement** Agent/Shared blackboard
scope for the **native** backend -- unconditionally, with real contribution/reduction/multi-instance
machinery, already covered by passing test suites (`GeneratedScopeCompilerTests`,
`NativeSharedContextTests`). Put to the owner again: build the missing reference-side storage from
scratch, or leave the reference backend Tree-only (disclosed) and wire up only the already-working
native path end-to-end. **The owner chose native-only.**

The real remaining gap was narrower than either framing: MCP's authoring/verification surface is
deliberately, exclusively reference-executor-bound (`McpVerificationToolDispatcher`'s own doc
comment: "no second validator/compiler/executor exists here"), and even a project opting in via
`.aibt/policy.json` couldn't compile an Agent/Shared document, because `ReferenceCompiler.cs`'s own
`BuildBlackboardSlots` check was *unconditional* (never consulted the policy flags at all) --
unlike `TreeValidator.ValidateBlackboardScope`, which already did.

**Implementation:**
- `Authoring/Compilation/ReferenceCompiler.cs`: `BuildBlackboardSlots`'s scope check made
  policy-aware, mirroring `TreeValidator`'s exact pattern. Live-discovered while testing:
  `ReferenceCompiler.Compile` already runs `TreeValidator.Validate` first (using the same
  policy-derived options) -- that pre-existing check already gates Agent/Shared correctly when a
  policy opts in; this fix's own `AIBT3012` check is the second gate, reached only once validation
  already passes, exactly matching `ADR-P6-014`'s own live-proven finding.
- `MCP/Authoring/McpAuthoringToolDispatcher.BuildRegistryAndOptions`: now reads `.aibt/policy.json`
  (mirroring `McpVerificationToolDispatcher.Validate`'s already-established pattern) instead of
  hardcoding `ReferenceCompilationPolicy.Phase1`. `CreateTree` picks `TreeDocument.CreateVersion2`
  specifically when the document declares an Agent/Shared entry (matching
  `GeneratedScopeCompiler.cs`'s own already-enforced v2 requirement); an ordinary tree-scope-only
  document still writes v1.
- `MCP/Verification/McpVerificationToolDispatcher.Compile`: same policy-read treatment.
- `MCP/Authoring/McpAuthoringJson.cs`: `ReadBlackboardKey`/`WriteBlackboardKey` widened to accept
  `scope`/`default`/`reduction`, plus a local default-value and vector-shape reader/writer mirroring
  `CanonicalTreeJson`/`CanonicalTreeJsonWriter`'s own (private) canonical shapes exactly -- matches
  `P6-014`'s own investigation-pass-1 recommendation. New `ReadScopeContract`/`WriteScopeContract`
  for `create_tree`'s own `agentContract`/`sharedContract` args.
- `simulate` (`ReferencePreviewDriver`-backed) needed **no code change at all**: it hardcodes
  `ReferenceCompilationPolicy.Phase1` (never reads project policy), so it already, automatically
  fails an Agent/Shared document at `TreeValidator`'s own pre-existing `AIBT2030` check with a clear,
  specific diagnostic -- proven live rather than assumed.
- `ReferenceCompilationPolicy.Phase1`'s own shared default is untouched (still
  `false`/`false`) -- proven by a full regression pass and a direct assertion in the new
  `Compile_AgentAndSharedScopeBlackboard_RequiresPolicyOptIn` test.

**Verification:** full regression 392/392 (`AIBT.Editor.Tests`) and 1645-test whole-host-project run
(only 3 pre-existing, unrelated failures: 2 known `GeneratedArtifactContractTests` host-layout
failures, 1 unrelated `LocalSaveSystem` package test -- none touched by this card). Native-path
capability reconfirmed live rather than re-built: `GeneratedScopeCompilerTests` 11/11,
`AIBT.NativeSharedBlackboard.Tests` 36/36, both pre-existing and unaffected. New tests prove both
policy gates end-to-end (rejected without opt-in, accepted with a real project policy, `simulate`'s
own independent Tree-only diagnostic). See `Planning~/Evidence/P7-018/README.md`.
