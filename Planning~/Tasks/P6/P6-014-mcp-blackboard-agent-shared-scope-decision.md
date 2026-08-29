# P6-014 — MCP blackboard Agent/Shared scope decision

Status: `Draft`

## Objective

Decide, on real evidence against the actual `TreeValidator`/`ReferenceCompilationPolicy`/
`CanonicalTreeJson` code (not assumption), whether and how MCP's blackboard-declaring tools
(`create_tree`'s initial `blackboard`, `set_blackboard_keys`) should support Agent/Shared-scope
blackboard keys, currently explicitly rejected by `McpAuthoringJson.ReadBlackboardKey`
(`MCP/Authoring/McpAuthoringJson.cs`) with "Only tree-scoped blackboard keys are supported."

This card exists because a 2026-08-28/29 fix session on 6 owner-confirmed findings from `P6-006`'s
evidence investigated this (item 5) in two escalating passes and found the true scope significantly
larger and more architecturally loaded than the originating finding's own text suggested, and the
owner chose to defer rather than decide mid-session. Both investigation passes are recorded here so
a future session does not have to re-derive them.

## Investigation pass 1: the "obvious" blockers are smaller than expected

- **`BlackboardScopeContract`** (`Authoring/Model/Blackboard/BlackboardScopeContract.cs`) is
  trivial: an opaque `(contractId: string, contractVersion: uint)` pair with only local grammar
  validation (`GeneratedIdentityRules.IsValidMemberId`). No external contract registry or catalog
  is consulted anywhere — a caller-supplied value is sufficient to construct one.
- **`RegisteredBlackboardTypeCatalog`** (the thing `P6-006`'s own evidence flagged as inaccessible
  to the MCP layer) is needed **only** for `BlackboardValueType.Registered` defaults
  (`CanonicalTreeJson.ReadDefault`'s `Enum32`/registered branches). MCP's blackboard tool already
  explicitly rejects `Enum32`/`Registered` value types (a pre-existing, disclosed, unrelated
  limitation) — so a scope limited to **built-in scalar types only** (the 15 non-Enum32/Registered
  `BlackboardValueType` values: `Bool`, `Int32`, `Int64`, `Float32`, `Float64`, `Float2`, `Float3`,
  `Quaternion`, `FixedString32/64/128/512`, `AgentId`, `EntityId`, `OperationId`, `AssetId`) needs
  no catalog access at all — a self-contained default-value JSON reader/writer mirroring
  `CanonicalTreeJson.ReadDefault`/`CanonicalTreeJsonWriter.WriteDefault`'s exact shapes (both
  `private` to `AIBT.Authoring`, so mirrored locally, matching this fix session's own item-1
  precedent for `Observer`/`Bindings`) is enough.
- **Tree format v2.** `TreeDocument.CreateVersion2`/the `formatVersion: 2` JSON shape are plain
  data with no other hidden requirements found — Agent/Shared entries and their scope contracts
  are only valid in v2 (`CanonicalTreeJson.ValidateRepresentable`), so `create_tree` would need to
  conditionally emit v2 (only when Agent/Shared keys or contracts are actually declared, preserving
  today's v1 behavior for the common tree-scope-only case) and `set_blackboard_keys` would need an
  explicit policy for what happens when a caller tries to add an Agent/Shared key to an *existing*
  v1 tree (recommendation from pass 1: refuse with a structured diagnostic rather than silently
  upgrading the file's format version — an implicit format migration is exactly the kind of thing
  `DECISION_BOUNDARIES.md`'s "persisted JSON/schema... changes" rule exists to catch).

## Investigation pass 2: a real, deeper blocker found after the owner had already approved scoping down to pass 1's plan

`TreeValidator.ValidateBlackboardScope` (`Authoring/Validation/TreeValidator.cs`) rejects any
Agent/Shared-scope key outright with `TreeValidationDiagnosticCodes.UnsupportedBlackboardScope`
**unless** `ValidationOptions.SupportsAgentScope`/`SupportsSharedScope` are explicitly `true`. These
flags flow from `ReferenceCompilationPolicy.SupportsAgentScope`/`SupportsSharedScope`
(`Authoring/Compilation/ReferenceCompilationPolicy.cs`), which default to `false` in the
constructor — and **`ReferenceCompilationPolicy.Phase1` (`new ReferenceCompilationPolicy()`, both
flags `false`) is the exact policy constant every MCP tool hardcodes**
(`McpAuthoringToolDispatcher.BuildRegistryAndOptions`, `McpVerificationToolDispatcher`'s
validate/compile). A grep across the entire codebase for `supportsAgentScope:\s*true` /
`supportsSharedScope:\s*true` found matches **only** in three test files
(`TreeValidatorTests.cs`, `ReferenceCompilerTests.cs`, one `GeneratedArtifactContractTests.cs`
case) — **no production code path anywhere in AIBT currently enables Agent/Shared-scope
compilation.**

This means supporting Agent/Shared through MCP is not "widen JSON parsing" — it is "become the
first production consumer of an execution-capability flag that is off everywhere else in the
codebase, under a policy constant deliberately named `Phase1`." Two real open questions follow,
neither answered during this fix session:

1. Is the `Phase1` naming/default a deliberate statement that Agent/Shared blackboard scope is a
   later-phase capability, not yet meant to be exercised by any Phase 1-era tool (including MCP)?
   Or is it simply an unexercised flag nobody has had a reason to flip yet?
2. Does the reference (managed) executor actually **execute** Agent/Shared blackboard reads/writes
   correctly at runtime once validation is bypassed, or only pass static validation? `Runtime/Blackboard/Storage/`
   has real `BlackboardScope`-aware diagnostics (`AIBT4201`-`4209`), suggesting some runtime
   support exists, but this session did not verify it end-to-end (no behavior case or test was run
   proving an Agent/Shared blackboard entry round-trips correctly through a real tree instance's
   execution, only that static validation accepts it with the flag on).

## Depends on

- `P6-006` (done — the card whose evidence originally disclosed the narrower "no Agent/Shared
  support" limitation this card investigates fully).

## Required reading

- This card's own "Investigation pass 1" and "Investigation pass 2" sections above — do not
  re-derive from zero; confirm, extend, or correct them with fresh evidence.
- `Authoring/Validation/TreeValidator.cs`'s `ValidateBlackboardScope` and `ValidationOptions`
  (`Authoring/Validation/TreeValidationContracts.cs`).
- `Authoring/Compilation/ReferenceCompilationPolicy.cs`, specifically the `Phase1` static instance
  and every real call site that hardcodes it (`grep -rn "ReferenceCompilationPolicy.Phase1"`).
- `Runtime/Blackboard/Storage/` — whatever real Agent/Shared runtime read/write support already
  exists there, to answer open question 2 above with evidence, not assumption.
- `Documentation~/execution-and-scheduling.md` and `Documentation~/roadmap.md`'s phase boundaries,
  to check whether "Agent/Shared blackboard scope is a later-phase capability" is already a stated
  or implied decision anywhere, rather than this card inventing the question fresh.
- `MCP/Authoring/McpAuthoringJson.cs`'s `ReadBlackboardKey` (the current explicit rejection) and
  `MCP/Authoring/McpAuthoringToolDispatcher.cs`'s `BuildRegistryAndOptions`/`CreateTree` (the
  hardcoded `ReferenceCompilationPolicy.Phase1` and always-v1 format).

## Allowed changes

- `Spikes~/McpBlackboardAgentSharedScope/` (new, disposable) — proves whichever design is
  recommended, including a real runtime execution proof for open question 2, not validation-only.
- `Planning~/Evidence/P6-014/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `McpAuthoringJson.cs`, `McpAuthoringToolDispatcher.cs`,
  `ReferenceCompilationPolicy.cs`, or `TreeValidator.cs` — this card decides on paper (backed by a
  disposable spike); a separate future card implements an accepted decision.
- Changing `ReferenceCompilationPolicy.Phase1`'s own default flags without treating that as its own
  explicitly escalated, separately-justified decision (it is a shared constant with unknown blast
  radius beyond MCP — every other `Phase1`-labeled call site across the codebase would be affected).
- Assuming the reference executor correctly runs Agent/Shared blackboard semantics without a real
  proof.

## Deliverables

- A decided answer to open question 1 (is enabling Agent/Shared scope for MCP-authored trees
  consistent with the `Phase1` boundary, or does it require a different, explicitly-scoped policy
  object rather than touching the shared `Phase1` constant).
- A decided answer to open question 2, backed by a real spike proving (or disproving) correct
  runtime Agent/Shared blackboard behavior through the reference executor.
- If proceeding: the pass-1 design (built-in-scalar-only defaults, opt-in tree format v2, explicit
  refusal rather than silent upgrade for an existing v1 tree) confirmed or revised in light of the
  pass-2 findings.
- A proposed ADR recording the decision, including an explicit "not implemented, deferred because…"
  outcome as a legitimate, first-class option if the evidence does not support proceeding.

## Acceptance criteria

- The spike proves real runtime behavior (a compiled tree instance actually reading/writing an
  Agent- or Shared-scope blackboard entry through the reference executor), not just that
  `TreeValidator`/`ReferenceCompiler` accept the document with the capability flags enabled.
- The ADR states plainly whether `ReferenceCompilationPolicy.Phase1`'s own defaults are touched,
  and if not, exactly what alternative policy object MCP's blackboard tools would use instead and
  why that does not weaken any other `Phase1` call site's existing guarantee.

## Required verification

```text
Verify-Static.ps1
disposable spike: real ReferenceCompiler/TreeValidator/reference executor, live Unity MCP execute_code,
  proving actual Agent/Shared runtime behavior, not validation-only
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) — discovered mid-session as optional
  follow-up work on an already-`Done` card's disclosed limitation, not part of the original Phase 6
  decomposition's own scope. `P6-012` does not depend on it.
- If accepted, a future implementation card (not yet numbered) applies the ADR to production
  `MCP/Authoring/`.
