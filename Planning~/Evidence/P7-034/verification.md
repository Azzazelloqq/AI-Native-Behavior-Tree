# P7-034 verification

## Focused automated coverage

- `AIBT.Tests.Integration.NativeRuntime.ProductionTreeWorldEffectTests`: 6/6 passed (0.888 s).
  These exercise the public P7-041 path Swarm Arena consumes: commit-gated typed effects, grouped
  attribution, immediate/pipelined input barriers and warmed drain allocation.
- `AIBT.Tests.Integration.NativeRuntime.GeneratedDispatchLifecycleProofTests`: 12/12 passed
  (0.952 s), proving the generated lifecycle route used by the sample.
- `AIBT.Editor.Tests`: 436/436 passed (20.43 s) after runtime API documentation regeneration.

## Live Unity proof

Unity MCP Play Mode drove the authored scene with a fixed seed. Observed state counts were: Patrol
12/12; Pursue/Attack after beacon entry; Flee 8 agents while remaining agents completed an admitted
prior wave; Attack 12/12; and Return 12/12 Patrol. Across 8.13 logical seconds after the cooldown
fix, 12 agents produced 84 further attacks, below the eleven-per-agent cooldown bound rather than one
per update. A selected `Swarm Agent 64` opened the existing AIBT Trace Timeline through the sample
menu. `p7-034-low-pursue.png` and `p7-034-high-pursue.png` are the representative low/high captures.

The MCP screenshot helper itself logs a recursive PlayerLoop error and can reenter scheduling, so
teardown was separately rechecked without capture: after 3.5 s of normal Play, the scene reported
Pursue/12 agents, no host or world failure, and zero Console warnings/errors on exit.

## Static and full regression

`Verify-Static.ps1 -RepositoryPath C:\UnityProjects\Modules\Assets\AIBT` passed (schema 7,
work-items 146), including package `git diff --check`. The host-project full EditMode run reported
1,806 total / 1,801 passed / 5 failed. Two stale generated-documentation failures were repaired and
the editor assembly then passed 436/436; the three remaining failures are the two embedded-package
`PackageInfo.FindForAssembly` context failures and the unrelated `LocalSaveSystem` autosave test,
documented in P7-041.

A final post-refresh retry completed all 1,806 tests. Unity MCP's final job payload omitted a summary,
but reported exactly three failures (not capped): the same two embedded-package
`GeneratedArtifactContractTests` `PackageInfo.FindForAssembly` failures and the unrelated
`LocalSaveSystem` autosave failure. No P7-034/P7-041 failure appeared.

## 2026-09-13 showcase telemetry revision

The first runnable scene exposed a sample defect: a direct 12-to-256 live cohort mutation could join
an in-flight pipelined group and fault 27 hosts with `Callback rejected: InvalidHandle`. The sample
now queues every post-start population request until every host reports the same P7-039-safe idle
boundary. Unity MCP verified 12-to-256 and 256-to-512 transitions with zero host failures; the final
64-agent scene reports 64 registered hosts and 64 scheduler frame entries with zero host failures.
At 64, 256 and 512 hosts the scene has only three Renderer components; agent geometry is submitted
through instanced state groups rather than one Renderer per host. The overlay reports only public
scheduler/world/agent telemetry and is explicitly labelled non-benchmark Editor evidence.

The overlay now also displays a 0.5-second unscaled Runtime FPS average for live orientation, both in
Editor and in Player. With the 64-agent Modules-local scene in Play Mode it reported 28 FPS, 64
registered hosts and no pending population change. This is deliberately not Player benchmark evidence.
