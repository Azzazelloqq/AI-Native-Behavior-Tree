# P7-020 — CI-enforced public API diff check

Status: `Draft`

## Objective

`P7-001`'s stability review proposed, as its 5th open question, making
`Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1` a real CI-enforced check (catching an
accidental public-surface change automatically) rather than a manually-run snapshot tool. Put to
the owner directly during `P7-016`'s gate session (2026-09-03): the owner's decision is yes, before
`1.0`.

## Depends on

- `P7-001` (the tool this card wires into CI) and `Planning~/Evidence/P7-GATE/
  p7-001-stability-decision.md` (the recorded owner decision this card implements).
- `P7-015` (`.github/workflows/release.yml`, `validation.yml` — the two existing CI entry points a
  new check should extend, not duplicate).

## Required reading

- `Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1` (the existing dump tool — confirm whether it
  already supports a "compare against a committed baseline and fail if not additive" mode, or
  whether this card adds one).
- `.github/workflows/validation.yml`'s `static` job (the existing pattern: pinned actions, sanitized
  artifact upload — a new check should match this discipline, not relax it).
- The most recent gate's own recorded baseline (`Planning~/Evidence/P7-GATE/public-api.txt` once
  `P7-016` lands) — the check's own committed comparison baseline.

## Allowed changes

- `Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1`, widened with a `-CompareAgainst`/similar
  mode if it does not already have one.
- `.github/workflows/validation.yml`'s `static` job (or a new job), running the comparison and
  failing loudly on an unexpected removal/rename — additive changes should not fail the build by
  default (Phase 7 and every future phase legitimately adds public surface); only a removal, rename,
  or other non-additive change should.
- A committed baseline file this check compares against (updated deliberately, once per accepted
  gate — not on every PR).
- `Planning~/Evidence/P7-020/`.

## Forbidden changes

- Failing CI on *any* public-surface change, additive included — that would make ordinary,
  legitimate development (new types every phase has added) impossible without constantly touching
  CI config. The check exists to catch removals/renames automatically, not to freeze the surface.
- Requiring a Unity job for this check if it can run from the same headless dump technique
  `Get-FullPublicApi.ps1` already uses on `windows-2022` (mirrors `P7-015`'s own local-first
  discipline; do not introduce a new dependency on `P0-005`'s still-unresolved self-hosted runner).

## Deliverables

- A CI job that runs `Get-FullPublicApi.ps1`, diffs it against the last accepted gate's committed
  baseline, and fails loudly (with the actual diff in its own log) on any removal or rename.
- A deliberately-broken test case (a synthetic removal) proving the check actually fails when it
  should, not merely that it passes when nothing changed.

## Acceptance criteria

- A real additive change (a new public type) does not fail the check.
- A real non-additive change (a synthetic rename/removal, reverted immediately after proving the
  point) does fail the check, loudly and specifically.

## Required verification

```text
Verify-Static.ps1
CI job dry run: additive change passes, deliberate removal/rename fails loudly
```

## Handoff notes

- Spun off from `P7-016`'s own gate session, per the owner's decision recorded in
  `Planning~/Evidence/P7-GATE/p7-001-stability-decision.md` — not required for `P7-016`'s own
  verdict, but required before an accidental breaking public-API change can be caught automatically
  before `1.0`.
