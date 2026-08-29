# P6-020 — Hot-reload debug-instrumentation trace injection decision

Status: `Draft`

## Objective

Decide how to give `Authoring/HotReload/HotReloadPreviewDriver.cs` a real `IReferenceTraceSink`
injection point -- it currently hardcodes `null` (`HotReloadPreviewDriver.cs:104`, the `traceSink`
argument to `HotReloadStateMigration.Migrate`) -- so a future benchmark card can measure
debug-instrumentation overhead during hot reload, which `P5-009` explicitly could not.

This card exists because `P5-009`'s own evidence names this precisely as "a genuine,
structurally-grounded gap: `HotReloadPreviewDriver` hardcodes `traceSink: null` with no injection
point, and both ways to fix that (an internals-visible test assembly, or a public API change) fell
outside this card's own allowed/forbidden-changes fence." `P5-009` measured compile-only,
full-restart, compatible-migration, and subtree-restart cost in both Editor batchmode and a real
Windows Player, but could not measure the added cost of running with trace instrumentation active
during a reload -- a real, disclosed, load-bearing gap in that card's own performance picture.

## Depends on

- `P5-008` (done -- owns `HotReloadPreviewDriver.cs`, the file this card would change).
- `P5-009` (done -- the card whose evidence found and disclosed this exact gap).

## Required reading

- `Authoring/HotReload/HotReloadPreviewDriver.cs` (specifically the `Migrate`/restart call sites
  around line 104) -- the exact hardcoded-`null` site this card resolves.
- `Runtime/HotReload/Migration/HotReloadStateMigration.cs` and
  `Runtime/HotReload/Restart/HotReloadFullRestart.cs` -- both already accept a real
  `IReferenceTraceSink traceSink` parameter; confirm neither needs to change, only the driver's own
  call sites.
- `Planning~/Evidence/P5-009/README.md` -- the original disclosure and the two candidate fixes it
  already named (internals-visible test assembly vs. public API change) without picking between
  them, since doing so was outside that card's own scope.
- `MCP/Verification/McpVerificationToolDispatcher.cs`'s `Simulate` and
  `Authoring/BehaviorCases/AuthoringBehaviorCaseExecutorFactory.cs` (`P6-008`) -- both existing
  examples of a caller supplying its own `IReferenceTraceSink`/`CollectingTraceSink`, useful
  precedent for how a hot-reload benchmark caller would plug in its own sink once this card's
  chosen mechanism exists.

## Allowed changes

- `Spikes~/HotReloadTraceInjection/` (new, disposable) -- proves the recommended injection
  mechanism against the real `HotReloadPreviewDriver`/`HotReloadStateMigration`, mirroring
  `P6-002`'s own spike-before-ADR methodology.
- `Planning~/Evidence/P6-020/`.
- One proposed ADR.

## Forbidden changes

- Any production change to `Authoring/HotReload/HotReloadPreviewDriver.cs` or
  `Runtime/HotReload/` -- this card decides on paper; a separate future card implements it and a
  further card (or the same one, if scoped that way) then re-runs `P5-009`'s own benchmark with
  instrumentation on to close the gap for real.
- Widening `HotReloadPreviewDriver`'s public facade beyond exactly what a trace-sink injection
  point requires -- do not bundle unrelated facade widening (e.g. anything resembling `P6-013`'s
  own deferred `ReferencePreviewDriver` questions) into this card.

## Deliverables

- A decision between the two candidates `P5-009` already named (an `InternalsVisibleTo` grant
  letting a benchmark-owning assembly pass its own sink, vs. a public constructor/method parameter
  on `HotReloadPreviewDriver`) or a third option if investigation surfaces one, with a real reason
  for the choice -- consistency with how `P3-009`/`P6-008` already solved the analogous problem is
  a relevant factor, not a foregone conclusion.
- A disposable spike proving a caller-supplied `IReferenceTraceSink` actually captures real trace
  records during a real hot-reload migration/restart pass through the chosen mechanism.
- A proposed ADR recording the decision and rationale.

## Acceptance criteria

- The spike demonstrates real trace records captured from an actual `HotReloadStateMigration`/
  `HotReloadFullRestart` pass driven through `HotReloadPreviewDriver`, not a hypothetical
  description.
- A regression check confirms nothing in this investigation weakens `P5-008`'s own accepted tests
  (re-run them unmodified).
- The ADR states plainly what a future `P5-009`-style benchmark card would need to do to actually
  measure instrumentation overhead once this decision ships.

## Required verification

```text
Verify-Static.ps1
disposable spike: real HotReloadPreviewDriver + a caller-supplied trace sink, live Unity MCP
  execute_code
regression: P5-008's own existing test suite, unmodified, still passing
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) -- discovered as cross-phase debt
  during a Phase 6 session, mirroring `P6-013`/`P6-014`/`P6-015`'s own pattern.
- If accepted, a future benchmark card re-runs `P5-009`'s own methodology with instrumentation
  active to finally close that card's own disclosed measurement gap.
