# P7-036 Swarm Arena UX and node-library review

Date: 2026-09-17

## Scope and verdict

This is an evidence review, not a scheduler, node-library, workload or policy change. The only
committed-scope edits are two verified onboarding/benchmark-documentation corrections in the package
and this evidence. No default profile, scheduler constant, sample tree, custom sample node or Player
benchmark input changed.

Verdict: the sample is understandable as a new consumer, its advanced scheduler/debugger path is
observable, and its three custom nodes do **not** justify a stdlib promotion. The documented
release-Player command is reproducible from the already completed P7-035 evidence. No implementation
follow-up card is justified by this one review.

## Review-path ledger

| Path | Observed evidence and outcome | Severity |
|---|---|---|
| 1. Clean consumer import/open | A fresh Unity 6000.5.8f1 project with only `file:C:/UnityProjects/Modules/Assets/AIBT` plus the JSON dependency resolved Burst 1.8.30, Collections 6.5.0 and Newtonsoft JSON 3.2.2. A batch refresh of the Swarm Arena payload compiled `AIBT.Samples.SwarmArena.Nodes`, `.Catalog`, runtime and editor assemblies and finished with `*** Tundra build success` / `Exiting batchmode successfully now!`. The only initial warning was the pre-existing `NativePipelinedPhaseControllerV1.cs(182,44)` CS0652. | Low: the native Package Manager Samples-tab click was unavailable to automation, so that click was not visually witnessed. It is documented exactly and the clean package/payload compile is independently proven. |
| 2. Understand gameplay/tree without implementation code | Parsed `swarm-arena.aibt.json` and its layout only. The 12-node reactive tree reads as: danger within 4 m -> flee; beacon within 12 m -> attack within 1.8 m, otherwise pursue; fallback -> patrol. Four Tree-scope `Float3` facts are authored: `agent`, `beacon`, `danger`, `patrol`. All twelve layout positions parse. | None. |
| 3. Open/edit/reload supplied tree and layout | Unity MCP opened `Assets/SwarmArena/Trees/swarm-arena.aibt.json` in the real `AIBT Graph` window. A disposable copy changed its root display name and `priority` layout x-coordinate from 40 to 64. Reload parsed the edited display name; `LayoutPersistenceController.Load` returned `success=True`, `usedDefault=False`, `priority=64,430`. The copy was deleted afterwards. | None. Graph editing is intentionally presentation/read-only here; editing the shared JSON/layout copy and reloading is the supported observable path. |
| 4. Custom scheduling profile | In live Play Mode, 4 idle-safe hosts were unregistered/re-registered with `p7-036-review-immediate` (priority 200, cadence 1, pipelining false, forced `Immediate`). After three seconds each record showed `Immediate` / `Executed`, cadence 1, batch size/count 0 and observed latency 0. The original scene assignments were restored by Play-mode exit. | None. Advanced configuration is explainable without source inspection. |
| 5. Live agent, scheduler explanation, debugger and profiler | The live scene had 64 registered/frame-entry agents, 32 `PipelinedJobs` and 32 `Immediate`. A selected `Swarm Agent 17` had an owned trace channel and opened `AIBT Trace` through `AIBT/Samples/Swarm Arena/Attach Selected Agent Trace`. One Editor profiler frame exposed `AIBT.Production.Scheduler.Frame` (26.434 ms), `PipelinedAdvance` (1.476 ms), `SwarmArenaScenario.LateUpdate` (0.066 ms), and the underlying native dispatch markers. | None for UX. These are Editor-profiler observability values, explicitly not Player benchmark values. |
| 6. Project-specific custom node | The documented external MCP route was exercised through the official `@modelcontextprotocol/inspector` CLI against an explicitly started Unity bridge: `generate_node` -> `preview_node_diff` -> test scaffold -> `analyze_and_compile_node` -> `test_node` -> `apply_node`. The disposable UInt32 condition `aibt.swarmaarena.review.has-patrol-slot` compiled with content hash `217acd332e5f78f1e7c8715b06d6367ec34b9d5c6664c9ddca80c2dd458aed89`; `test_node` returned `valid=true`, `dispatchProven=true`, `enteredSuccessfully=true`, `tickStatus=Success`. It was applied only under `_P7036Review/GeneratedNode`, then that folder, the staging folder and the bridge discovery file were deleted. | Low operational note: the first headless compile check returned `Requested compilation did not verify both staging assemblies`; a forced Unity refresh followed by a fresh start/check completed. This matches the known headless file-watcher characteristic; no product defect was silently fixed. |
| 7. Deterministic Player benchmark | The sample README now states the exact `Run-SwarmArenaPlayerBenchmark.ps1` command, normal matrix duration and diagnostic-only `-PlayerCellLimit`. P7-035 already ran that exact frozen 60-cell release Windows x64 IL2CPP/Burst matrix: five populations x six policies x rendering on/off, 20 s warmup + 60 s measured per cell. The tree SHA-256 is `bc3c0e0ea7a807376267c427018b959a743e19b2d66153a75f0bb50605944c33`, matching this authored tree. | None. It was not rerun during a review that makes no workload change. |

