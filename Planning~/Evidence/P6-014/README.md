# P6-014 MCP blackboard Agent/Shared scope decision evidence

## Result

Done, accepted: **not implemented, deferred**. `ADR-P6-014` (`AIBT-029`) confirms MCP's blackboard
tools should continue rejecting Agent/Shared-scope keys exactly as they do today, backed by real
evidence rather than the two open questions the originating fix session left standing.

## Real finding: the wall is one layer earlier than either investigation pass placed it

Investigation pass 1 (recorded in the card itself) concluded that Tree format v2 plus an opt-in
policy flag would be enough to make Agent/Shared-scope documents compile. Investigation pass 2 found
`TreeValidator.ValidateBlackboardScope` also gates on the same flags, and asked (open question 2)
whether the runtime executor actually *executes* Agent/Shared reads/writes once validation is
bypassed -- implicitly assuming compilation itself would succeed.

The spike proves otherwise: `TreeValidator` does respect `SupportsAgentScope`/`SupportsSharedScope`
(confirmed -- the same document is accepted with the flag on, rejected with it off), but
`ReferenceCompiler.cs`'s own separate Tree-scope-only check (`AIBT3012`,
"Phase 1 compilation supports only Tree-scope blackboard slots") does **not** consult those flags at
all -- it is unconditional. The exact same opt-in policy that satisfies `TreeValidator` still makes
`ReferenceCompiler.Compile` fail. A validated Agent/Shared-scope document can never become a
`CompiledProgram` today, so the runtime-storage question (whether `ReferenceExecutionMachine`
actually executes such a slot) never arises -- `ReferenceBlackboardStorage`'s own matching Tree-only
rejection is unreachable, not merely unexercised.

This also directly answers open question 1: `ReferenceCompilationPolicy.Phase1`'s naming is a
deliberate statement. Two independent code paths (`ReferenceCompiler.cs` and
`ReferenceBlackboardStorage.cs`) both literally say "Phase 1 ... supports only Tree-scope," in their
own diagnostic text, confirming this is a stated engine-level boundary, not an accidental default.

## Verification

```text
Disposable spike (SpikeMcpBlackboardAgentSharedScope, Tests/Editor/McpBlackboardAgentSharedScopeSpike/
  during this session, archived afterward): 2/2 tests passing, live via Unity MCP run_tests --
  TreeValidator_AcceptsAgentScope_OnlyWhenThePolicyOptInFlagIsSet_AndNeverTouchesPhase1,
  ReferenceCompiler_RejectsAgentScope_UnconditionallyRegardlessOfThePolicyFlag
Regression (unmodified, live via Unity MCP): AIBT.Tests.Editor.Validation.TreeValidatorTests --
  24/24 passing
Verify-Static.ps1 -- passed
git diff --check -- clean
```

No production file (`McpAuthoringJson.cs`, `McpAuthoringToolDispatcher.cs`,
`ReferenceCompilationPolicy.cs`, `TreeValidator.cs`, `ReferenceCompiler.cs`) was touched, per this
card's own Forbidden-changes clause -- the spike lived temporarily in
`Tests/Editor/McpBlackboardAgentSharedScopeSpike/`, then archived to
`Spikes~/McpBlackboardAgentSharedScope/` and deleted from `Tests/`, mirroring this session's own
established precedent.

## Handoff

A future engine capability card wanting real Agent/Shared blackboard execution support would need to
make `ReferenceCompiler.cs`'s own Tree-scope-only check policy-aware (mirroring `TreeValidator`'s
existing pattern) and then give `ReferenceBlackboardStorage` matching runtime support -- its own
escalated design, not an MCP authoring change. `MCP/Authoring/McpAuthoringJson.cs`'s current
rejection needs no change; this ADR supplies the evidence-backed justification it previously lacked.
