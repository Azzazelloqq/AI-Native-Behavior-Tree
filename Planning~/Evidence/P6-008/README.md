# P6-008 MCP verification tools (test, benchmark) evidence

## Result

Done, narrowed. `MCP/Testing/` implements the 2 tools this card's narrowed scope owns --
`run-tests`, `run-benchmark` -- each wrapping exactly one promoted-but-otherwise-unchanged
production entry point (`BehaviorCaseRunner`, `SchedulingPolicyDriver`/`SchedulingScenarios`). No
second runner/driver/catalog exists anywhere in this card's code. Wired through
`McpToolDispatcher.cs` (2 new permission-tagged cases) and relayed by 2 new thin server methods in
`MCP~/Server/TestingTools.cs`.

## Scope correction: trace/compare-trace spun off to P6-015

This card originally covered `trace`/`compare-trace`/`run-tests`/`run-benchmark`. Before writing
any code, research found the `trace`/`compare-trace` half's premise false: nothing anywhere in
production or tests wires a *real* running native tree's lifecycle steps into a
`NativeTraceChannelOwnerV1` -- the only two things that ever write trace records are a synthetic
hand-authored `Scenario` helper local to `Tests/Editor/Debugger/NativeExecutionDebuggerSessionTests.cs`
and an unrelated diagnostic probe job, neither derived from an actual compiled tree's execution.
This is exactly the gap `Planning~/Evidence/P3-010/README.md` already disclosed and deliberately
left open ("no production code wires a native trace channel to a live pass at all"). Building that
wiring now would be genuine new trace-producing logic, not "wrapping an accepted entry point," so
per `DECISION_BOUNDARIES.md` it was spun off into its own `Draft` spike/decision card, `P6-015`,
mirroring how `P3-010`/`P6-013`/`P6-014` each handled the same shape of finding -- decided on paper
with a disposable spike, not built silently mid-card. Owner confirmed this scope split via
`AskUserQuestion` before any implementation began. `P6-008`'s own task card was corrected in place
to reflect the narrowed scope (see its "Scope correction" section), and `work-items.json`/
`MASTER_PLAN.md` updated to match, all committed together (see git log entry preceding this one).

## Second real gap found during investigation: the entry points weren't reachable either

Even narrowed to `run-tests`/`run-benchmark`, the card's premise that these wrap "already-accepted
production entry points" was also not literally true:

