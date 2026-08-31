# P6-013 ReferencePreviewDriver simulation-capability decision evidence

## Result

Done, accepted. `ADR-P6-013` (`AIBT-026`) decides `ReferencePreviewDriver`'s facade should be
widened for completions injection, resume-with-step-budget, and abort (via the `Abort(update,
reason, index)` overload, not `RequestAbort`), plus a caller-supplied `TreeInstanceId` -- all four
already implemented by the wrapped `ReferenceExecutionMachine`, never exposed. `rootSeed` and
behavior-case-style external "events" are both rejected as out of scope: genuine missing **engine**
capability, not facade gaps, confirmed by direct evidence rather than assumed.

## Real finding: `RequestAbort` is not usable for the obvious "cancel a waiting operation" case

The spike's first `Abort` attempt used `ReferenceExecutionMachine.RequestAbort(reason, index)` --
the method the card's own research had already spotted as `internal`-but-reachable. It failed
(`Commands.Records.Count` was `0`, not `1`) because `RequestAbort` requires an *already-open* update
(`_hasOpenUpdate`) and is rejected once a tick reaches a `Waiting` boundary -- exactly the state a
preview caller most wants to cancel from (an operation the tree is currently waiting on). The
*other* overload, `Abort(update, reason, index[, budget])`, opens its own fresh update context and
drives the whole abort traversal to a real boundary in one call; switching to it fixed the spike and
is now the ADR's own explicit recommendation, not a detail left to a future implementer to
rediscover.

## Verification

```text
Disposable spike (SpikeReferencePreviewSimulationCapability, Tests/Editor/ReferencePreviewSpike/
  during this session, archived afterward): 4/4 tests passing against the real, unmodified
  ReferenceExecutionMachine, live via Unity MCP run_tests --
  CompletionsInjectionRoundTrip, AbortMidTick, ResumeAfterStepBudgetYield,
  TwoConcurrentSessionsWithDistinctInstanceIdsDoNotInterfere
Full EditMode regression (host project): 1589/1589 executed with the spike present (same 3
  pre-existing unrelated failures), 1585/1585 after archiving it back out -- zero regressions
  either way
Tests/Editor/Preview/ReferencePreviewParityTests.cs (P3-009's own accepted parity guarantee):
  re-run unmodified in both full-regression passes above, still passing
Verify-Static.ps1 -- passed
git diff --check -- clean
```

No production file (`ReferencePreviewDriver.cs`, `ReferenceExecutionMachine.cs`,
`MCP/Verification/`) was touched, per this card's own Forbidden-changes clause -- the spike lived
temporarily in `Tests/Editor/ReferencePreviewSpike/`, then archived to
`Spikes~/ReferencePreviewSimulationCapability/` and deleted from `Tests/`, mirroring `P5-001`'s own
`SpikeHotReloadCompatibilityModel` precedent exactly.

## Handoff

A future, not-yet-numbered implementation card applies `ADR-P6-013`'s four widenings to the real
`ReferencePreviewDriver.cs` and extends `P6-007`'s `simulate` tool's own step reader
(`MCP/Verification/McpVerificationJson.cs`) to accept the newly-available fields.
