# P5-003 — Compatibility classifier

Status: `Draft`

## Objective

Implement `P5-001`'s decided compatibility-classification rule as an
explicit, testable function over two `CompiledProgram`s (old, new): which of
`testing.md`'s five change categories occurred, whether the result is
compatible or incompatible, and if compatible, whether full-restart,
subtree-restart, or migration applies. This card classifies; it does not
perform any restart or migration itself.

## Depends on

- `P5-002` (identity/version/state-layout-hash data this classifier reads).

## Required reading

- `P5-001`'s accepted ADR (the exact decision table this card implements).
- `Documentation~/hot-reload.md`'s "Compatibility classification" section.
- `Documentation~/testing.md`'s "Hot-reload tests" section.

## Allowed changes

- `Runtime/HotReload/Classification/` (new, or wherever `P5-001`'s ADR
  places it).
- `Tests/Runtime/HotReload/Classification/` (new).

## Forbidden changes

- Any restart or migration mechanism (`P5-004`/`P5-005`/`P5-006`).
- Reinterpreting or narrowing `P5-001`'s decided classification table --
  if a real case reveals the table is wrong or incomplete, escalate back to
  `P5-001`'s ADR rather than silently patching the classifier to disagree
  with its own accepted decision.

## Deliverables

- A pure function: `(oldProgram, newProgram) -> ClassificationResult`, where
  `ClassificationResult` states the detected change category (or categories,
  if more than one applies), the compatible/incompatible verdict, and the
  recommended strategy per `P5-001`'s decision tree.
- Localization data sufficient for `P5-005` (affected-subtree restart) to
  identify which subtree(s) an incompatible change touched, when the change
  is localizable.
- A structured, inspectable "why" for every verdict -- the same
  explainability discipline `execution-and-scheduling.md` requires of
  scheduler decisions applies to reload decisions too
  (`Documentation~/hot-reload.md`'s "Editor workflows" section).

## Acceptance criteria

- Every one of `testing.md`'s five change categories has a real
  `CompiledProgram`-pair test fixture (not synthetic-toy data) and an
  asserted verdict matching `P5-001`'s ADR exactly.
- A change combining two categories at once (e.g., an insertion plus a
  parameter edit elsewhere) classifies according to the ADR's stated
  combination rule, not an unstated default.
- Every verdict carries a stated reason inspectable in tests, not just a
  boolean.
- No classification result is ever silently defaulted to "compatible" when
  the classifier cannot determine the category with confidence -- an
  unrecognized or ambiguous change classifies incompatible (full restart),
  per the safety ordering `Documentation~/hot-reload.md`'s "Reload
  strategies" section states.

## Required verification

```text
Verify-Static.ps1
Run-UnityTests.ps1 -Mode EditMode -Scope Focused -TestFilter <classifier fixture>
one real CompiledProgram-pair fixture per testing.md category, plus at least one combined-category case
```

## Handoff notes

- `P5-004`, `P5-005`, and `P5-006` each consume this card's verdict directly
  to decide which strategy to run; none of them re-derive compatibility
  themselves.
- `P5-008` (editor workflow) surfaces this card's "why" output to the user
  verbatim -- keep the reason text meaningful outside a test-assertion
  context, not just a debug enum name.