## Live scheduler observations

Before the custom-profile assignment, the 64-agent scene reported 32 `PipelinedJobs` and 32
`Immediate` registrations. Representative pipelined frame entries had batch `1/32`, observed latency
32 frames and expected node steps per agent about 29.27. The profile test used only hosts whose
`CanAcceptExternalTreeWrites` idle barrier was true, so no in-flight pipelined participant was
mutated. The four reassigned hosts (instance IDs 9, 17, 20 and 48) all reported the expected forced
policy/disposition and no batching/latency. This validates the zero-configuration mixed scene and a
small advanced override without modifying the saved scene.

The profiler capture recorded whole Editor-frame timing (`cpu_frame_time_ms=30.973`, main thread
`27.935`, render `0.889`, GPU `0.142`). It is retained solely to prove that named scheduler/native
markers are visible to a reviewer; it must not be compared with P7-035 Player aggregates.

## Node classification

| Sample node | Classification | Evidence and decision |
|---|---|---|
| `within-radius` | Gameplay-specific and correctly sample-owned | It is a spatial predicate used three times inside one authored tree. The built-in 13-node catalog has no spatial guard, but one tree is not the required two independent usages for a reusable-missing-primitive promotion. No stdlib card. |
| `steer` | Gameplay-specific and correctly sample-owned | It combines direction with Swarm Arena positions and visible agent-state effects. It is not evidence for a generic navigation or command leaf. |
| `attack-with-cooldown` | Reusable primitive already covered by existing stdlib/API | The domain attack effect remains sample-owned. Its rate-limit concern is already represented by `aibt.core.cooldown`; the showcase does not justify a generic attack primitive. |

There is no authoring/blackboard/command API-gap classification: the authored tree's four external
facts and visible effects are represented through the existing public integration path. No custom
node is promoted merely because the sample uses it.

## Documentation and performance-claim audit

`README.md` now tells a consumer to select **AI-Native Behavior Tree** in Package Manager, open
**Samples**, import **Swarm Arena**, then open `SwarmArena.unity`. The sample README now gives the
same import route and the exact canonical benchmark command. It also corrects the former vague
population wording to the actual five fixed P7-035 populations.

The sample does not claim an FPS tier, scalability tier or Player-equivalent Editor performance. Its
runtime FPS is orientation telemetry only. The only performance source is P7-035's raw release Player
report, including its disclosed machine/platform limitations; no Editor/Player multiplier or
cross-device conclusion is introduced by this review.

## Limitations and cleanup proof

- The clean-consumer technical import and sample payload compile are observed, but native UI access
  was unavailable for an actual recorded Package Manager Samples-tab click. This is an evidence
  limitation, not a replacement claim.
- The custom-node test scaffold deliberately contains an inconclusive placeholder pending the generic
  dispatch test harness; the real `test_node` result above, not that scaffold, is the node proof.
- The first headless node compile was invalid evidence and is retained as an operational observation;
  only the subsequent fresh compile/test result is accepted.
- The temporary `Assets/_P7036Review`, `Assets/AIBT-Generated` staging tree and
  `Library/AibtMcp.json` were all verified absent after the review. The Unity Console was then clean
  after forced refresh and recompilation.

## Prioritized recommendation

1. **Fix before release:** none. The package/sample docs, observable scheduler path, trace window and
   release-Player handoff are sufficient for this reviewed scope.
2. **Post-release candidate:** if headless MCP use becomes a supported consumer workflow, document
   the explicit AssetDatabase refresh/retry fallback for staging compilation. Do not create a
   scheduler or node-library implementation card from this one review.
3. **No action:** do not promote any Swarm Arena node to stdlib until a second independent authored
   tree demonstrates the same primitive need.
