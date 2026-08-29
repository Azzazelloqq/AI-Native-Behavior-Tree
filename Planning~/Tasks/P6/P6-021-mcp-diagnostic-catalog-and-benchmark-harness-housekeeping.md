# P6-021 — MCP diagnostic-catalog accessibility and benchmark-harness housekeeping

Status: `Draft`

## Objective

Two small, unrelated-but-mechanical follow-ups bundled into one card because neither is large
enough to warrant its own spike/decision cycle:

1. Widen 4 diagnostic catalog holders' `Catalog` field from `private` to `internal` so `P6-007`'s
   `explain_diagnostic` tool can reach them, matching the same widening `P6-007`'s own 2026-08-28
   addendum already did for 3 other catalogs.
2. Live-verify (real Player build, not inspection) the 5 isolated Phase 4 benchmark harness
   scripts `P6-008` mechanically edited this session, closing that card's own disclosed "not run"
   gap.

Unlike `P6-016`-`P6-020`, this card is **not** a spike/decision -- both items are mechanical,
their correct outcome is already known, and neither touches a public/cross-assembly contract in a
way that needs owner sign-off beyond the accessibility widening itself (already precedented).

## Depends on

- `P6-007` (done -- owns `explain_diagnostic`, item 1's consumer).
- `P6-008` (done, this session -- item 2's own disclosed "not run" gap).

## Required reading

- `MCP/Verification/McpVerificationToolDispatcher.cs`'s `ExplainDiagnostic` method and its own
  2026-08-28 addendum comment (`Planning~/Evidence/P6-007/README.md`) -- the exact widening
  pattern this card's item 1 repeats for the remaining 4 catalogs.
- The 4 catalog holders and their exact `Catalog` field declarations, confirmed `private` in this
  session: `Authoring/Compilation/ReferenceCompilerDiagnostics.cs:32`,
  `Runtime/Execution/Reference/Core/ReferenceExecutionDiagnostics.cs:17`,
  `Runtime/Commands/CommandAsyncDiagnostics.cs:19`,
  `Runtime/Blackboard/Storage/BlackboardStorageContracts.cs:223` (this one holds
  `BlackboardStorageDiagnostics`'s catalog despite the file's own name).
- `Benchmarks~/Phase4/Scheduling/Run-SchedulingBenchmark.ps1` and its 4 siblings under
  `Benchmarks~/Phase4/{AutoComparison,Platform/{Android,Web,Windows}}/` -- the scripts `P6-008`
  edited this session (removed now-redundant special-case `SchedulingPolicyDriver.cs`/
  `SchedulingScenarios.cs` copy steps, since both files now live inside the scripts' own existing
  wholesale `Runtime/`/`Authoring/` copy) without running any of them end-to-end.
- `Planning~/Evidence/P6-008/README.md`'s own "Not run" disclosure -- the exact gap item 2 closes.

## Allowed changes

- `Authoring/Compilation/ReferenceCompilerDiagnostics.cs`,
  `Runtime/Execution/Reference/Core/ReferenceExecutionDiagnostics.cs`,
  `Runtime/Commands/CommandAsyncDiagnostics.cs`,
  `Runtime/Blackboard/Storage/BlackboardStorageContracts.cs` -- `private` -> `internal` on each
  `Catalog` field only, no other change.
- `MCP/Verification/McpVerificationToolDispatcher.cs`'s `ExplainDiagnostic` -- add the 4 newly-
  reachable catalogs to its existing lookup chain, matching the 2026-08-28 addendum's own pattern
  exactly.
- `Tests/Editor/Mcp/Verification/` -- new parametrized test cases (one per newly-reachable
  catalog), matching the addendum's own precedent.
- Running (not editing, unless a real bug is found) the 5 `Benchmarks~/Phase4/*.ps1` scripts --
  at minimum `Run-SchedulingBenchmark.ps1`, ideally all 5 if time and hardware allow (Web/Android
  need the platform modules `P4-008` already confirmed installed; Windows needs no extra setup).
- `Planning~/Evidence/P6-021/`.

## Forbidden changes

- Widening any catalog's accessibility beyond `internal`, or granting `InternalsVisibleTo` to any
  new assembly -- `AIBT.Mcp` already has the grants it needs (from `P6-007`'s own addendum); this
  card only removes the per-type `private` restriction that grant alone couldn't bypass.
- Editing any benchmark script's actual measurement logic, scenario catalog, or output format --
  this card verifies the existing scripts still run after `P6-008`'s path fix, it does not change
  what they measure or how.
- Treating a script's successful run as license to draw new performance conclusions -- this card
  produces a pass/fail on "does the harness still work," not new benchmark evidence; if a script
  happens to produce numbers, report them as a side effect, not as this card's own deliverable.

## Deliverables

- 4 catalogs (`ReferenceCompilerDiagnostics` AIBT2042-2046/3010-3019,
  `ReferenceExecutionDiagnostics` AIBT4001-4008, `CommandAsyncDiagnostics` AIBT4101-4110,
  `BlackboardStorageDiagnostics` AIBT4201-4209) reachable from `explain_diagnostic`, each proven
  by a real test asserting `catalogReachable: true` for at least one real code from each.
- At least `Run-SchedulingBenchmark.ps1` run end-to-end producing a real isolated-project Player
  build and a real output JSON, confirming `P6-008`'s path-only edit did not break it; the other 4
  scripts run too where practical, each disclosed as run-or-not-run honestly (mirroring
  `P4-008`'s own per-platform disclosure discipline) rather than assumed identical from one
  passing script.

## Acceptance criteria

- `explain_diagnostic` for a real code from each of the 4 newly-reachable catalogs returns
  `catalogReachable: true` with real subsystem/severity/field data, proven by a live call, not
  just a unit test in isolation.
- At least one isolated Phase 4 benchmark harness script is confirmed to still build and run a
  real Player producing real output after `P6-008`'s path fix -- closing that card's own "not
  run" disclosure with real evidence, not just re-asserting the fix "looks right by inspection."
- No diagnostic code, severity, or field contract changed for any existing reachable catalog --
  this card only affects catalog *reachability*, never catalog *content*.

## Required verification

```text
Unity EditMode: explain_diagnostic tests for the 4 newly-reachable catalogs
Unity EditMode: full AIBT.Tests.Editor.Mcp.* regression, unmodified elsewhere, still passing
At least one of Run-SchedulingBenchmark.ps1 / Run-AutoComparisonBenchmark.ps1 /
  Build-AndroidPlatformBenchmark.ps1 / Build-WebPlatformBenchmark.ps1 /
  Run-WindowsPlatformBenchmark.ps1 run end-to-end, real Player build produced
Verify-Static.ps1
```

## Handoff notes

- Not required for the Phase 6 integration gate (`P6-012`) -- discovered as cross-phase debt
  during a Phase 6 session, mirroring `P6-013`/`P6-014`/`P6-015`'s own pattern.
- If a real Player build reveals the path fix was actually wrong (script throws, wrong file
  copied), fix it as part of this card and disclose the original `P6-008` fix as having contained
  a real bug -- do not silently patch it without recording that.
