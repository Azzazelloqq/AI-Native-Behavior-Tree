# P7-021 — API-reference generator package-root resolution fix

Status: `Draft`

## Objective

`P7-016`'s detached-UPM-harness regression (the same clean, `file:`-package-referenced environment
every gate since `P6-012` uses to prove production-readiness) found a real, previously-undetected
bug: `MCP/Documentation/McpApiReferenceGenerator.cs`'s `CollectTypeSummaries()` hardcodes
`Path.Combine(Application.dataPath, "AIBT")` as the source-scan root for correlating a type's own
`<summary>` XML-doc comment. This assumption only holds when AIBT is embedded directly under a host
project's `Assets/` folder (this repository's own dev setup) — it silently resolves to a
non-existent directory for any real `file:`/registry UPM consumer (the package lives under
`Packages/`, never `Assets/`), so `CollectTypeSummaries()` returns an empty dictionary and every
generated `api-reference-*.md` loses 100% of its inlined type summaries, with zero error or warning.
`Tests/Editor/Documentation/McpDocumentationGeneratorsTests
.GeneratedDocumentationRegeneratesToExactlyTheCommittedFiles` caught this live — the committed
`api-reference-runtime.md` (generated inside the host project, summaries intact) does not match a
fresh regeneration inside `P7-016`'s own detached harness (summaries silently dropped).

## Depends on

- `P7-014` (the generator this card fixes).
- `Planning~/Evidence/P7-GATE/known-limitations.md` (this gate's own record of the finding, with the
  exact failing assertion).

## Required reading

- `MCP/Documentation/McpApiReferenceGenerator.cs`'s `CollectTypeSummaries()` (the bug) and
  `Tests/Editor/Documentation/McpDocumentationGeneratorsTests.FindGeneratedDocumentationDirectory()`
  (the *already-correct* sibling technique for resolving the package's real root via
  `UnityEditor.PackageManager.PackageInfo.FindForAssembly`/`GetAllRegisteredPackages` — mirror this,
  don't reinvent a second resolution strategy).

## Allowed changes

- `MCP/Documentation/McpApiReferenceGenerator.cs` — replace the hardcoded `Application.dataPath`
  assumption with the same `PackageManager`-based resolution the test helper already uses
  correctly.
- `Documentation~/generated/api-reference-*.md` — regenerated once the fix lands, if their content
  changes (it should not, when run inside the host project where the bug was invisible; it should
  change, correctly, when regenerated inside a detached harness).
- `Tests/Editor/Documentation/` — a new test proving `CollectTypeSummaries()` resolves correctly
  from *outside* `Application.dataPath` (e.g. a temp-directory fixture, or run inside a detached
  harness as this gate's own finding did), so this exact regression cannot silently reappear.
- `Planning~/Evidence/P7-021/`.

## Forbidden changes

- Changing which types get a `<summary>` correlated (the matching regex/logic itself) — this card
  fixes *where* it looks, not *how* it matches.

## Deliverables

- `CollectTypeSummaries()` resolves the package root the same way regardless of whether AIBT is
  embedded under a host project's `Assets/` or consumed as a real `file:`/registry UPM package.
- A regression test proving this specifically (the gap `P7-014`'s own original test suite never
  covered, since it only ever ran inside the host project).

## Acceptance criteria

- `Tests/Editor/Documentation/McpDocumentationGeneratorsTests
  .GeneratedDocumentationRegeneratesToExactlyTheCommittedFiles` passes when run inside a detached
  UPM harness (`file:` package reference), not only inside the host project — proven live, not
  assumed from the host-project run alone.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Full, run inside a detached UPM harness (not just the host project)
```

## Handoff notes

- Spun off from `P7-016`'s own gate session (2026-09-03) — found by the gate's own detached-harness
  regression, not fixed inside the gate task per its own Forbidden-changes clause. Not required for
  `P7-016`'s own verdict; the gate discloses the exact failing assertion and this card's number
  rather than silently patching around it or hiding the one real test failure.
