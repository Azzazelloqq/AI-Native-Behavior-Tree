# P7-015 — Release automation

Status: `Draft`

## Objective

Add repeatable release automation (`Documentation~/roadmap.md`'s Phase 7 scope): versioning,
`CHANGELOG.md` discipline, and a UPM package-publish workflow, extending
`.github/workflows/validation.yml`'s own existing pattern rather than inventing a second CI system.
Only `validation.yml` exists today (confirmed by listing `.github/workflows/` before this card was
written) — no release/tag/publish workflow exists yet.

## Depends on

- `P0-002` (repeatable verification entrypoints; release automation must call the same local
  commands CI already uses, per `Planning~/AGENT_WORKFLOW.md`'s "local verification entrypoints
  remain the source of CI commands" rule, inherited from `P0-005`'s own acceptance criterion).

## Required reading

- `.github/workflows/validation.yml` (the existing pattern: pinned actions, no secrets beyond
  provider secret names, sanitized artifact retention — a release workflow must match this
  discipline, not relax it).
- `CHANGELOG.md`'s own "Keep a Changelog"-style structure and its `[Unreleased]` section.
- `package.json`'s own version field and `com.unity.nuget.newtonsoft-json`-style dependency pinning
  (a release must bump this consistently, not just tag git).

## Allowed changes

- `.github/workflows/release.yml` (new).
- `Tools~/Verification/P7/Release/` (new, if a local dry-run script is needed before the workflow
  can be trusted — mirror `Tools~/Verification/P0/` and `P2/`'s own local-first discipline).
- `CHANGELOG.md` (process/structure only — moving `[Unreleased]` entries under a real version
  heading is part of the release process this card automates, not a one-time edit).
- `Planning~/Evidence/P7-015/`.

## Forbidden changes

- Embedding any credential, token, or Unity license file in the repository or workflow — restated
  explicitly from `P0-005`'s own Forbidden-changes clause, since a release workflow is exactly the
  kind of thing that tempts embedding a publish token directly.
- Depending on the still-unresolved `P0-005` self-hosted-runner gap for any release step that does
  not actually need a full Unity Editor (e.g., package version bump, changelog validation, git tag,
  UPM registry publish of already-committed source) — scope this card's own workflow to what is
  achievable without a Unity job if `P0-005` remains blocked when this card is assigned, and
  disclose plainly which release steps still need it (e.g., a pre-publish full-regression run).
- Making any public API or persisted-format change as part of "preparing a release" — that is
  `P7-001`'s own scope, not this card's.

## Deliverables

- A `release.yml` workflow (manually triggered, `workflow_dispatch`) that: validates
  `CHANGELOG.md`/`package.json` version consistency, runs the existing static verification job,
  creates a git tag, and publishes release notes generated from the changelog's own newly-versioned
  section.
- A documented, explicit list of what this workflow does NOT yet cover (e.g., a full Unity
  compile/EditMode gate, if `P0-005`'s runner is still unresolved) rather than a workflow that
  silently skips a step and calls itself complete.

## Acceptance criteria

- A dry run of the workflow (via `workflow_dispatch` on a throwaway branch/tag, or a local
  equivalent script) produces a correct, sanitized release artifact set without requiring any
  secret beyond a provider-managed token referenced by name.
- The workflow fails loudly (not silently) if `CHANGELOG.md` and `package.json` versions disagree.
- Explicit, disclosed scope: this card states plainly whether its own release gate includes a full
  Unity compile/EditMode run, and if not, why (citing `P0-005`'s own status at the time).

## Required verification

```text
Verify-Static.ps1
dry-run of release.yml (workflow_dispatch on a throwaway ref, or an equivalent local script)
version-consistency failure case proven (deliberately mismatched CHANGELOG/package.json version)
```

## Handoff notes

- If `P0-005` closes before or during this card, its release gate should be widened to include the
  real Unity compile/EditMode job rather than left permanently scoped around the gap.
