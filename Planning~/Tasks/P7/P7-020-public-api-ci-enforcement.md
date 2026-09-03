# P7-020 — CI-enforced public API diff check

Status: `Done`

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

## Outcome

**One part of the card's own text did not hold up to re-verification**, found before implementing:
its Forbidden-changes clause said not to require a Unity job "if it can run from the same headless
dump technique `Get-FullPublicApi.ps1` already uses on `windows-2022`." That's not actually true —
`Get-FullPublicApi.ps1` only works by launching a real, licensed Unity Editor in batch mode inside a
disposable isolated project (its own code comment explains plain-host reflection was abandoned
because it "cannot reliably resolve netstandard/Unity BCL dependencies"). `.github/workflows/
validation.yml`'s `static` job (`windows-2022`, GitHub-hosted) has no Unity installed anywhere —
confirmed by grep, no `game-ci`/`unity-builder`/license-activation step exists in this repo's CI. The
only job with Unity access is `unity` (`[self-hosted, Windows, X64, unity-6000.5.8f1]`), gated on
`P0-005`'s still-unresolved self-hosted runner, which has never once picked up a queued job.

**Resolution, matching `P7-015`'s own precedent of disclosing rather than faking a Unity CI gate**:
the new check was added as a step **inside the existing `unity` job**, not a second separate job —
this adds no *new* dependency on the blocked runner (that job already needs it for compile +
EditMode), and until `P0-005` resolves, this step is exactly as unproven in real GitHub Actions as
the rest of that job already is.

**Implementation**: `Get-FullPublicApi.ps1` gained an optional `-BaselinePath` parameter. When
supplied, it computes a **content-based set difference** (PowerShell `Compare-Object`, keyed on line
content, not position) between the baseline and the fresh dump, and throws (with the exact missing
lines listed) if any baseline line is genuinely absent from the fresh dump; new lines are logged only,
never fail. This deliberately avoids a known trap: `Planning~/Evidence/P7-GATE/README.md` records
that a *positional/textual* diff of this exact dump format previously produced a false "5 removed
members" signal, root-caused to the dump's own type-agnostic, globally-deduplicated member-line block
(`PublicApiDump.cs.txt`) — a content-based set diff cannot reproduce that failure mode, since
inserting new sorted lines elsewhere never makes an unrelated existing line look "removed."

New stable baseline: `Tools~/Verification/P7/Audit/Baseline/public-api-baseline.txt`, seeded from
`Planning~/Evidence/P7-GATE/public-api.txt` — confirmed live first (via `mcp__unityMCP__execute_code`
reflecting the real open `Modules@783f0d4fd8687b7b` Editor instance with `PublicApiDump.cs.txt`'s own
exact algorithm) to still be byte-identical to today's real compiled surface (425 types / 2130
members, unchanged since `P7-016`, since none of `P7-017`/`P7-019`/`P7-021`/`P7-022` touched a public
C# member).

Both Acceptance Criteria were proven through the **real, full mechanism** (not a shortcut): a real
end-to-end run of the modified script through its actual isolated-Unity-harness path, once with the
real baseline (additive/no-op case — passed cleanly, `Public API check passed: no removals or
renames vs baseline.`), and once against a temporary, uncommitted copy of the baseline with one
synthetic extra line (`TYPE AIBT.SyntheticProbe.ThisTypeCannotExist`, never a real symbol) — failed
loudly with that exact line named, exit code 1. The synthetic-baseline-line technique was chosen over
temporarily renaming a real production symbol: it exercises the identical detection logic (a missing
baseline line is a missing baseline line, regardless of why) without any risk of an accidental
leftover rename in the codebase. See `Planning~/Evidence/P7-020/README.md`.
