# P6-019 Auto scheduler heuristic recalibration evidence

## Result

Done, implemented, owner-approved. `NativeAutoSelectionV1.TrySelect`'s deterministic decision rule
now tries `Immediate`/`Budgeted` before `BatchedJobsSameFrame` for same-frame-required throughput
(the reverse of the original, defective order), grounded entirely in `P4-002`'s and `P4-006`'s own
already-measured cost data -- no new numeric threshold or coefficient was introduced.

## Owner approval

Presented directly to the owner via `AskUserQuestion` before any code was written: the specific
proposed change (reorder `TrySelect`'s branches; no new threshold; re-run `P4-006`'s own 24-case
methodology to confirm). **Approved as proposed**, 2026-08-31.

## The fix

`Runtime/Scheduling/Native/Auto/NativeAutoSelectionV1.cs`'s `TrySelect` (the deterministic rule
only -- `TrySelectAdaptive`, `P4-007`'s own rejected runtime-adaptation experiment, is deliberately
left unchanged; see its own updated remarks explaining why) now tries `Immediate` then `Budgeted`
before `BatchedJobsSameFrame`, instead of the reverse. `BatchedJobsSameFrame` remains fully
reachable -- it is demoted in priority, not removed -- for the case where it is the only
same-frame-capable policy a caller has made available (`SupportedPolicies`). A new
`NativeAutoSelectionReasonV1.PreferredOverBatchedByMeasuredCost` explains this branch when it fires,
distinct from the pre-existing `FallbackToOnlyAvailablePolicy` (which remains live only in
`TrySelectAdaptive`'s own untouched tail) and from `BatchedForSameFrameThroughput` (now meaning
"chosen because it was the only same-frame option left," not "preferred by default").

No new numeric threshold, coefficient, or default was introduced -- the recalibration is a pure
branch-priority reorder, chosen specifically because the existing cost-curve data does not support
deriving a reliable break-even formula (the overhead `P4-002`'s own README documents is non-linear
in a way inventing a formula from a handful of points would mean fabricating a model beyond what is
measured -- explicitly forbidden by this card's own text).

## Re-run of P4-006's own 24-case methodology (before/after, side by side)

**Before this card** (`Planning~/Evidence/P4-006/README.md`, `Benchmarks~/Phase4/AutoComparison/README.md`,
2026-08-2x run): `Auto` underperformed the best fixed policy in **23 of 24** measured cases, by
+188% to +1,774% in ns/agent. One exact match (`many-programs-small-populations`/16 agents); the
24th (`scheduling-baseline-empty-job`/16 agents) was a 3.9% underperformance, within noise.

**After this card** (re-run 2026-08-31, same isolated-project harness, same 6-scenario catalog, same
agent counts 16/64/256/1024, same `LatencyMode=SameFrame` scope, same
`minimumJobWorkloadNanoseconds`/`targetBatchWorkNanoseconds` = 50,000 configuration --
`Benchmarks~/Phase4/AutoComparison/Results/auto-comparison-windows-editor-20260831-124156.json`):

```text
scheduling-baseline-empty-job     16/64/256/1024  auto=Immediate  best=Immediate  gap=0%  (all 4)
shallow-tree-cheap-conditions     16/64/256/1024  auto=Immediate  best=Immediate  gap=0%  (all 4)
deep-sequence-selector-traversal  16/64/256/1024  auto=Immediate  best=Immediate  gap=0%  (all 4)
wide-branching-frequent-failures  16/64/256/1024  auto=Immediate  best=Immediate  gap=0%  (all 4)
predominantly-running-actions     16/64/256/1024  auto=Immediate  best=Immediate  gap=0%  (all 4)
many-programs-small-populations   16/64/256/1024  auto=Immediate  best=Immediate  gap=0%  (all 4)

TOTAL = 24, MATCHES = 24, UNDERPERFORM = 0
```

**`Auto` now matches the best fixed policy in 24 of 24 measured cases** (up from 1 of 24), reported
honestly from the same re-run this card's own acceptance criteria require -- no cherry-picking, same
scenarios/agent-counts/fixed-policy baselines as `P4-006`'s own original methodology. This is
consistent with `P4-002`'s/`P4-006`'s own finding that `Immediate` was cheaper in every measured
point *before* this card -- the recalibration simply makes `Auto`'s choice match what the evidence
already showed. `P4-005`'s/`P4-006`'s own prior evidence files were not edited to retroactively match
this result.

## Verification

```text
Unity EditMode: NativeAutoSelectionTests (P4-005's own 24-branch/determinism suite, updated per this
  card's own Allowed-changes clause) -- 25/25 passing
Unity EditMode: NativeAutoAdaptiveSelectionTests, NativeAutoAdaptiveRealisticFeedbackTests
  (P4-007's own experiment, deliberately untouched) -- 9/9 passing, unaffected
Re-run of P4-006's own 24-case comparison methodology -- 24/24 matches (see above)
Full host-project EditMode regression -- 1586/1586 executed, same 3 pre-existing unrelated
  failures every recent card's evidence already discloses, zero new ones
Verify-Static.ps1 -- passed
git diff --check -- clean
```

## Scope discipline

- `TrySelectAdaptive` (`P4-007`'s rejected runtime-autotuning experiment) was deliberately NOT
  recalibrated -- this card's own scope, and the owner's own approval, are specifically about
  `TrySelect`'s deterministic rule. `OQ-006`/`ADR-P4-007`'s own rejection of runtime adaptation is
  not reopened by this card.
- No new benchmark methodology was invented -- the same isolated-project harness, scenario catalog,
  and agent counts `P4-006` already used were re-run unchanged.
- This remains a single-workstation result, not generalized to other hardware, per
  `Planning~/USER_ACTIONS.md`'s own standing requirement -- unchanged by this card.
