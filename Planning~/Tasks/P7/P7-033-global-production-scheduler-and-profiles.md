# P7-033 — Global production scheduler and custom scheduling profiles

Status: `Draft`

## Objective

Replace per-tree scheduling guesswork with one population-level production coordinator. It owns a
single AI frame budget, groups compatible agents, selects an executor through the existing
explainable `Auto` rule, and distributes work according to user-authored scheduling intent.

The normal user assigns a built-in or custom profile to a tree/group. Concrete policies and
step-count limits remain advanced overrides for profiling and exceptional integrations.

## Agreed direction

- Budget is global to the AI system. Per-tree hosts must not each claim an independent time budget.
- Profiles express gameplay intent: priority, cadence, maximum latency, pipelining permission and
  optional caps/overrides.
- Built-in profiles and custom profiles use the same public contract.
- All optional values support an `Auto`/unset state. No illustrative number from planning becomes a
  default without evidence.
- Base priority comes from the game/profile. The scheduler may derive urgency from deadlines,
  event wakeups, continued actions and starvation age; it must not infer gameplay importance from
  tree structure.
- Manual `Immediate`/`Budgeted`/Jobs selection remains available as an explicit advanced override.

## Depends on

- `P4-004`/`P4-005` — work estimation and deterministic explainable selection.
- `P6-019` — measured same-frame preference over `BatchedJobsSameFrame`.
- `P7-030` — complete single-tree production execution contract.
- `P7-032` — recoverable native scheduler ownership.
- `P7-024` — Player evidence and its disclosed `PipelinedJobs` measurement gap.

## Required reading

- `Documentation~/execution-and-scheduling.md` and
  `Documentation~/decisions/ADR-P4-007-runtime-autotuning-resolution.md`.
- `Documentation~/decisions/ADR-P7-010-production-play-mode-host.md`.
- `Runtime/Integration/ProductionTreeHost.cs`.
- `Runtime/Scheduling/Native/Auto/`, work estimation, same-frame and pipelined controllers.
- `Planning~/Evidence/P7-024/README.md` and `Planning~/Evidence/P6-019/`.

## Mandatory planning gate

This card changes public API and scheduling semantics. Before implementation, propose and record an
accepted decision covering:

1. The runtime profile value type and optional Unity asset wrapper, including stable profile IDs.
2. The source of the zero-configuration automatic global budget. It may use measured frame
   headroom or another explicit signal, but it may not contain an arbitrary hidden millisecond,
   percentage or priority-weight constant.
3. Deterministic ordering between deadline, base priority, temporary urgency, starvation age and
   stable agent ID.
4. How one coordinator owns/groups agents while preserving `ProductionTreeHost` lifecycle,
   tracing, teardown and hot reload.
5. Web capability restrictions and the behavior of unsupported forced policies.

Do not start production implementation until this decision is accepted by the owner.

## Proposed profile surface

The exact public names are decided at the planning gate. The capability set must cover:

- stable profile identity;
- semantic priority class, with optional advanced relative weight;
- optional update cadence and maximum response latency;
- optional maximum share of the global budget;
- pipelining permission;
- optional fixed-policy override;
- per-agent temporary urgency without cloning or mutating the shared profile.

Built-in `Interactive`, `Normal` and `Background` profiles are presets over this contract, not
special scheduler branches. Their numeric mapping must be justified by accepted evidence.

## Allowed changes

- Population-level runtime scheduling/coordinator types and their public integration surface.
- A small serializable Unity profile asset adapter if accepted by the planning decision.
- Integration changes required to register/unregister `ProductionTreeHost` instances without
  duplicating their lifecycle machine.
- Focused behavior, allocation and scheduling tests.
- Documentation, generated public API, sample-facing setup documentation and
  `Planning~/Evidence/P7-033/`.

## Forbidden changes

- No hidden arbitrary budget, weight or latency defaults.
- No runtime policy-cost autotuning rejected by ADR-P4-007 unless a new decision explicitly
  reopens it with evidence. Automatic budget control must not silently become policy learning.
- No gameplay-specific priority inference, reflection, Entities dependency, managed fallback in a
  Burst path, or one Job per agent/tree.
- Do not make `BatchedJobsSameFrame` the default; current Player evidence shows a substantial loss
  for the measured workload.
- Do not claim `PipelinedJobs` performance before P7-035 measures it in a real Player.

## Acceptance criteria

- A default profile requires no per-tree policy or step-budget choice from the user.
- One coordinator enforces one observable global budget across all registered groups.
- Custom profiles can be created as project assets and runtime values, validated without silent
  clamping, and assigned to multiple trees/agents.
- Deadline and starvation behavior is deterministic and prevents permanent starvation.
- Temporary urgency changes ordering without mutating the shared profile.
- Unsupported forced policies fail with a structured diagnostic; `Auto` respects backend and
  latency capabilities.
- Explainability exposes selected policy/reason, estimate/confidence, allocated/consumed budget,
  completed/deferred agents and observed latency.
- Registration, scene disable/unload, terminal completion, failure and disposal do not leak or
  retain stale agents.
- Hot paths meet the existing allocation/Burst rules.

## Required verification

```text
Verify-Static.ps1
focused scheduling/profile behavior tests
Run-UnityTests.ps1 -Mode EditMode -Scope Full
public API diff and generated documentation checks
allocation measurement after warmup
live Play-mode integration proof through Unity MCP
git diff --check
```

No new cross-platform performance claim is required here. P7-035 owns canonical Player measurement.
