# ADR P7-033: Global production scheduler and scheduling profiles

- Status: Accepted 2026-09-05
- Date: 2026-09-05
- Decision ID: AIBT-037

## Context

`ProductionTreeHost` owns and drives one native tree instance. Its nullable `StepBudget` is local to
that host: null runs an update without a step limit, while a value limits one frame segment. This is
correct for the host's original scope but cannot enforce one AI budget across a population. If many
hosts each receive a local budget, their combined cost is unconstrained.

The native scheduling layer already contains work estimation, deterministic `Auto` selection,
same-frame batch controllers and a pipelined controller. These pieces are internal and have no
population-level production integration. The current `SchedulingPolicyDriver` is a benchmark/test
driver: its leaf outcomes come from a caller-supplied status array. It does not execute real project
node callbacks or generated catalog dispatch.

Generated Burst catalogs can execute immediately or schedule a catalog Job over a Runtime-owned
`BurstExecutionBatch`. Authoring can prebind catalog layouts, and Runtime owns workspace, transaction,
snapshot and command primitives. No production component currently constructs real per-agent
generated-dispatch requests from lifecycle-machine dispatch steps and carries their results back to
those machines. A population coordinator cannot honestly claim real custom-node Jobs execution by
wrapping `SchedulingPolicyDriver`.

The owner wants zero per-tree policy guesswork, one automatically managed system budget, custom
profiles for unusual games, a gameplay showcase and real Player evidence. The design must also keep
ADR-P4-007's rejection of runtime policy exploration and ADR-P7-010's per-tree lifecycle ownership.

## Verified constraints

1. AIBT cannot infer how much of a game's frame belongs to AI. Rendering, physics and later PlayerLoop
   work are outside its authority. Any zero-input millisecond or frame-percentage cap would be an
   arbitrary product assumption.
2. Time budgets are soft admission limits. A node callback, atomic lifecycle step or scheduled Job
   cannot be safely interrupted when the stopwatch reaches a deadline.
3. A global admission budget is distinct from `NativeAutoConfigurationV1.UpdateBudgetSteps`.
   Supplying the global budget through that field would cause the existing deterministic rule to
   select `Budgeted` before considering pipelined execution.
4. Automatic extra-frame latency remains opt-in. `PipelinedJobs` cannot be selected for a profile
   that did not permit it.
5. Current Player evidence makes `Immediate` the preferred same-frame policy for the measured
   workloads. There is no Player measurement of real `PipelinedJobs` gameplay yet.
6. Hosts must retain their own tree state, trace and teardown lifecycle. A coordinator may drive a
   registered host but must not duplicate or silently transfer its native ownership.

## Proposed decision

### 1. One optional population coordinator

Add one `ProductionTreeScheduler` component/service per independently scheduled AI world. A
`ProductionTreeHost` may run standalone as it does today or register with exactly one coordinator.
While registered, the host does not call its own drive loop from `Update`; the coordinator invokes an
internal host-driving surface. Registration never creates a second tree instance.

The host continues to own its machine, trace channel, terminal result, failure and disposal. The
coordinator owns registration records, due/deferred queues, group execution controllers, budget
accounting and population-level metrics.

Multiple independent worlds are allowed only through explicit separate coordinators. There is no
hidden global singleton or PlayerLoop injection.

### 2. Global budget modes

The public scheduler has three budget-source modes:

- **Unbounded**: backward-compatible default. All eligible work may run. No hidden time claim.
- **Fixed time**: one caller-supplied positive microsecond/millisecond allowance per scheduler frame.
- **Provider**: a caller-owned main-thread provider returns the allowance for the current frame,
  enabling integration with a game's own frame-time/quality manager.

`Auto` means automatic policy selection and automatic distribution *inside the available budget*;
it does not mean guessing an unknowable game-wide time allowance. P7-035 may support a documented
showcase preset, but one scene or hardware class cannot justify making that preset the universal
package default.

At the start of a scheduler frame, the allowance is frozen. The scheduler estimates the next atomic
segment before admitting it, measures actual elapsed time, stops admitting ordinary work once the
allowance is exhausted, and reports any overrun. It never aborts a callback or Job to enforce time.

### 3. Profiles express intent, not executor mechanics

Ship one public runtime profile value plus a Unity `ScriptableObject` asset wrapper that freezes to
the same runtime value at registration. Built-in presets and custom assets use this exact contract.
The profile contains:

- stable profile ID;
- ordered semantic priority;
- update cadence in frames;
- optional maximum response latency in frames;
- optional maximum share of the current global allowance;
- permission for pipelined latency;
- optional fixed-policy override.

Default semantics preserve current behavior: normal priority, cadence every frame, no hard maximum
latency, no group share cap, pipelining disabled and policy `Auto`. Cadence `1` is justified by the
existing `ProductionTreeHost.Update` contract; it is not a performance-derived tuning constant.

