# P7-020 CI-enforced public API diff check — evidence

## Result

Done. `Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1` gained an optional `-BaselinePath`
parameter that fails loudly on any public-API removal or rename, wired into `.github/workflows/
validation.yml`'s existing `unity` job.

## What the card got wrong, found before implementing

Its Forbidden-changes clause said not to require a Unity job for this check "if it can run from the
same headless dump technique `Get-FullPublicApi.ps1` already uses on `windows-2022`." Re-checked
rather than assumed:

- `Get-FullPublicApi.ps1`'s own code comment: plain-host reflection was deliberately abandoned
  because "reflecting over Unity-Mono-compiled netstandard2.1 assemblies from plain Windows
  PowerShell 5.1 cannot reliably resolve netstandard/Unity BCL dependencies" — the dump only works
  by launching a real, licensed Unity Editor in batch mode (`-executeMethod PublicApiDump.Run`)
  inside a disposable isolated project.
- `.github/workflows/validation.yml`'s `static` job (`runs-on: windows-2022`, GitHub-hosted) has no
  Unity installed anywhere — confirmed by grepping the whole `.github/`/`Tools~/` tree for
  `unity-builder`/`game-ci`/`UNITY_LICENSE`/`GameCI`: no matches. This repository's CI never installs
  or activates Unity on a GitHub-hosted runner.
- The only job with Unity access is `unity` (`runs-on: [self-hosted, Windows, X64,
  unity-6000.5.8f1]`), gated on `P0-005`'s still-unresolved self-hosted runner — reconfirmed
  (consistent with every prior session's live GitHub API check) that it has never once picked up a
  queued job.

So a genuinely fresh, per-PR dump cannot run on `windows-2022` in real CI today, contrary to the
card's own text.

## Resolution

Matching `P7-015`'s own precedent (disclose a real gap rather than fake a Unity CI gate that cannot
run): the new check is a **new step inside the existing `unity` job**, immediately after "Run full
EditMode suite," not a second separate job. This adds no *new* dependency on the blocked runner — that
job already requires Unity for compile + EditMode — and, exactly like the rest of that job, this new
step is unproven in real GitHub Actions until `P0-005` closes. Disclosed, not hidden.

## Comparison logic — avoiding a known trap

`Planning~/Evidence/P7-GATE/README.md` records that a *positional/textual* diff of a `public-api.txt`
dump previously produced a false "5 removed members" signal, root-caused to the dump's own
type-agnostic member-list format (`PublicApiDump.cs.txt`'s `memberLines` block is a single global,
deduplicated, sorted set of signature strings with no per-type association — inserting new sorted
types/members elsewhere in the file can shift what a positional diff tool reports as "removed" even
when nothing real changed).

The new `-BaselinePath` logic uses PowerShell `Compare-Object`, whose default behavior is a
**content-based set comparison**, not positional: it only flags a line that exists in the baseline
and is genuinely absent from the fresh dump. New lines (additions) are logged as informational only
and never fail the check — this directly satisfies the card's own Forbidden-changes clause (purely
additive changes must never fail CI).

## New stable baseline

`Tools~/Verification/P7/Audit/Baseline/public-api-baseline.txt` — seeded as a byte-identical copy of
`Planning~/Evidence/P7-GATE/public-api.txt` (the most recently accepted gate's own dump). Confirmed
live first, not assumed: a fresh reflection over the real, open `Modules@783f0d4fd8687b7b` Unity
instance's 4 public assemblies, using `PublicApiDump.cs.txt`'s own exact algorithm via
`mcp__unityMCP__execute_code`, produced 425 types / 2130 members — an exact, byte-identical match to
`Planning~/Evidence/P7-GATE/public-api.txt` (confirmed via `diff`, unsorted). Nothing since `P7-016`
(`P7-017`/`P7-019`/`P7-021`/`P7-022`) touched a public C# member, so the seeded baseline is current.
Future gates update this file deliberately (copy the new gate's own dump over it) — never on every PR.

## Verification

All three cases were proven, and the two required by Acceptance Criteria were proven through the
**real, full mechanism** end-to-end, not only the fast logic shortcut:

1. **Fast logic checks** (pure PowerShell `Compare-Object`, no Unity — the comparison logic has no
   Unity dependency) against dumps obtained live via `mcp__unityMCP__execute_code`:
   - Positive: real current dump vs. baseline — `missing=0 added=0`.
   - Additive: a synthetic new line added to the fresh side — `missing=0 added=1` (does not fail).
   - Negative: a synthetic extra line added to a temporary, uncommitted baseline copy — `missing=1`,
     naming exactly `TYPE AIBT.SyntheticProbe.ThisTypeCannotExist`.
2. **Real end-to-end runs** of the actual modified `Get-FullPublicApi.ps1 -BaselinePath ...` through
   its real isolated-Unity-harness path (the exact mechanism CI will invoke):
   - Positive run (real baseline): `Public API check passed: no removals or renames vs baseline.`
     Clean exit.
   - Negative run (temporary, uncommitted synthetic-baseline-line copy, deleted immediately after):
     `Public API REMOVED or RENAMED since baseline (1): - TYPE AIBT.SyntheticProbe.ThisTypeCannotExist`,
     script threw, exit code 1.
   - The synthetic-baseline-line technique was chosen deliberately over temporarily renaming a real
     production symbol: the detection logic doesn't care *why* a baseline line is missing from the
     fresh dump, so this proves the same code path with zero risk of an accidental leftover rename in
     the codebase.
3. `Tools~/Verification/Verify-Static.ps1` — passed after the Planning~ edits.

## Scope and limitations

- The new `validation.yml` step itself has not been exercised by real GitHub Actions — identical to
  every other step in the `unity` job, blocked on `P0-005`'s self-hosted runner never picking up a
  job. Disclosed here rather than claimed as proven CI coverage.
- Transient verification artifacts (`Tools~/Verification/P7/Audit/Results/`, produced by the real
  end-to-end runs above) were deleted, not committed — regeneratable, not themselves evidence, per
  `AGENTS.md`'s evidence-artifact-size discipline.
