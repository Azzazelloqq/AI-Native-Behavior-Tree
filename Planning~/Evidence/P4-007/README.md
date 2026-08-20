# P4-007 OQ-006 resolution: runtime autotuning evaluation evidence

## Result: runtime autotuning rejected

`OQ-006` is resolved. Full reasoning and evidence are in
[`Documentation~/decisions/ADR-P4-007-runtime-autotuning-resolution.md`](../../../Documentation~/decisions/ADR-P4-007-runtime-autotuning-resolution.md)
(`AIBT-013`, accepted). Summary:

- `P4-006` found `Auto` underperforming the best fixed policy in 23 of 24 measured cases, clearing
  `Documentation~/benchmarks.md`'s step-5 gate to test lightweight adaptation.
- A prototype was built: `NativeAutoPolicyCostTrackerV1` (per-policy bounded-EWMA smoothed recent
  cost, the same proven mechanism `P4-004`'s estimator uses) plus
  `NativeAutoSelectionV1.TrySelectAdaptive` (compares tracked candidates' costs once at least two
  have real data, falling back to `P4-005`'s exact deterministic rule at cold start or with fewer
  than two tracked candidates).
- **Tested against a realistic single-observer feedback model** (a real caller only learns the
  cost of the policy it actually ran, never an untried alternative's), using real
  `wide-branching-frequent-failures`/1024-agent numbers from `P4-002`
  (`Immediate` 5,157.42 ns/agent vs. `BatchedJobsSameFrame` 74,158.79 ns/agent — one of `P4-006`'s
  worst-gap cases): across 50 simulated rounds, the tracker for the policy never chosen
  (`Immediate`) never receives a single observation, so the adaptive comparison (which requires at
  least two tracked candidates) never activates. The prototype stays stuck on its cold-start
  mistake for all 50 rounds.
- A second test confirmed the comparison logic itself is correct once both policies' costs happen
  to be known (e.g. via an external exploration mechanism this card does not build) -- the flaw is
  specifically the missing exploration, not a bug in the comparison.
- **Verdict**: a purely reactive lightweight tracker cannot close this gap in a realistic
  deployment. Adding real exploration (periodically sampling non-preferred policies) would
  introduce exactly the overhead/instability/unpredictability `benchmarks.md`'s step 6 says
  disqualifies adaptation. `OQ-006` resolves as: fixed heuristics remain the right tool; `P4-006`'s
  gap is a specific, nameable defect in `P4-005`'s own decision rule (no cost comparison before
  preferring `BatchedJobsSameFrame`), fixable by deterministic recalibration -- legitimate future
  work, not built by this card.

## What shipped and what did not

- `Runtime/Scheduling/Native/Auto/NativeAutoPolicyCostTrackerV1.cs` (new): the tracker, kept as the
  tested, disclosed experiment this ADR cites -- not deleted, since it is correct and its
  documented failure mode (no exploration) *is* the evidence.
- `Runtime/Scheduling/Native/Auto/NativeAutoSelectionV1.cs` (modified): `TrySelect`'s own body was
  refactored (explanation-building extracted into a shared private `BuildExplanation` helper) to
  avoid duplicating that logic in the new `TrySelectAdaptive` -- `TrySelect`'s own 24 existing
  P4-005 tests were re-run unchanged after the refactor and all still pass, confirming no behavior
  change to the shipped deterministic entry point. `TrySelectAdaptive` is new, additive, and is not
  called from anywhere production-facing.
- `Runtime/Scheduling/Native/Auto/NativeAutoContracts.cs` (modified): added
  `NativeAutoSelectionReasonV1.AdaptiveLowestTrackedCost`, an additive enum value.
- No production code path selects `TrySelectAdaptive` over `TrySelect` -- this ADR's own
  Consequences section states that explicitly.
- 13 new tests: 4 for the tracker's own smoothing/bounding (mirroring `NativeWorkEstimatorV1`'s
  already-proven mechanism), 7 for `TrySelectAdaptive`'s mechanical correctness (forced-policy
  delegation, cold-start fallback, tracked-cost comparison, budget/minimum-workload priors
  unaffected, determinism, zero managed allocation), and 2 for the decisive realistic-feedback
  experiment described above.
- Full EditMode suite: 1399 tests (1386 + 13 new), 1395 passed; **4** pre-existing failures
  unrelated to this card -- the same 3 seen in every prior P3/P4 evidence file
  (`AIBT.Tests.CodeGen.Generation.GeneratedArtifactContractTests` x2,
  `LocalSaveSystem.Tests.SaveStoreTests.SaveStore_AutoSave_WritesToDisk`) plus a 4th,
  `LocalSaveSystem.Tests.SaveTaggedFormatTests.ValidateFieldIds_LogsDuplicates`, appearing for the
  first time in this card's run. Re-ran that 4th test in isolation and it failed there too, with
  the same message -- confirming it is not test-order pollution from this card's new tests, and
  `git status` inside the `AIBT` submodule confirms this session touched only
  `Runtime/Scheduling/Native/Auto/`, `Tests/Runtime/NativeExecution/Scheduling/Auto/`,
  `Documentation~/decisions.md` and `Documentation~/decisions/`, and `Planning~/` -- nothing in
  `LocalSaveSystem`, a package entirely unrelated to AIBT. Treated as a pre-existing,
  environment-specific issue in that other package, same category as the other 3, not investigated
  further since it is outside this card's (and AIBT's) scope.

## Scope note: Allowed changes did not literally list the Tests/ directory

This card's own "Allowed changes" lists `Runtime/Scheduling/Native/Auto/`,
`Planning~/OPEN_QUESTIONS.md`, `Documentation~/decisions.md`, and `Planning~/Evidence/P4-007/` --
it does not literally mention `Tests/Runtime/NativeExecution/Scheduling/Auto/`. New Runtime code
without any test coverage would violate this project's own established practice throughout every
prior P3/P4 card, and this exact directory already exists as `P4-005`'s own test area for the same
`Runtime/Scheduling/Native/Auto/` code this card extends -- adding tests for new code in the same
already-established pairing is treated as an implied, low-risk extension (matching the "Runtime/X/
+ Tests/.../X/" pattern every other Phase 4 card follows), not a new escalation-worthy scope
conflict, unlike `P4-001`'s/`P4-003`'s genuine boundary crossings into previously-untouched
directories owned by other cards.

## Scope and limitations

- This experiment used one real gap case (`wide-branching-frequent-failures`/1024 agents) as the
  decisive test; the "gets stuck" finding is a direct, provable consequence of the algorithm's own
  structure (no exploration mechanism can ever be reached without at least two tracked
  candidates), not a statistical claim needing broader sampling to hold.
- No claim is made that *no* adaptive design could ever help here -- only that the natural,
  minimal "lightweight adaptation" this card actually built and tested does not, and that the
  necessary fix (exploration) itself fails `benchmarks.md`'s own step-6 bar by introducing
  deliberate overhead/unpredictability.
- `P4-005`'s own decision-rule defect (unconditional `BatchedJobsSameFrame` preference) remains
  unfixed -- recalibrating it is real, valuable follow-up work this card explicitly does not
  attempt (this card resolves `OQ-006`'s question, not `P4-005`'s baseline).

See `verification-results.json` for exact commands and results.
