# P7-002 — Supported-platform matrix and regression-threshold proposal

Status: `Draft`

## Objective

Consolidate every platform benchmark already gathered (`P2-022` Windows, `P2-023` Android ARM64,
`P2-024`/`P0-003` Web, `P4-002` cost curves, `P4-006` `Auto`-vs-fixed comparison, `P4-008` real
Player platform evidence, `P6-021`'s Windows Player re-run) into one candidate supported-platform
matrix and a proposed set of regression thresholds, then put both to the owner for the explicit
approval `Planning~/USER_ACTIONS.md` already requires ("Approve the exact supported browser/version
policy" and "Approve performance hardware classes and acceptable regression thresholds after
research results exist"). The research already exists; this card is what makes it decidable.

## Depends on

- `P4-009` (Phase 4 integration gate; every fixed-policy/`Auto` number this card cites is
  Phase-4-accepted).
- `P6-021` (the only card to actually build and run a real Windows Player from the isolated
  benchmark harnesses since `P4-008`).

## Required reading

- `Documentation~/benchmarks.md`'s "Platform process" section (mandatory pre-1.0 targets: Windows
  x64, Android ARM64, Unity Web on supported desktop browsers).
- `Planning~/Evidence/P4-008/` and `Planning~/Evidence/P6-021/` (the actual measured numbers).
- `Planning~/USER_ACTIONS.md`'s two relevant bullets under "Required before public 1.0 claims."

## Allowed changes

- `Planning~/Evidence/P7-002/` (the proposal itself).
- A new `Documentation~/compatibility-matrix.md` draft, marked explicitly as a **proposal**, not yet
  an accepted claim, until the owner approves it.

## Forbidden changes

- Introducing any regression threshold, hardware class, or browser-support claim into a test,
  benchmark script, or shipped documentation as if already accepted — every number in this card's
  own deliverable is a proposal pending explicit owner sign-off, per `Planning~/DECISION_BOUNDARIES.md`.
- Running new benchmarks whose only purpose is producing a bigger number; this card consolidates
  existing evidence, it does not manufacture new claims to strengthen the proposal.

## Deliverables

- A single table: platform (Windows x64 / Android ARM64 / Web-Chrome / Web-Firefox / Web-Safari),
  measured or explicitly unmeasured, with a citation to the exact evidence file for every measured
  cell (Safari is explicitly unmeasured per `Planning~/USER_ACTIONS.md`'s macOS/Safari access gap —
  state this plainly, not silently omit the row).
- A proposed regression-threshold range per policy per platform, derived from the actual spread in
  the cited evidence (not a single best run, per `benchmarks.md`'s own rule), with the specific
  runs it was derived from named.
- An explicit `AskUserQuestion`-driven approval step (or, if run outside an interactive session, a
  clearly marked pending-decision document) before treating any number as accepted.

## Acceptance criteria

- No proposed number is asserted without a citation to a real, already-accepted evidence file.
- The Safari/mobile-browser gap from `Planning~/USER_ACTIONS.md` is restated, not silently dropped
  from the matrix.
- The owner's actual decision (approved, approved-with-changes, or rejected-pending-more-data) is
  recorded in this card's own evidence file, not assumed.

## Required verification

```text
Verify-Static.ps1
cross-check every cited number against its source evidence file (re-open and confirm, not recall)
```

## Handoff notes

- Once approved, the resulting `Documentation~/compatibility-matrix.md` becomes the reference every
  later platform claim (README, samples, release notes) must match — a future documentation pass
  should point at it rather than restating numbers inline.
- `P7-016` (Phase 7 gate) requires this card's proposal to have an owner decision recorded, one way
  or another, before Phase 7 can close.
