# P8-012 — Patrol/pursuit consumer research

Status: `Review` — compiler fix and live consumer acceptance verified on installed toolchain.

Owner asked for a simple three-agent scene and an honest usability/execution assessment:
patrol, detect another agent, pursue, give up when outdistanced, return to patrol; a third agent
wanders randomly; show an exclamation mark above the patroller on detection.

## Research boundary and plan

1. Build an isolated persistent Unity consumer at C:/UnityProjects/AIBT-PatrolResearch using the
   current local package. Keep reproducible source under Spikes~/PatrolPursuit. Preserve all prior
   changes. Reuse the owner's accepted P8-009/010/011 working implementation without promoting
   their separate baseline/manual acceptance states.
2. Use real native trees with explicit patrol/chase/return branches. Perception and path-point
   selection are scene adapters; movement decisions, target locking and return state belong to
   native nodes/tree memory. Do not hide the behavior in a MonoBehaviour state machine.
3. Use an empty bounded ground map, sight cone/range, a larger pursuit-loss distance and direct
   locomotion. The second agent follows a looping route that demonstrates acquisition and loss;
   the third chooses seeded random destinations. Both are valid detection candidates. No NavMesh,
   obstacles, combat or networking are part of this research.
4. Show distinct colors, labels, patrol route, sight cone, current mode and the overhead ! while
   chasing. Prepare through public CLI/catalog APIs and inspect actual host execution.
5. Observe acquisition, movement, loss, return completion and independent wandering; capture real
   scene evidence and a reproducible report. Record usability friction separately from execution
   failures. Leave a scene/project the owner can open and use.

Owned: Spikes~/PatrolPursuit, this card, Planning~/Evidence/P8-012 and the work-item entry, plus
the new isolated research project. On 2026-11-07 the owner explicitly approved a regression
test and production fix for the reproduced mixed-width default alignment defect. Additional
owned paths: Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs,
Tests/Editor/BlackboardScopes/GeneratedScopeCompilerTests.cs and a scoped CHANGELOG.md entry.
No API/schema changes, package version changes or publication. Existing completed P7 native
integration is the foundation.

Approved fix plan: first reproduce mixed Bool/Float32/Float3/Int64 defaults with nonzero values
and declaration-order invariance across Tree/Agent/Shared scopes in a focused regression.
Then align both native and semantic default blobs using existing layout alignment, run focused
and broader Editor checks, prepare the unchanged patrol tree and finish live scene acceptance.

Acceptance: actual AIBT hosts/trees drive all three agents; at least one verified patrol ->
chase -> return -> patrol cycle, UI ! consistent with chase, a moving random walker, no host
runtime failures; trace/root/position evidence and explicit limits on manual UX claims.

## Session handoff

Outcome: the isolated scene runs all three real native hosts. Four consumer leaf types,
three trees/layouts, explicit world adapters, visuals and an opt-in 45-second observer are
reproducible under Spikes~/PatrolPursuit. The approved eight-line compiler correction aligns
both default blobs. Three mixed-width regressions fail with AIBT3017 before it and pass after.
Full installed-toolchain EditMode: 1517/1517, no failed/skipped/inconclusive cases. Static
schema/JSON/work-item/link/whitespace checks pass. Native CLI preparation succeeds for all
three final trees. The accepted observer verifies acquisition within 5.2 m, loss beyond 9 m,
return to the suspended waypoint, patrol resumption, consistent ! and seven random arrivals;
all hosts keep progressing without a terminal root/native failure for 45 seconds.

Evidence: Planning~/Evidence/P8-012/README.md, XML red/full results, before/after/final CLI,
actual host/partial trace snapshots, observer/checker results and four scene-camera frames.
Earlier Activation-memory and insufficient-speed attempts are retained as rejected research
observations, not accepted behavior. Existing reactive abort semantics are unchanged; the
consumer uses Instance memory for its target lock. No MonoBehaviour behavior state machine.

Branch: existing feature/aibt-consumer-workflow; no commit/push. Installed Unity 6000.7.0b1,
.NET 10.0.300. Exact stable baseline, Player, performance and manual newcomer graph UI were
not tested or claimed. Known full-suite 112 Persistent allocation warning remains. No package
version, schema/API, MCP or scheduler behavior change. The scene consumer is retained, with
its owned batch Editor shut down after verification so the owner can open it normally.

Recommended next task: owner review of the scene and prioritization of the documented native
authoring friction. No additional library feature was implemented on the basis of this spike.
