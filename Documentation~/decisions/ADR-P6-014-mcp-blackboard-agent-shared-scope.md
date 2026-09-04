# ADR P6-014: MCP blackboard Agent/Shared scope

- Status: Accepted 2026-08-31
- Date: 2026-08-31
- Decision ID: AIBT-029

## Context

MCP's blackboard-declaring tools (`create_tree`'s initial `blackboard`, `set_blackboard_keys`)
explicitly reject Agent/Shared-scope blackboard keys today (`McpAuthoringJson.ReadBlackboardKey`:
"Only tree-scoped blackboard keys are supported"). A 2026-08-28/29 fix session investigated this in
two escalating passes and found the true scope larger than the originating finding suggested,
deferring the decision rather than resolving it mid-session. Both passes are recorded in the card
itself; this ADR resolves the two open questions they left standing, with a materially different
answer than either pass's own framing assumed.

## Spike evidence (`Spikes~/McpBlackboardAgentSharedScope/`, 2026-08-31, this workstation)

A disposable NUnit spike (`SpikeMcpBlackboardAgentSharedScope`, run live via Unity MCP `run_tests`)
built a real `TreeDocument.CreateVersion2` document with a genuine Agent-scope blackboard key and a
matching `BlackboardScopeContract`, exactly mirroring `TreeValidatorTests.cs`'s own accepted
construction pattern.

1. **`TreeValidator` does respect the policy opt-in flags, confirmed.** A custom
   `ReferenceCompilationPolicy(supportsAgentScope: true, supportsSharedScope: true)` -- a distinct
   instance, never the shared `Phase1` constant, which remains completely untouched and still
   defaults both flags `false` -- makes `TreeValidator.Validate` accept the same document
   `ReferenceCompilationPolicy.Phase1`'s own default options reject with
   `TreeValidationDiagnosticCodes.UnsupportedBlackboardScope`. This confirms pass 1's own
   Investigation-pass-1 conclusion and answers question 1 directly: **no**, enabling this for MCP
   does not require touching the shared `Phase1` constant at all -- `ReferenceCompilationPolicy` was
   already designed as a plain constructible object with `Phase1` as one convenience default among
   possible others, not a hardcoded gate.
2. **Real, decisive finding that supersedes both prior passes' own framing:
   `ReferenceCompiler` rejects Agent/Shared scope unconditionally, never consulting the policy at
   all.** The exact same opt-in policy that made `TreeValidator` accept the document still makes
   `ReferenceCompiler.Compile` fail, with `AIBT3012` ("Phase 1 compilation supports only Tree-scope
   blackboard slots"). Reading `Authoring/Compilation/ReferenceCompiler.cs`'s own check
   (`if (key.Scope != BlackboardScope.Tree) throw Failure(UnsupportedCapability, ...)`) confirms this
   check does not read `SupportsAgentScope`/`SupportsSharedScope` at all -- unlike `TreeValidator`,
   which does. Investigation pass 2's own open question 2 ("does the runtime executor actually
   execute Agent/Shared reads/writes") assumed a validated Agent/Shared document could be compiled;
   it cannot, by construction, confirmed here rather than assumed. The runtime-storage layer's own
   separate Tree-only rejection (`ReferenceBlackboardStorage.TryCreate`, read but not additionally
   spiked since it is unreachable -- no `CompiledProgram` carrying a non-Tree slot can ever exist)
   is therefore moot: the wall is one layer earlier than either investigation pass placed it.

Full raw output is in `Planning~/Evidence/P6-014/README.md`.

## Decision

**Not implemented, deferred.** MCP's blackboard tools continue to reject Agent/Shared-scope keys
exactly as they do today. This is a legitimate, evidence-backed outcome, not an unexamined default:

1. Supporting Agent/Shared scope through MCP is not "widen JSON parsing and flip a policy flag" --
   it requires the reference *compiler* itself to gain a genuinely new capability
   (`ReferenceCompiler.cs`'s own hardcoded Tree-only slot check would need to become policy-aware,
   consistent with how `TreeValidator` already is), which is new engine capability work, not a
   facade or policy-construction change MCP's own tools could make unilaterally.
2. `ReferenceCompilationPolicy.Phase1`'s naming is confirmed a deliberate statement, not an
   unexercised flag nobody flipped: `ReferenceCompiler.cs`'s own diagnostic message literally reads
   "Phase 1 compilation supports only Tree-scope blackboard slots," matching
   `ReferenceBlackboardStorage`'s own "Phase 1 reference storage supports only Tree-scope compiled
   slots." Both independently name the same boundary the same way. Agent/Shared blackboard scope is
   a stated later-phase capability at the engine level, not a gap specific to MCP or to any one
   validation layer.
3. If a future card wants to build real Agent/Shared execution support, the correct target is
   `ReferenceCompiler.cs`'s own slot-compilation logic (and, once that exists, `ReferenceBlackboardStorage`'s
   matching runtime-storage support) -- an engine capability card in its own right, requiring its own
   escalation and design, not something layered onto MCP's authoring tools. Until that exists, no
   MCP tool, hot-reload driver, or other reference-executor consumer can offer real Agent/Shared
   support regardless of how permissively it parses JSON.

## Consequences

- `MCP/Authoring/McpAuthoringJson.cs`'s current explicit rejection of Agent/Shared blackboard keys
  is correct as-is and needs no change; this ADR provides the evidence-backed justification the
  original code lacked (a comment could cite this ADR, at a future card's discretion).
- Any future engine card building real Agent/Shared compilation support should re-read this ADR's
  evidence directly rather than re-deriving it -- both the `TreeValidator`/`ReferenceCompiler`
  asymmetry and the exact diagnostic codes involved (`AIBT3012` at compile time,
  `BlackboardStorageDiagnosticCodes.UnsupportedScope` at the unreachable runtime layer) are now
  documented facts, not things to rediscover.
- No production file was touched (`McpAuthoringJson.cs`, `McpAuthoringToolDispatcher.cs`,
  `ReferenceCompilationPolicy.cs`, `TreeValidator.cs`, `ReferenceCompiler.cs` all remain exactly as
  they were), per this card's own Forbidden-changes clause.

## Explicitly unverified (stated, not generalized)

- `ReferenceBlackboardStorage.TryCreate`'s own Tree-only rejection was read and cited but not
  separately re-spiked, since no `CompiledProgram` carrying a non-Tree-scope slot can exist while
  `ReferenceCompiler`'s own unconditional check stands -- the runtime layer is unreachable, not
  independently exercised.
- Whether `ReferenceCompiler.cs`'s Tree-only check could be made policy-aware cheaply (mirroring
  `TreeValidator`'s own existing pattern) or is substantially harder for reasons not yet investigated
  (blackboard slot layout, cross-instance addressing for Shared scope, etc.) was not assessed -- that
  is exactly the scope of the future engine capability card this ADR defers to, not answered here.

## Addendum (2026-09-04)

This ADR's "not implemented, deferred" Decision is being revisited: the owner explicitly authorized
real implementation, superseding the deferral -- not because the evidence above was wrong, but
because the owner now wants the capability this ADR found architecturally absent. Spun off as
`P7-018` (tree-format `v2` promotion, rescoped to include the engine unblock this ADR identified).
This ADR's own findings remain the accurate, still-current map of the real work -- both engine
walls' exact locations (`ReferenceCompiler.cs`'s `AIBT3012` check and `ReferenceBlackboardStorage`'s
matching `UnsupportedScope` check, both unconditional and never consulting the policy flags) --
`P7-018` builds on them directly rather than re-investigating.
