# P3-007 layout/semantic isolation proof evidence

## Result

- `Tests/Editor/Layout/LayoutSemanticIsolationTests.cs` (2 tests, both passing), test-only per
  this card's `Allowed changes` (no production code touched):
  - `EveryManualOrganizationActionAndAutoLayoutLeaveTheCompiledProgramByteIdentical` — compiles a
    representative fixture tree, records `Program.Header.CompiledContentHash`, then exercises
    every named manual-organization action kind (pin, group, comment/sticky-note — one
    `LayoutNote` call, since `editor-layout-v1.md` models both as the same schema concept — and
    reroute) plus a `P3-004` auto-layout re-run, all through `P3-005`'s
    `LayoutOrganizationOperations`/`DeterministicAutoLayoutService`, recompiles the *same,
    untouched* `TreeDocument`, and asserts the content hash is unchanged.
  - `AGenuineSemanticEditDoesChangeTheCompiledProgram` — applies a real `P3-006`
    `SemanticEditOperations` edit (disconnect + remove a node) and asserts the content hash
    *does* change, proving the comparison mechanism can actually detect a difference (not
    vacuously true).
- Uses the existing `CompiledProgram.Header.CompiledContentHash` field
  (`Authoring/Compilation/CompiledContentHasher.cs`) as the byte-identity proxy — the same
  mechanism `Tests/Editor/Compilation/ReferenceCompilerTests.cs` already relies on for its own
  content-hash stability assertions, not a new comparison method invented for this card.
- New fixture `Tests/Editor/Layout/Fixtures/isolation-proof.aibt.json`, built entirely from
  already-bound node types (`aibt.core.memory-sequence`, `aibt.core.inverter`,
  `aibt.test.success`, `aibt.test.failure`) so it actually compiles — none of the earlier
  `Editor/Graph`/`Editor/Editing` test fixtures using `sample.*`/`aibt.core.test-leaf`
  `UserExtension` types would compile (see `P3-006`'s evidence: Phase 1 cannot execute
  `UserExtension`-sourced node types).

## Decision

No new decision. The invariant held on first measurement — no bug was found in `P3-004`/`P3-005`/
`P3-006`'s existing separation, so there was nothing to report against another card.

## Scope and limitations

- The positive case's structural guarantee is also enforced at the type level: every
  `LayoutOrganizationOperations`/`DeterministicAutoLayoutService` signature takes and returns only
  `LayoutDocument`, never `TreeDocument` — `ReferenceCompiler.Compile` has no way to observe layout
  data at all. This test is a runtime regression guard for that invariant (per the card's "as an
  automated test rather than a review convention" requirement), not proof that the type-level
  separation itself is unbreakable; it will catch a future regression where a compiler code path
  is accidentally given access to layout data, which is exactly the scenario worth guarding
  against continuously in the standard EditMode suite.
- Environment note (unrelated to the test content, recorded for future sessions in this
  workspace): mid-task, an interactive Unity Editor was opened locally to connect a Unity MCP
  bridge. That Editor instance holds `Temp/UnityLockfile`, and headless
  `-batchmode -quit` compiles/tests against the same project path fail silently (exit code 1, no
  compiler error in the log) while it holds the project. Verification for this card required
  closing that Editor first.

See `verification-results.json` for exact commands and results.