Priority is an ordering value, not a multiplier that silently converts to budget share. Custom
profiles may choose a custom rank. Optional budget share is explicit user intent and is never filled
with a hidden default.

Profiles are immutable while a scheduling round is using them. Asset edits or runtime replacement
take effect at the next eligible update boundary and produce an observable profile revision.

### 4. Deterministic service order and urgency

For each eligible host, the coordinator records its eligible-since frame and optional hard deadline.
Selection order is deterministic:

1. earliest explicit deadline;
2. oldest eligible-since frame (starvation protection without a magic aging coefficient);
3. pending one-shot urgent request;
4. higher profile priority;
5. stable tree-instance ID.

`RequestUrgentUpdate` is a one-shot signal consumed when that host's next logical update begins. It
does not mutate the shared profile and cannot grant pipelined permission or override a hard group cap.
This ordering gives delayed work precedence over newly eligible work without a hidden weight curve.

### 5. Separate global admission from local step suspension

The coordinator's time allowance controls whether another host/group segment is admitted. A
profile-specific fixed step limit, when explicitly configured in an advanced override, controls
where one native lifecycle update may suspend and therefore selects `Budgeted`.

The global allowance is never copied into `UpdateBudgetSteps`. This preserves access to Immediate and
Jobs policies and keeps the explanation truthful: `BudgetConfigured` means a real per-update step
budget, not merely that the AI system has a frame-time allowance.

### 6. Policy selection and grouping

The coordinator first filters policies by backend capability, profile latency permission and any
fixed override, then calls the deterministic `NativeAutoSelectionV1.TrySelect` rule. Forced policies
that contradict the backend or latency contract fail with a structured diagnostic.

Agents are grouped only when compiled-program identity, generated catalog identity, execution phase,
snapshot/blackboard access contract, command-reduction domain and selected policy are compatible.
Group membership never changes semantic child order or command publication order.

Same-frame `Auto` retains P6-019's measured preference for `Immediate`. `BatchedJobsSameFrame`
remains an explicit override/only-capable-policy path until later evidence supports another rule.
`PipelinedJobs` is reachable only for profiles that explicitly permit its latency; P7-035 measures
its real gameplay path before any performance recommendation changes.

### 7. Generated dispatch is a prerequisite, not a benchmark shortcut

Before the population coordinator can expose Jobs policies, a production bridge must:

- materialize catalog/layout authority usable in Player without reflection;
- translate real lifecycle dispatch requests into generated-dispatch workspace requests;
- bind per-agent configuration, memory, blackboard/snapshot, random, completion and command storage;
- call the generated catalog's immediate/scheduled facade without knowing project node types;
- feed callback status/failure back into the matching lifecycle machine;
- reduce/publish commands deterministically and retain ownership until every Job completes.

This bridge is a separate coherent prerequisite scope. A disposable proof must execute the same real
custom node and lifecycle update through immediate and scheduled generated dispatch before the public
coordinator implementation claims Jobs support. `SchedulingPolicyDriver` and precomputed status
arrays are forbidden substitutes.

### 8. Explainability

Expose a read-only frame snapshot containing profile/revision, eligible/deadline state, selected
policy and reason, estimate/confidence, batch shape, allocated/consumed time, executed steps,
completed/deferred agents, observed latency, budget overrun and structured failure. Metrics are
bounded and allocation-free after warmup; they do not expose native ownership handles.

## Alternatives rejected

- **Independent per-host budgets:** cannot cap total AI cost and scale poorly in authoring UX.
- **A package-chosen default number of milliseconds or frame percentage:** has no game-wide evidence
  source and was explicitly rejected by the owner when illustrative values were questioned.
- **Inferring priority from tree structure/type:** confuses implementation cost with gameplay value.
- **Runtime policy exploration:** already rejected by AIBT-013 because it requires deliberately
  suboptimal runs and adds instability.
- **One manager owning every tree's native state:** conflicts with the accepted host lifecycle and
  makes scene/object teardown ownership ambiguous.
- **Calling the benchmark driver production scheduling:** does not execute real node callbacks.
- **Hard stopwatch interruption:** would split atomic callbacks/jobs and violate lifecycle semantics.

## Consequences

- Most users choose a profile and, when needed, one global time allowance. They do not choose a
  scheduler policy for every tree.
- Truly zero-configuration execution remains unbounded, matching existing behavior honestly.
- Games with their own performance manager can provide dynamic headroom without AIBT learning policy
  costs or guessing the rest of the frame.
- P7-033 should be implemented only after the generated-dispatch prerequisite is separately scoped
  and proven. P7-034/P7-035 must use that real public path.
- Public types, exact validation diagnostics and serialization details are finalized in the accepted
  implementation proposal; the behavior above is the decision under review.

## Acceptance record

The owner accepted AIBT-037 as proposed on 2026-09-05 and approved splitting the production
generated-dispatch/lifecycle bridge into prerequisite card P7-037. Exact public type names remain an
implementation detail constrained by this decision and the package's public-API review gate.
