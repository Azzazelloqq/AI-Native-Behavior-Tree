# P7-015 release automation evidence

## Result

The last directly-assignable Phase 7 card before the `P7-016` gate: repeatable release automation,
built local-first (mirroring `Verify-Static.ps1`/`validation.yml`'s own existing relationship)
because `P0-005`'s self-hosted `unity-6000.5.8f1` runner is still genuinely blocked — reconfirmed
live today (2026-09-03) via the GitHub REST API before any code was written: the most recent
`Validation` run's Unity job (`33734103961`) sat `queued, runner_id: 0` since dispatch, and every
prior run's Unity job has never actually run either.

- **`Tools~/Verification/P7/Release/Verify-ReleaseReadiness.ps1`** (new): takes `-TargetVersion`
  (required) and optional `-RepositoryPath` (same convention as `Verify-Static.ps1`). Throws with a
  clear message on the first failure — never partial success — checking, in order: `package.json`
  and `-TargetVersion` are both valid semver; `-TargetVersion` is strictly greater than
  `package.json`'s current version; `CHANGELOG.md`'s first heading is `## [Unreleased]`;
  `CHANGELOG.md` has no existing `## [<TargetVersion>]` heading already (not already released);
  `CHANGELOG.md`'s `[Unreleased]` section is non-empty; no git tag `v<TargetVersion>` already
  exists. On success, prints a dry-run summary (current → target version, the changelog section that
  would move, the tag name that would be created) and exits 0. Never mutates `package.json` or
  `CHANGELOG.md` itself — that is `release.yml`'s own later, explicit `publish` job, not this
  script's job.
- **`.github/workflows/release.yml`** (new): `workflow_dispatch`-only (never automatic), with a
  required `version` input and a required `confirm_local_editmode_passed` boolean input (no
  default) that the workflow fails loudly on if not explicitly checked — its own header comment and
  the `readiness` job's first step both state plainly, in this exact case, why a full Unity
  EditMode gate is not included. Three `windows-2022` jobs, no self-hosted runner used anywhere:
  `readiness` (confirm the EditMode input, then `Verify-ReleaseReadiness.ps1`) →
  `static` (`Verify-Static.ps1`, mirroring `validation.yml`'s own job) →
  `publish` (`contents: write` only on this one job; bumps `package.json`'s version, moves
  `CHANGELOG.md`'s `[Unreleased]` content under a new `## [<version>] - <date>` heading leaving a
  fresh empty `[Unreleased]` above, commits both files, tags `v<version>`, pushes the branch and
  tag, creates a GitHub Release via the runner-preinstalled `gh` CLI using
  `${{ secrets.GITHUB_TOKEN }}` — no credential or token embedded in the repository). Pinned action
  SHAs reused verbatim from `validation.yml` (`actions/checkout@11bd719...` v4.2.2,
  `actions/upload-artifact@658462...` v4.3.3) — no new floating-tag action introduced.

## Verification

- **`Verify-Static.ps1`**: passed (`ok -- validation done` x3, 6 schemas, 122 work items) —
  confirms this card's own `work-items.json`/task-card edits stay well-formed.
- **`Verify-ReleaseReadiness.ps1`, positive case, run live**: `-TargetVersion "0.2.0"` against the
  real repository (`package.json` at `0.1.0`, `CHANGELOG.md` entirely under `[Unreleased]`) printed
  `Release readiness OK: 0.1.0 -> 0.2.0.` plus the correct "would move"/"would create tag" lines and
  the full real `[Unreleased]` body, exit 0.
- **`Verify-ReleaseReadiness.ps1`, negative case (version-consistency failure), run live**:
  `-TargetVersion "0.0.1"` (not strictly greater than the current `0.1.0`) failed loudly:
  `TargetVersion '0.0.1' must be strictly greater than package.json's current version '0.1.0'.`,
  nonzero exit. This is this card's own proof of the acceptance criterion "fails loudly if
  `CHANGELOG.md` and `package.json` versions disagree" — read as: the target version being released
  must be consistent with both `package.json`'s current version and `CHANGELOG.md`'s already-released
  headings (there is no separate "version" field in `CHANGELOG.md` before release to disagree with,
  since `[Unreleased]` carries no version number by construction — this reading is disclosed, not
  silently assumed).
- **`Verify-ReleaseReadiness.ps1`, negative case (invalid semver), run live**:
  `-TargetVersion "not-a-semver"` failed loudly: `TargetVersion ('not-a-semver') is not a valid
  semver X.Y.Z version.`, nonzero exit.
- **`release.yml` itself: not dispatched for real this session** — doing so would push a real git
  tag and create a real public GitHub Release, a hard-to-reverse, publicly-visible action requiring
  its own separate, explicit ask distinct from building the automation. The card's own acceptance
  criteria explicitly accepts "a local equivalent script" for the dry-run requirement; the three live
  `Verify-ReleaseReadiness.ps1` runs above are that equivalent. The workflow YAML was parsed with
  PyYAML (`uv run --with pyyaml python -c "yaml.safe_load(...)"`) and confirmed syntactically valid;
  its job/step logic was not executed end-to-end on a runner.

## Decision

No new architectural decision. One real implementation judgment made without escalation: since
`CHANGELOG.md` never carries an explicit version number before release, "CHANGELOG/package.json
version disagreement" was implemented as the combination of (a) target version not strictly greater
than `package.json`'s current version, and (b) target version already present as a `CHANGELOG.md`
heading — both proven live above — rather than a literal version-string field comparison that does
not exist in this repository's actual `CHANGELOG.md` structure.

## Scope and limitations

- **No full Unity compile/EditMode gate in `release.yml`** — disclosed plainly in the workflow's own
  header comment and enforced via the required, no-default `confirm_local_editmode_passed` input,
  per `P0-005`'s still-unresolved status (reconfirmed live today, not from memory). Per the task
  card's own Handoff notes, this should be widened to a real Unity job once `P0-005` closes.
- **`release.yml` has never been dispatched for real** — no tag, no GitHub Release, no version bump
  has actually happened. `package.json` remains `0.1.0`; `CHANGELOG.md` remains entirely under
  `[Unreleased]`. Cutting a real first release (`1.0.0` or otherwise) remains the owner's own
  decision per `Planning~/USER_ACTIONS.md` — this card builds the mechanism, it does not use it.
- **`publish` job's commit/tag/push/release-create logic was not exercised live** — only its
  read-only sibling (`Verify-ReleaseReadiness.ps1`) was. It mirrors patterns already proven
  elsewhere in this project (`git` invocations follow `Verify-ReleaseReadiness.ps1`'s own
  established `-LiteralPath`/`$ErrorActionPreference = 'Stop'` conventions; `gh release create` is
  the runner-preinstalled CLI, not a new dependency) but is disclosed as unexecuted rather than
  silently assumed correct.

See `verification-results.json` for exact commands and results.
