# P6-017 — Per-project leaf-node registration mechanism decision

Status: `Draft`

## Objective

Decide whether and how to let a consuming project register its own reference-executor leaf
behavior (`IReferenceLeafHandler` implementations, matching `NodeManifest`/observer-condition
registrations) so tools built on the reference executor can drive real, project-authored trees --
not just the fixed Phase 1 fixture/built-in set every such tool is limited to today.

This card exists because the same limitation has now been independently disclosed by four
different cards: `P3-009`'s `ReferencePreviewFixtureEnvironment` ("AIBT ships no production
per-project leaf-behavior registration mechanism yet... every real leaf handler in the repository
today is a test fixture"), `P6-007`'s `simulate` tool (inherits the identical fixture set), and
`P6-008`'s `run-tests` tool (`AuthoringBehaviorCaseExecutorFactory`, built this session, explicitly
reuses `ReferencePreviewFixtureEnvironment` rather than inventing a fourth copy of the same
limitation). A real project tree using anything beyond built-in composites/decorators plus
`aibt.test.success`/`failure`/`running` cannot be previewed, simulated, or test-run through any
MCP or Editor tool that exists today, even though it compiles and validates fine for real
authoring.

## Depends on

- `P3-009` (done -- owns `ReferencePreviewFixtureEnvironment`, the fixed set this card would
  extend or replace).
- `P6-007` (done -- `simulate`, a consumer of the same fixed set).
- `P6-008` (done -- `run-tests`, a second consumer via `AuthoringBehaviorCaseExecutorFactory`).

## Required reading

- `Authoring/Execution/ReferencePreviewFixtureEnvironment.cs` and
  `Authoring/BehaviorCases/AuthoringBehaviorCaseExecutorFactory.cs` -- both current consumers of
  the fixed fixture set; both would need to accept a real registry instead once this decision
  ships.
- `Runtime/Execution/Reference/` -- `ReferenceLeafRegistry`, `IReferenceLeafHandler`,
  `ReferenceObserverConditionRegistry` -- the actual extension points a per-project mechanism
  would populate.
- `P1-004`'s node-registry decision and `Authoring/Registry/` -- the existing manifest-side
  registration mechanism (`NodeRegistryBuilder`), to confirm whether leaf *behavior* registration
  should follow the same discovery pattern or is a genuinely different concern (manifests are
  metadata; leaf handlers are executable code).
- `P6-010`'s task card (`Draft`, not yet implemented) -- its "Custom MCP tools" IoC discovery
  pattern (attribute/interface implemented by the consumer, discovered by the host, never a
  hardcoded reference) is a plausible template for this card's own discovery mechanism; confirm
  whether reusing it is appropriate or whether leaf registration has different constraints (e.g.
  Burst/native compatibility) that rule it out.

## Allowed changes

- `Spikes~/PerProjectLeafRegistration/` (new, disposable) -- proves the recommended discovery/
  registration design against the real `ReferenceExecutionMachine`, mirroring `P6-002`'s own
  spike-before-ADR methodology.
- `Planning~/Evidence/P6-017/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `Authoring/Execution/`, `Runtime/Execution/Reference/`, or either
  current consumer (`P6-007`/`P6-008`) -- this card decides on paper; a separate future card
  implements it.
- Assembly scanning in player/runtime code, consistent with `P1-004`'s own "no assembly scanning
  occurs in player/runtime code" acceptance criterion, if the recommended mechanism resembles
  `P6-010`'s Editor/MCP-only discovery pattern.
- Silently widening this decision to cover the native (Burst) execution backend as well as the
  reference backend -- scope this to the reference executor only unless the native backend's own
  constraints are separately investigated and disclosed, not assumed identical.

## Deliverables

- A decision on the registration/discovery shape (interface + attribute-based discovery mirroring
  `P6-010`'s custom-tool pattern? a builder API a project's own Editor-only bootstrap code calls
  directly? something else) and exactly what a project must supply (handler implementation,
  manifest, observer-condition binding, or some subset).
- A disposable spike proving a project-authored leaf handler (not one of the existing `aibt.test.*`
  fixtures) can be registered and driven through a real `ReferenceExecutionMachine` tick.
- A proposed ADR recording the decision, its rationale, and which of `P3-009`/`P6-007`/`P6-008`
  would need updating to consume it (expect all three).

## Acceptance criteria

- The spike demonstrates a genuinely new, project-authored leaf type (not a renamed copy of an
  existing fixture) being registered and ticked through the real, unmodified
  `ReferenceExecutionMachine`.
- A regression check confirms nothing in this investigation weakens `P3-009`'s own accepted parity
  guarantee (re-run `ReferencePreviewParityTests` or equivalent unmodified).
- The ADR states plainly what remains out of scope (native backend, at minimum, unless separately
  justified) rather than implying full parity across both backends.

## Required verification

```text
Verify-Static.ps1
disposable spike: real ReferenceExecutionMachine, a genuinely new leaf type, live Unity MCP
  execute_code
regression: P3-009's own parity test suite, unmodified, still passing
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) -- discovered as cross-phase debt
  during a Phase 6 session, mirroring `P6-013`/`P6-014`/`P6-015`'s own pattern.
- If accepted, expect follow-up implementation cards against `P3-009`, `P6-007`, and `P6-008`
  each, since all three currently hardcode the same fixed fixture set independently.
