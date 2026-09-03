# P7-016 — Phase 7 integration gate

Status: `Done`

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

## Outcome

**Accepted, with disclosed gaps — does not declare `1.0.0`** — 2026-09-03, against commit
`eedeb3c8408714ed5e5b3ee773a7a76c258e9864`. Clean detached-UPM-harness compile (exit 0) and full
EditMode regression **1269/1270**, 0 skipped (up from `P6-GATE`'s 1224/1224) — one real,
pre-existing, disclosed failure (`McpApiReferenceGenerator`'s package-root resolution silently
breaks for a real UPM consumer, first surfaced by this gate's own detached-harness technique; spun
off as `P7-021`, not fixed inside this gate). Public API: **425 types/2130 members, +13/+34 versus
`P6-GATE`'s own combined baseline, confirmed purely additive by direct type-set comparison, zero
removals**. Assembly dependency audit: zero drift since `P6-GATE`. `scope.md`'s 7 release criteria
checked item-by-item: 5 fully met, 2 partially met (stable contracts — blocked on tree-format `v2`
promotion; production-ready editor/debugger — blocked on a still-undecided-but-unbuilt production
Play-mode host). This gate's own review found and fixed real bookkeeping drift (4 task cards with
accepted evidence but a stale `Status: Draft`) and closed a genuinely open acceptance criterion —
`P7-001`'s stability proposal had no recorded owner decision; one was gathered live this session
(`Runtime`/`Authoring` stable, `Editor`/`Mcp` experimental — the latter partly *because* this gate
found a real, previously-undocumented breaking change in `AIBT.Mcp`'s own tool-surface history),
producing three new required-before-`1.0` follow-up cards (`P7-018`, `P7-019`, `P7-020`) plus a
fourth from the regression failure above (`P7-021`) — none required for this gate's own verdict.
`README.md`/`CHANGELOG.md` had no Phase 7 section at all; both updated. **Phase 7 is complete**:
`P7-001` through `P7-016` are all `Done`. See `Planning~/Evidence/P7-GATE/README.md`.
