# P7-016 — Phase 7 integration gate

Status: `Blocked`

## Objective

Independently verify all Phase 7 evidence against `Documentation~/scope.md`'s "Release criteria for
1.0" and declare `1.0.0` implementation-ready or return precise blockers — mirroring
`P2-025`/`P3-013`/`P4-009`/`P5-010`/`P6-012`'s own shape exactly.

## Depends on

- `P7-001` through `P7-015` (every Phase 7 card).

## Allowed changes

- `Planning~/Evidence/P7-GATE/`.
- Status updates in `Planning~/work-items.json` after review.
- `README.md`/`CHANGELOG.md`, checked against a fresh claims inventory (per `P6-012`'s own
  precedent), if they are found stale relative to Phase 7's actual completion.

## Forbidden changes

- Fixing implementation inside the gate task or redefining any predecessor card's acceptance
  criteria.
- Declaring `1.0.0` itself — that remains the owner's own release decision, per
  `Planning~/USER_ACTIONS.md`'s "Approve final public API and persisted-format stability review."
  This gate confirms the *evidence* is complete and consistent; it does not cut the release.

## Deliverables

- A clean detached-UPM-harness compile-and-full-regression run, matching every prior gate's own
  bar exactly (fresh project, `com.azzazello.aibt` as a local `file:` package, nothing else from the
  host `Modules` project).
- Independent confirmation that `P7-001`'s public-API/persisted-format stability proposal and
  `P7-002`'s supported-platform-matrix/regression-threshold proposal both have a recorded owner
  decision — not merely a proposal awaiting one.
- A public-API surface diff against `P6-GATE`'s own baseline, confirmed purely additive unless
  `P7-001`'s own accepted stability review explicitly authorized a breaking change.
- An honest accounting of every disclosed gap this gate inherits (native-backend hot reload,
  production Play-mode host, trace inspection, applied-node discoverability) — each is either
  closed by a specific Phase 7 card, or explicitly still open and disclosed, never silently dropped
  from the record.

## Acceptance criteria

- Every Phase 7 card has an accepted handoff (a `Done` `Outcome` section, or an explicitly accepted
  disclosed-limitation state matching that card's own acceptance criteria).
- Required commands pass from a clean checkout with initialized submodules.
- Any unverified platform or performance claim remains explicitly unverified, per this project's
  own standing discipline across every prior gate.
- `Documentation~/scope.md`'s own "Release criteria for 1.0" list is checked item-by-item against
  real evidence, not assumed satisfied because the phase completed.

## Current gate result

Blocked — Phase 7 was only decomposed, not yet implemented, as of this card's own creation
(`Planning~/MASTER_PLAN.md`'s Phase 7 decomposition entry). Assignable once `P7-001` through
`P7-015` are all `Done`.