- `BehaviorCaseRunner` (P1-017) and its whole supporting framework
  (`Tests/BehaviorCases/Framework/{Model,Runner,Serialization,Validation}/`, 7 files) lived in
  `AIBT.BehaviorCases.Tests`, an Editor-only Tests assembly. `AIBT.Mcp`'s own asmdef does not
  reference it, and adding that reference would violate `architecture.md`'s `## Dependency
  direction` diagram (`MCP -> Runtime/Authoring/Editor` only; "Benchmarks and tests may reference
  public layers as required" -- never the reverse).
- `SchedulingPolicyDriver` (P4-001) had the identical problem (`AIBT.Runtime.Tests`), and its
  scenario catalog, `SchedulingScenarios`, lived only under the uncompiled
  `Benchmarks~/Phase4/Scheduling/Unity/` template folder -- not part of the main project at all.

Resolved by **promotion** (my own architectural call, made after escalating the finding to the
owner via `AskUserQuestion` and being told to decide): move the genuinely reusable,
test-framework-free logic out of Tests-only/uncompiled locations into the production layers
`AIBT.Mcp` already sits on (`AIBT.Authoring`, `AIBT.Runtime`), leaving the Tests assemblies and the
isolated Player-benchmark harnesses as thin callers of the promoted code -- exactly the shape
`architecture.md`'s own diagram already expects. This is a mechanical move (namespace + `using`
updates via `git mv` + targeted edits), not a rewrite; no logic changed anywhere.

## Promotion (file-by-file)

| From | To | Assembly |
|---|---|---|
| `Tests/BehaviorCases/Framework/Model/BehaviorCaseModel.cs` | `Authoring/BehaviorCases/BehaviorCaseModel.cs` | `AIBT.Authoring` |
| `Tests/BehaviorCases/Framework/Runner/{BehaviorCaseExecutorContract,BehaviorCaseRunner,BehaviorCaseRunResult}.cs` | `Authoring/BehaviorCases/` | `AIBT.Authoring` |
| `Tests/BehaviorCases/Framework/Serialization/{BehaviorCaseJson,BehaviorCaseJsonDiagnostics,BehaviorCaseJsonResult}.cs` | `Authoring/BehaviorCases/` | `AIBT.Authoring` |
| `Tests/BehaviorCases/Framework/Validation/{BehaviorCaseRegisteredValueRegistry,BehaviorCaseSemanticValidator}.cs` | `Authoring/BehaviorCases/` | `AIBT.Authoring` |
| `Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs` | `Runtime/Scheduling/SchedulingPolicyDriver.cs` | `AIBT.Runtime` |
| `Benchmarks~/Phase4/Scheduling/Unity/SchedulingScenarios.cs` | `Authoring/Benchmarking/SchedulingScenarios.cs` | `AIBT.Authoring` |

Namespace `AIBT.Tests.BehaviorCases` -> `AIBT.Authoring.BehaviorCases`,
`AIBT.Tests.Runtime.Benchmarking` -> `AIBT.Runtime.Scheduling`,
`AIBT.Benchmarks.Phase4.Scheduling` -> `AIBT.Authoring.Benchmarking`. Every promoted type stayed
`internal` -- `AIBT.Authoring`/`AIBT.Runtime` already grant `InternalsVisibleTo("AIBT.Mcp")` (from
`P6-005`/`P6-007`), so no new public API surface was introduced.

**Deliberately not promoted**: `Tests/Integration/SemanticSlice/ReferenceBehaviorCaseAdapter.cs`
(`ReferenceBehaviorCaseExecutorFactory`). Investigated as a promotion candidate first (it was
briefly moved, then reverted after reading its full body): it hardcodes
`SemanticSliceNodeContracts.CreateLeafRegistry()`/`.CreateObserverRegistry()` -- test-only fixture
node handlers whose own doc comment says "Do not ship in production registries." Promoting it
would have left a broken Tests-assembly dependency sitting in production. Instead, a new
`Authoring/BehaviorCases/AuthoringBehaviorCaseExecutorFactory.cs` was written, structurally
mirroring the reverted file (same compile/validate/construct-machine/adapt-steps shape -- itself
just composing other already-public production APIs, `CanonicalTreeJson`/`TreeValidator`/
`ReferenceCompiler`/`ReferenceExecutionMachine`) but wired to `ReferencePreviewFixtureEnvironment`'s
registries instead -- the same already-accepted (`P3-009`) Phase 1 fixture/built-in node set
`simulate` (`P6-007`) already uses. This is not a second `BehaviorCaseRunner` -- the runner itself
is reused unchanged; only the executor-factory extension point P1-017's own design explicitly
anticipated ("Runner can later accept another executor implementation without changing case
files") gained a new, production-appropriate implementation.

Consumers updated (namespace/`using` only, zero logic changes): `Tests/BehaviorCases/Framework/Tests/
{BehaviorCaseJsonTests,BehaviorCaseRunnerTests}.cs`, `Tests/Integration/NativeRuntime/
{NativeBehaviorCaseAdapter,NativeExecutionEquivalenceTests}.cs`, `Tests/Integration/SemanticSlice/
{ReferenceBehaviorCaseAdapter,SemanticSliceBehaviorTests,SemanticSliceCompilationTests}.cs`,
`Tests/Runtime/Benchmarking/SchedulingPolicyDriverTests.cs`. `Benchmarks~/Phase4/Scheduling/Unity/
SchedulingBenchmarkRunner.cs` and the 3 `Benchmarks~/Phase4/{AutoComparison,Platform/{Android,Web,
Windows}}/Unity/...` probe files (which also consume `SchedulingScenarios`/`SchedulingPolicyDriver`)
got the same `using` update.

**Isolated Player-benchmark harness fix**: `Benchmarks~/Phase4/{Scheduling,AutoComparison,
Platform/{Android,Web,Windows}}/` each had a per-file special-case `Copy-Item` step that copied
`SchedulingPolicyDriver.cs`/`SchedulingScenarios.cs` from their old locations into the isolated
project as flat extra files. Since both files now live inside `Runtime/`/`Authoring/`, each
script's own pre-existing wholesale `Copy-Item ... $runtimeSource/$authoringSource ... -Recurse`
step already picks them up -- the special-case copies became redundant and, worse, a real
duplicate-type risk (the same namespace-qualified class compiled twice into the same isolated
project). All 5 scripts' special-case driver/scenarios copy steps (and their `Test-Path`
existence-check throws) were removed; `Authoring/AssemblyInfo.cs` gained one more
`InternalsVisibleTo("AIBT.Runtime.Tests")` grant so each isolated harness's own local asmdef
(already named `AIBT.Runtime.Tests` to inherit `AIBT.Runtime`'s own matching grant -- an existing
trick, not introduced here) can still see the now-`AIBT.Authoring`-resident `SchedulingScenarios`.
**Not run**: the actual isolated Player harnesses (a real, non-development Player build per script)
-- out of proportion for this card and not required by its acceptance criteria; the fix is
mechanical (pure path/reference changes, verified by inspection) and the load-bearing regression
check (`SchedulingPolicyDriverTests`, the in-project EditMode correctness suite for the promoted
driver) *did* run and pass. Disclosed per `AGENT_WORKFLOW.md`'s "state `not run` and why."

## Implementation

`MCP/Testing/`:
- `McpTestingDiagnostics.cs` -- `AIBT9025`-`9029` (case not found, malformed arguments, unknown
  scenario, scenario not implemented, unknown policy).
- `McpTestingJson.cs` -- `WriteRunResult` (`BehaviorCaseRunResult` -> pass/fail + per-failure
  kind/pointer/message, reusing `BehaviorCaseAssertionFailure`'s own fields) and
  `WriteBenchmarkResult` (raw measured numbers + environment metadata, no threshold/default field).
- `McpTestingToolDispatcher.cs` -- `RunTests` resolves a `.aibtcase.json` by path relative to the
  project's Assets folder, builds `AuthoringBehaviorCaseExecutorFactory` rooted at the case's own
  directory, calls `BehaviorCaseRunner.Run`. `RunBenchmark` looks up the named scenario in
  `SchedulingScenarios.Catalog`, refuses an unknown name (`AIBT9027`) or a real, documented
  placeholder (`AIBT9028`, never silently substituted) with a structured diagnostic, otherwise
  builds agents via `SchedulingPolicyDriver.TryCreateAgents` and runs one of the three fixed
  same-frame policies for a caller-specified agent count, measuring wall-clock time via
  `Stopwatch`. No `PipelinedJobs`/`Auto` (outside the approved 6-scenario/3-fixed-policy surface).

`MCP/McpToolDispatcher.cs`: 2 new cases -- `run_tests` -> `TestExecution`, `run_benchmark` ->
`BenchmarkExecution` (both categories already existed in `McpPermissionCategory`, no enum change).

`MCP~/Server/TestingTools.cs`: 2 thin relays mirroring `VerificationTools.cs`'s exact shape.

`MCP/AIBT.Mcp.asmdef`: gained a `Unity.Collections` reference (needed for
`Allocator`/`NativeBudgetStateV1`, the same kind of addition `P3-010` made to `AIBT.Editor.asmdef`
for an analogous reason).

## Real bug found live: `UnityEngine.Application` is main-thread-only

The first live `aibt_run_benchmark` call against the real running Editor failed with `AIBT9013`
("`get_isBatchMode` can only be called from the main thread"). `McpTestingJson.WriteBenchmarkResult`
originally read `UnityEngine.Application.unityVersion`/`.platform`/`.isBatchMode` for environment
metadata -- but `McpBridgeListener` dispatches each request on a background TCP-accept thread, not
Unity's main thread, and those `Application` properties enforce a main-thread-only check regardless
of the value being effectively constant. No other file anywhere in `MCP/` touches
`UnityEngine.Application` (confirmed by grep before writing the fix), so this was new territory
this card introduced, not a pre-existing pattern to copy. Fixed by using only thread-safe
`System.Environment.MachineName`/`.OSVersion`/`.Version` for environment metadata; re-verified live
immediately after the fix (see below).

## Unity EditMode tests (8, all real, run live against `6000.5.8f1`)

`Tests/Editor/Mcp/Testing/McpTestingToolDispatcherTests.cs`, calling the real
`McpToolDispatcher.Dispatch` entry point:
- `run-tests` on a passing case compared against a direct `BehaviorCaseRunner.Run` call's own
  result (`success`, `executedStepCount`); on a deliberately-broken case (wrong `progress`
  expectation), the same failure kind the direct call reports; on a missing case path, `AIBT9025`.
- `run-benchmark` against a real implemented scenario (`scheduling-baseline-empty-job`) compared
  against a direct `SchedulingPolicyDriver.TryRunImmediate` call's own `totalSteps`; a documented
  placeholder scenario (`event-driven-sleeping-wakeup`) rejected with `AIBT9028`; an unknown
  scenario name rejected with `AIBT9027`.
- The full 2-tool permission-negative matrix (`run_tests`/`run_benchmark` each rejected when
  granted only `Read`, `AIBT9012`).

## Regression

`AIBT.BehaviorCases.Tests` + `AIBT.Integration.Tests` + `AIBT.Runtime.Tests` + `AIBT.Editor.Tests`
(which now also holds `Tests/Editor/Mcp/Testing/`): **997/997 passed, 0 failed, 0 skipped**, run
twice (once right after the promotion alone, once again after the `UnityEngine.Application` fix) --
proves the promotion itself broke nothing in any of the four assemblies it touched.

## Live end-to-end verification (real MCP client, real permanent server, real Unity bridge)

Bridge started live in the actually-open Unity `6000.5.8f1` Editor via Unity MCP `execute_code`
(mirroring `P6-005`-`P6-007`'s own methodology). `dotnet build` on the real, permanent
`MCP~/Server/` -- 0 warnings, 0 errors. The official `@modelcontextprotocol/inspector` CLI,
configured via a `--config`/`--server` file with an explicit `env` map (the same documented
workaround `P6-005`/`P6-006` established for shell-exported env vars not reaching the CLI's spawned
subprocess):
- `tools/list` showed `aibt_run_tests`/`aibt_run_benchmark` with real schemas alongside every prior
  tool.
- `aibt_run_benchmark` against the real project, `scheduling-baseline-empty-job`/8 agents/immediate
  -- real `totalSteps: 32`, real `elapsedMicroseconds`, real environment metadata (this call is
  what first hit the `UnityEngine.Application` bug above; re-run clean after the fix).
- `aibt_run_benchmark` against the documented placeholder `event-driven-sleeping-wakeup` -- real
  `AIBT9028`, not silently substituted.
- `aibt_run_tests` against the real, checked-in project fixture
  `AIBT/Tests/Editor/Mcp/Testing/Fixtures/success-then-running.aibtcase.json` -- `success: true`.
- `aibt_run_tests` against the checked-in known-failing variant
  (`success-then-running-known-failing.aibtcase.json`) -- `success: false`, real
  `ProgressMismatch`/`RootStatusMismatch` failures.
- Permission-negative: both tools called with only `Read` granted -- both real `AIBT9012`.

No live-created fixture files needed cleanup (the two `.aibtcase.json`/`.aibt.json` fixtures used
for live verification are checked-in test assets under `Tests/Editor/Mcp/Testing/Fixtures/`, not
transient files written during the live session). The bridge's discovery file
(`Library/AibtMcp.json`) was deleted after verification so a future client correctly reports "not
running" rather than pointing at a stale port. **Disclosed, not fully cleaned up**: the background
TCP-listener thread itself (started via `execute_code`, not through `McpBridgeWindow`'s own
Start/Stop UI) has no reachable handle to call `Stop()` on from a later `execute_code` invocation;
it remains alive in this Editor session until the domain reloads or the Editor closes -- harmless
(bound to an ephemeral, now-undiscoverable port) but not tidied, disclosed rather than silently
left unmentioned.

## Verification

```text
Unity MCP run_tests (EditMode): AIBT.Tests.Editor.Mcp.Testing.McpTestingToolDispatcherTests --
  8/8 passed
