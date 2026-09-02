# P7-002 supported-platform matrix and regression-threshold proposal evidence

## Result

Done, **accepted**. Produces and gets an owner decision on the concrete material
`Planning~/USER_ACTIONS.md`'s "Approve the exact supported browser/version policy" and "Approve
performance hardware classes and acceptable regression thresholds" bullets require. No production
file, test, or benchmark script was touched, per this card's own Forbidden-changes clause — this
card consolidates existing evidence, it manufactures no new benchmark run.

## What was built

`Documentation~/compatibility-matrix.md` (new): one platform x claim-type matrix, every measured
cell cited to its exact evidence file (not just a gate name — the specific results JSON), plus a
regression-threshold proposal derived from real spread re-opened live from the raw benchmark JSON
during this card's own planning, not recalled or invented:

- Windows Player (`Benchmarks~/Phase4/Platform/Windows/Results/windows-player-scheduling-20260821.json`,
  `scheduling-baseline-empty-job`, 16 agents): Immediate max is +8.5% over median, Budgeted +10.8%
  — tight, real-hardware spread. `BatchedJobsSameFrame` max is **+313.7% over median**, at the
  *smallest* tested population — confirming `P4-002`'s own Editor-side "batch overhead doesn't
  amortize with population" finding reproduces at the Player level too, not an Editor-only
  artifact.
- This directly grounded the proposal's central structural decision: a single regression-threshold
  percentage across all policies would be meaningless (either too loose for `Immediate`/`Budgeted`
  or a permanent false-positive generator for the batched policies) — so the proposal is per-policy,
  not uniform.

Every citation in the matrix (Windows, Android, Web, Safari/mobile/console gaps) was re-opened and
read directly during planning, per this card's own acceptance criterion — see the approved plan's
own "Research already verified" section for the full citation trail (`Planning~/Evidence/P2-WINDOWS/`,
`P2-ANDROID/`, `P2-WEB/`, `P0-003/`, `P4-002/`, `P4-004/`'s 2026-08-26 addendum, `P4-006/`, `P4-008/`,
`P6-021/`).

## A real attribution correction caught before writing anything

Prior-session recall attributed `CalibratedNanosecondsPerNodeStep = 60.275` (and its `0.25`
tolerance / 20.98% worst-observed-deviation figure) to `P4-008`. Re-opening the actual evidence
found both live in **`P4-004`'s own 2026-08-26 addendum** instead (`P4-004` *consumes* `P4-008`'s
extended Windows/Android probes to derive the calibration, but the numbers themselves are recorded
in `P4-004`'s own README) — cited correctly in the matrix as a direct result of the "re-open, don't
recall" verification discipline this card's own Required verification demands.

## The owner's decision

Put to the owner directly via `AskUserQuestion`, offering three options: approve as-is, approve
with named changes, or reject pending more data (naming concrete examples of what more data could
mean — a real batched-policy Android run, a separate Firefox Player run, or macOS/Safari access).

**The owner approved the matrix and threshold proposal as-is** (2026-09-02). No changes requested.
`Documentation~/compatibility-matrix.md`'s own status banner is updated from "DRAFT PROPOSAL" to
"ACCEPTED" accordingly — it is now the reference every later platform claim (README, samples,
release notes) should point at, per this card's own Handoff notes.

## Verification

```text
Verify-Static.ps1 -- passed
Every cited number cross-checked against its real source evidence file during planning (re-opened,
  not recalled) -- see the approved plan's own citation trail
AskUserQuestion put to the owner with the concrete proposal; answer recorded above and in the task
  card's own Outcome
```

## Scope and limitations

- The batched-policy ("`BatchedJobsSameFrame`/`PipelinedJobs`") regression-comparison method
  (multi-run median vs. multi-run median) is proposed structurally but no concrete run count was
  fixed, and accepting the matrix as-is did not resolve this — left as a real open item for
  whichever future card actually wires regression detection into CI (`P7-015`'s possible scope).
- Android's own raw benchmark JSON records median only, no min/max spread field — there is
  currently no data to derive an Android-specific threshold from; disclosed, not assumed equal to
  Windows' own spread.
- Firefox has no dedicated Player-level performance numbers of its own (only Chrome was
  Player-benchmarked in `P4-008`) — the matrix states this plainly rather than implying Firefox
  shares Chrome's own measured numbers.
