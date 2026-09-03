# P7-021 API-reference generator package-root resolution fix evidence

## Result

Done. `MCP/Documentation/McpApiReferenceGenerator.cs`'s `CollectTypeSummaries()` no longer hardcodes
`Path.Combine(Application.dataPath, "AIBT")` as its source-scan root. It now mirrors
`Tests/Editor/Documentation/McpDocumentationGeneratorsTests.FindGeneratedDocumentationDirectory()`'s
own already-correct pattern: `UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(
McpApiReferenceGenerator).Assembly)`, using `resolvedPath` when Package Manager knows about the
assembly, falling back to the `Application.dataPath` assumption only when it does not (this
repository's own host-embedded dev layout). No change to the summary-matching regex/logic itself,
per the card's own Forbidden-changes clause — only *where* the scanner looks. `CollectTypeSummaries()`
was widened from `private` to `internal` so a regression test can call it directly (`AIBT.Mcp`
already grants `InternalsVisibleTo` to the test assembly — confirmed, since the test file already
calls `McpApiReferenceGenerator.Generate()`, itself `internal`, directly).

## Verification

- **Host-project regression, scoped to AIBT's own 12 test assemblies**: 1271 tests, only the 2
  already-known, pre-existing, unrelated `AIBT.CodeGen.GenerationTests
  .GeneratedArtifactContractTests` failures (see "Related finding" below) — zero new failures, the
  new pinning test included.
- **New test, `CollectTypeSummariesFindsARealKnownSummaryInThisHostEmbeddedLayout`**: asserts
  `CollectTypeSummaries()` still finds `AIBT.HotReloadClassificationResult`'s real `<summary>` text
  correctly in this host-embedded layout (the exact type `P7-016`'s gate found missing when the old
  resolution regressed) — passes, pinning the fallback branch against a future accidental revert.
- **Live detached-harness proof (the branch a plain host-project test cannot exercise)**: a fresh
  Unity project referencing `com.azzazello.aibt` via a real `file:` package pointing at the live
  host project's own `Assets/AIBT` (not embedded under the harness's own `Assets/`, so
  `PackageInfo.FindForAssembly` resolves it for real, exactly the scenario every real UPM consumer
  hits). Compile: exit 0. Full EditMode regression:
  `McpDocumentationGeneratorsTests.GeneratedDocumentationRegeneratesToExactlyTheCommittedFiles`
  **now passes** — the one real, disclosed failure in `P7-016`'s own gate result. See
  `verification-results.json` for the exact counts.
- **No committed generated doc changed.** As predicted by the fix's own design (the host project
  always takes the `Application.dataPath` fallback branch, identical to before), regenerating inside
  the host project produces byte-identical `api-reference-*.md` content to what was already
  committed — confirmed by `git status`/`git diff` showing zero changes to
  `Documentation~/generated/` from this card.

## Related finding, disclosed, out of scope for this card

`Tests/Editor/CodeGen/Generation/GeneratedArtifactContractTests.cs` (unrelated file, not touched by
this card) has its own, independent `Assert.That(PackageInfo.FindForAssembly(...), Is.Not.Null, ...)`
check, which fails in this same host-embedded layout for the same underlying reason
`McpApiReferenceGenerator`'s bug did — confirmed pre-existing (`git log` shows its last change,
`360bbe7`, is unrelated to this card) and confirmed environment-dependent, not a regression: it
passed in `P7-016`'s own detached-harness gate run (2/2 tests, no CodeGen failures in that 1270-test
result) and fails only here in the host project. Not fixed here — a different file, outside this
card's own Allowed-changes fence — but worth a future owner note that the same class of bug exists
in at least one more place.

## Scope and limitations

- The `PackageInfo`-non-null (real UPM consumer) branch cannot be unit-tested in a plain host-project
  EditMode run — Unity's `PackageInfo` can't be cheaply faked without registering a real package.
  Proven instead via the live detached-harness run above, matching this card's own Required
  Verification and Acceptance Criteria text exactly ("proven live, not assumed").

See `verification-results.json` for exact commands and results.