Unity MCP run_tests (EditMode): AIBT.BehaviorCases.Tests + AIBT.Integration.Tests +
  AIBT.Runtime.Tests + AIBT.Editor.Tests -- 997/997 passed, no regressions (run twice)
dotnet build MCP~/Server -- 0 warnings, 0 errors
Live: real bridge + real permanent MCP~/Server/ + official Inspector CLI --
  tools/list (2 new + every prior tool, real schemas)
  run_benchmark on a real implemented scenario -> real totalSteps/elapsedMicroseconds/environment
  run_benchmark on a documented placeholder -> AIBT9028, never substituted
  run_tests on a real passing fixture -> success:true
  run_tests on a real known-failing fixture -> success:false, real failure kinds
  permission-negative: both tools with only Read granted -> AIBT9012
  discovery file cleaned up; background listener thread disclosed as not stoppable post-hoc
Tools~/Verification/Verify-Static.ps1 -- passed, 98 work items
git diff --check -- clean
Run-SchedulingBenchmark.ps1 (and the 4 sibling Phase4 harness scripts): path/reference changes
  verified by inspection only, NOT re-run end-to-end (disclosed as "not run")
```

## Scope and limitations

- `trace`/`compare-trace` are not part of the MCP surface yet -- deferred to `P6-015`'s decision
  and a future implementation card.
- `run-tests` drives cases only against `ReferencePreviewFixtureEnvironment`'s Phase 1 fixture/
  built-in node set (same limitation `simulate`/`P3-009`'s preview already have) -- a case using
  other node types (e.g. `patrol-react.aibtcase.json`'s `aibt.test.alert-condition`) will fail to
  compile through this tool even though it compiles for real authoring.
- `run-benchmark` supports only the 3 fixed same-frame policies (`Immediate`/`Budgeted`/
  `BatchedJobsSameFrame`) against the 6 `P4-001`-implemented scenarios -- no `PipelinedJobs`, no
  `Auto`, the 8 documented placeholders always refused.
- Single client, single Unity instance at a time, same as every prior P6 card's own disclosed
  scope, unchanged here.
