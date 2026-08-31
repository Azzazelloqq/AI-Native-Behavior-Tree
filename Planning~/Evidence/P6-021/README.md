# P6-021 MCP diagnostic-catalog accessibility and benchmark-harness housekeeping evidence

## Result

Done. Two small, unrelated, mechanical items.

## Item 1: 4 more diagnostic catalogs reachable from `explain_diagnostic`

`ReferenceCompilerDiagnostics`, `ReferenceExecutionDiagnostics`, `CommandAsyncDiagnostics`, and
`BlackboardStorageDiagnostics` each declared their own `Catalog` field `private` -- a per-type
restriction the existing `InternalsVisibleTo` grant from `AIBT.Authoring`/`AIBT.Runtime` to
`AIBT.Mcp` (from `P6-007`'s own 2026-08-28 addendum) could not bypass. Widened all four to
`internal`, matching the exact pattern that addendum already used for
`TreeJsonDiagnostics`/`NodeRegistryDiagnostics`/`LayoutJsonDiagnostics`, and added them to
`McpVerificationToolDispatcher.ExplainDiagnostic`'s existing lookup chain. No diagnostic code,
severity, or field contract changed for any catalog -- only reachability.

4 new parametrized test cases (`AIBT3010`/`RegistryAndCompiler`, `AIBT4001`/`Execution`,
`AIBT4101`/`Execution`, `AIBT4201`/`Execution`) each prove `catalogReachable: true` with real
subsystem/severity data through the real `McpToolDispatcher.Dispatch` entry point.

## Item 2: benchmark-harness live verification

`P6-008` mechanically edited 5 isolated Phase 4 benchmark harness scripts this session (removed
now-redundant special-case `SchedulingPolicyDriver.cs`/`SchedulingScenarios.cs` copy steps, since
both files now live inside the scripts' own existing wholesale `Runtime/`/`Authoring/` copy) without
running any of them end-to-end -- this card closes that "not run" disclosure with real evidence.

Ran 3 of the 5 scripts end-to-end, each with minimal `WarmupSamples`/`MeasuredSamples`/`AgentCounts`
(this card verifies the harness still works, not new benchmark evidence -- per its own
forbidden-changes clause):

- **`Run-SchedulingBenchmark.ps1`** -- completed successfully, real output JSON produced (Editor
  batchmode; `environment.editorBatchMode: true`).
- **`Run-AutoComparisonBenchmark.ps1`** -- completed successfully, real output JSON produced
  (Editor batchmode).
- **`Run-WindowsPlatformBenchmark.ps1`** -- completed successfully, **a real Windows x64 IL2CPP/
  Burst non-Development Standalone Player was built and run**
  (`environment.applicationPlatform: "WindowsPlayer"`, `isEditor: false`), producing real measured
  numbers across all 6 `P4-001` scenarios -- this is the one that actually satisfies "real Player
  build produced," since the two Editor-batchmode scripts above do not build a Player at all.

**Not run this session, disclosed honestly rather than assumed identical**:
`Build-AndroidPlatformBenchmark.ps1` and `Build-WebPlatformBenchmark.ps1` -- both need real device/
browser access `P4-008` already found and used (a physical Android device connected over `adb`, a
Browser pane WebGL run); re-acquiring that access was out of proportion for a housekeeping card
whose own deliverable only requires "at least one" real Player build, already satisfied by the
Windows script above.

No script's own measurement logic, scenario catalog, or output format was touched -- only their
existing `P6-008`-edited copy steps were exercised, unmodified by this card.

## Verification

```text
Unity EditMode full regression (host project) -- 1585/1585 executed, same 3 pre-existing unrelated
  failures every recent P6 card's evidence already documents, plus the 4 new explain_diagnostic
  test cases all passing
Run-SchedulingBenchmark.ps1 -- completed successfully, real output produced (Editor batchmode)
Run-AutoComparisonBenchmark.ps1 -- completed successfully, real output produced (Editor batchmode)
Run-WindowsPlatformBenchmark.ps1 -- completed successfully, real Windows x64 IL2CPP/Burst
  Standalone Player built and run, real measured output across all 6 P4-001 scenarios
Verify-Static.ps1 -- passed
git diff --check -- clean
```

## Scope and limitations

- `Build-AndroidPlatformBenchmark.ps1`/`Build-WebPlatformBenchmark.ps1` were not run this session
  (see above) -- their own harness content is unchanged from `P4-008`'s own accepted evidence, and
  the same mechanical copy-step fix `P6-008` applied to the other 3 scripts was applied identically
  to these 2, but their end-to-end re-verification remains open, real, disclosed follow-up work if
  ever needed.
