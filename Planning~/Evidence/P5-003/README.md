# P5-003 compatibility classifier evidence

## Result

- `Runtime/HotReload/Classification/HotReloadNodeVerdictCategory.cs`,
  `HotReloadNodeVerdict.cs`, `HotReloadClassificationResult.cs`,
  `HotReloadCompatibilityClassifier.cs` (new): implements `ADR-P5-001`'s per-node classification
  (`Migrate`/`New`/`Dropped`/`IncompatibleRestart`), structural direct-child-order-change detection
  (a purely structural fact, agnostic of which node types are Memory composites -- deciding whether
  that fact requires a cursor reset is left to the caller, per the ADR's own scoping), and subtree
  localization with a conservative shared-blackboard-write safety escalation.
- `HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram)` is pure and side-effect-free:
  no live tree instance, no restart, no migration -- exactly this card's own scope boundary.
- `Tests/Editor/HotReload/Classification/HotReloadCompatibilityClassifierTests.cs` (new, 8 tests,
  all passing): null-rejection, all five `testing.md` categories (parameter edit, insertion,
  removal, reordering, type change), the "changed ancestor sweeps an otherwise-unchanged descendant
  into its restart region" proof, root-level type change forcing `RequiresFullRestart`, and the
  shared-blackboard-write escalation rule.

## A real defect this card's own test-writing found and fixed

Building the type-change test, `TypeChange_NodeIsIncompatible_DescendantSweptIntoRestartRegion_RootIsJustTheChangedNode`
initially made the changed decorator the tree's own root, so `RequiresFullRestart` was correctly
`True` -- but the test asserted `False`, expecting to prove *localized* restart. This was a test
construction bug, not a classifier bug: fixed by wrapping the decorator under a non-changing
`MemorySequence` root, which is what actually exercises localization. Caught immediately by running
the test live rather than trusting the assertion's intent.

## A real platform limitation found while testing the shared-blackboard escalation rule

`SharedBlackboardWriteInsideCandidateRegion_EscalatesToFullTreeRestart` could not be built through
the real `ReferenceCompiler`: compiling a tree with a `Shared`-scope blackboard write fails with
`AIBT2030`/`AIBT2032` -- "Shared-scope writes require a deterministic reduction policy not
available in Phase 1," regardless of which `BlackboardReductionKind` is declared. This is a real,
current authoring-compiler-policy restriction (not fixable by test code), independent of the
compiled-program *format* itself, which does support a `Shared`-scope, write-flagged
`CompiledBlackboardSlotRecord` (the runtime-level representation has no such restriction). Since
`HotReloadCompatibilityClassifier` only ever reads `CompiledProgram` data and has no dependency on
how it was produced, this test instead hand-constructs a fully-validated `CompiledProgram` directly
(satisfying every one of `CompiledProgram`'s own constructor invariants -- header counts,
non-overlapping ranges, debug-map back-references) rather than skipping the category or weakening
the classifier's own conservative rule to match today's authoring limitation.

## Design note: `NodeVerdicts` vs. `RestartSubtreeRootNodeIds` is a deliberate two-signal design

A node nested under an `IncompatibleRestart` ancestor still reports its own honest per-node
comparison in `NodeVerdicts` (e.g. `Migrate`, if it did not itself change) -- `RestartSubtreeRootNodeIds`
is the signal that overrides this for any node inside a restarting region. `P5-004`/`P5-005`/`P5-006`
must combine both signals, not read `NodeVerdicts` alone; `HotReloadCompatibilityClassifierTests.TypeChange_NodeIsIncompatible_DescendantSweptIntoRestartRegion_RootIsJustTheChangedNode`
proves and documents this explicitly (a child's own `Migrate` verdict does not mean it is safe to
migrate once its ancestor's own type changed).

## Verification

Live Unity MCP test run: 8/8 passed (classifier), 6/6 still passing (`P5-002`'s identity tests,
re-run for regression). Full suite: 1431 tests, same 3 pre-existing unrelated failures as every
prior evidence file. `Verify-Static.ps1`: 83 work items, unchanged. Full detail in
`verification-results.json`.

## Scope and limitations

- The composite-cursor-reset rule (`ADR-P5-001`) is reported as a structural fact
  (`StructuralChildChangeNodeIds`) only; deciding which node TYPES actually need a cursor reset
  from that fact is deferred to whichever card owns composite-handler semantics knowledge
  (`P5-004`/`P5-005`/`P5-006`), since this classifier deliberately does not couple itself to the
  composite-handler type registry.
- The shared-blackboard-write escalation is a conservative over-approximation (any Shared-scope
  write anywhere in the candidate region disqualifies localization), not a full data-flow analysis
  proving the value is actually read from outside the region. This trades some restart-scope
  precision for guaranteed safety, consistent with this codebase's "when unsure, restart rather
  than guess" discipline -- disclosed, not silently assumed correct in the general case.
- No production code touches a live tree instance from this card.
