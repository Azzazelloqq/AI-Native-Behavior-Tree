# P7-036 — Swarm Arena UX and node-library review

Status: `Draft`

## Objective

Review the finished Swarm Arena as a new package consumer and turn observed friction into bounded,
evidence-backed follow-up work. Determine which sample nodes reveal reusable stdlib gaps and audit
whether performance/showcase claims match the real Player evidence.

This is a review card. It records findings and creates grouped scopes; it does not quietly redesign
the scheduler or promote gameplay code.

## Depends on

- `P7-034` — complete importable gameplay showcase.
- `P7-035` — canonical Player measurements and report.

## Review paths

1. Import/open the sample from documented instructions in a clean consumer project.
2. Understand the gameplay and tree behavior without reading implementation code.
3. Open/edit/reload the supplied trees and layouts.
4. Create a custom scheduling profile, assign it to a group and observe its effect.
5. Inspect selected live agents and scheduler explanations in the debugger/profiler.
6. Make one small project-specific custom node through the documented extension path.
7. Build and run the deterministic Player benchmark exactly as documented.

## Node classification

Classify every Swarm Arena custom node as one of:

- gameplay-specific and correctly sample-owned;
- reusable primitive already covered by existing stdlib/API;
- reusable missing primitive with evidence from at least two independent tree usages;
- symptom of an authoring/blackboard/command API gap rather than a missing node.

Promotion requires a separate implementation card and the same reference/native contract discipline
as P7-028. Do not move `Attack`, `Patrol`, target acquisition or other domain behavior into stdlib
merely because the showcase uses it.

## Allowed changes

- `Planning~/Evidence/P7-036/`, screenshots and review notes.
- Corrections to Swarm Arena/readme/report wording when they only fix verified documentation drift.
- New grouped Draft cards for validated implementation gaps.
- Status/plan/tracker/changelog integration required to record the review outcome.

## Forbidden changes

- No production scheduler or node-library implementation in this review card.
- No speculative feature list, one-card-per-comment fragmentation or cosmetic findings presented as
  release blockers.
- Do not weaken trees, workloads or tests to simplify the onboarding path.
- Do not claim “easy,” “production-ready” or a performance tier without evidence from the completed
  review and P7-035 Player data.

## Acceptance criteria

- Every review path has observed evidence, outcome and severity.
- Time/steps and unclear decisions in the onboarding path are recorded precisely enough to reproduce.
- Profile UX is checked for zero-configuration use and advanced custom configuration.
- Scheduler explanations make selection, budget, deferral and latency understandable without source
  inspection.
- Node candidates include concrete reuse evidence and stay separate from gameplay-specific nodes.
- Performance wording is audited against raw Player data and platform limitations.
- Findings are grouped by coherent implementation scope with dependencies and acceptance criteria.
- The review ends with a prioritized recommendation: fix before release, post-release candidate, or
  no action.

## Required verification

```text
Verify-Static.ps1
clean consumer-project import/onboarding walkthrough
Unity MCP Play-mode and debugger/profile walkthrough
documented release Player benchmark reproduction
Markdown link and JSON validation
git diff --check
```
