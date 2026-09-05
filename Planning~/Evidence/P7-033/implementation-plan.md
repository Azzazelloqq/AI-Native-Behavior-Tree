# P7-033 proposed implementation plan

Status: blocked on owner acceptance of proposed ADR AIBT-037.

## Scope sequence

### 0. Accept the decision and split the prerequisite

1. Review automatic-budget source, default `Unbounded` behavior, profile fields, deterministic order
   and host/coordinator ownership in ADR AIBT-037.
2. If accepted, mark the ADR Accepted and create one prerequisite implementation card for real
   generated-dispatch/lifecycle integration. Update P7-033/P7-034/P7-035 dependencies accordingly.
3. Do not combine the prerequisite with scheduler policy, profile UX or benchmark tuning.

### 1. Prove real generated dispatch

In the prerequisite card, build the smallest disposable proof first:

1. Compile one real custom Burst node and tree through the normal authoring pipeline.
2. Materialize the catalog/layout plan for Player without reflection or hand-authored offsets.
3. Execute one complete logical update immediately through generated dispatch.
4. Execute the same update through the generated scheduled facade and a real JobHandle.
5. Verify equal observable status, memory, blackboard/commands and lifecycle callbacks.
6. Only then promote the bridge into Runtime production ownership and test multi-agent grouping,
   failure, cancellation and disposal.

### 2. Add profile contracts

1. Add behavior-first tests for default semantics, custom IDs/ranks, optional values, invalid values,
   immutable frame snapshots and profile revisions.
2. Add the runtime profile value and Unity asset adapter with one conversion/validation path.
3. Implement built-in presets through the same constructor/data path; do not special-case them in
   the scheduler.
4. Regenerate public API documentation and record the additive diff.

### 3. Add coordinator registration and standalone compatibility

1. Refactor `ProductionTreeHost` drive logic behind an internal single-owner entry point without
   changing standalone behavior.
2. Register/unregister a host with exactly one scheduler; suppress host `Update` drive while owned.
3. Preserve host-owned machine, trace, terminal/failure state, disable/enable and destruction.
4. Test registration during callbacks, duplicate registration, scheduler destruction and scene
   unload as explicit negative/recovery cases.

### 4. Implement due ordering and budget admission

1. Build allocation-free due/deferred storage keyed by stable tree-instance ID.
2. Implement deadline, eligible age, one-shot urgency, profile priority and stable-ID ordering.
3. Freeze the global allowance once per scheduler frame. Estimate before admitting an atomic segment,
   measure actual elapsed cost and publish an overrun instead of interrupting work.
4. Apply optional profile share caps without silently redistributing a capped group's allowance back
   into that same group.
5. Keep global allowance separate from explicit per-update step budgets.

### 5. Integrate deterministic policy selection

1. Build supported-policy masks from backend/catalog capability and profile latency permission.
2. Feed estimates into the existing deterministic `TrySelect`; never call the rejected adaptive path.
3. Drive Immediate/Budgeted and real generated SameFrame/Pipelined paths through one result contract.
4. Preserve deterministic command reduction/publication and pipeline-stage latency.
5. Test forced-policy contradictions, deadline deferral, non-preemptible overrun and disposal while a
   Job is outstanding.

### 6. Explainability and verification

1. Publish bounded per-frame scheduler snapshots and debugger/profiler presentation.
2. Verify zero managed allocation after warmup and no native leaks.
3. Run focused suites, full EditMode regression, API drift/static checks and a Unity MCP Play-mode
   proof with multiple profiles and one coordinator.
4. Record no cross-platform performance conclusion. P7-035 owns release Player measurements.

## Exit criteria

- A user can run default `Auto` without selecting a policy per tree.
- One explicit global budget is enforced as a soft admission limit across the entire registered
  population.
- Custom profiles, urgency, cadence, latency and caps behave deterministically.
- Jobs policies execute real generated custom-node dispatch rather than precomputed statuses.
- Every choice and deferral is inspectable, and all lifecycle/native ownership paths dispose cleanly.
