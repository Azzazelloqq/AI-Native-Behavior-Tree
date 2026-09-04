# P7-030 — Complete the production host execution contract

Status: `Draft`

## Objective

Make the P7-027 host preserve the existing runtime lifecycle, time, root-completion and
budget semantics across actual Unity frames. This card groups four integration findings;
it does not add another executor or a population scheduler.

## Revalidated findings

Reviewed 2026-09-04 against `66fa058`, with concurrent P7-028 work excluded from this scope.

1. **P2 — Updating a completed instance repeatedly reports a lifetime error.**
   `ProductionTreeHost.Update` always calls `TryBeginUpdate`, even after recording a terminal
   root result. `NativeLifecycleMachineV1.TryBeginUpdate` rejects `HasRootStatus != 0`.
   Reproduced again in Unity: first update returns `Success`; attempting the next update
   returns `NativeLifetimeStateInvalid`. The root result itself remains correct. The defect
   is failure to handle completion at the host boundary, not proof that automatic restart
   is required. The earlier review's P1 classification was too strong for this finding alone.
2. **P1 — The host supplies a permanently zero execution clock.**
   Its two-argument `TryBeginUpdate` call delegates to the overload with `timeMicroseconds = 0`;
   the value was also read back from the live machine. A positive Timeout around a Running
   child cannot expire through this host. Cooldown deadline comparisons likewise never see
   advancing time when revisited within a live instance. This is distinct from root completion;
   the latter currently also prevents ordinary updates after a terminal root.
3. **P1 — General Action lifecycle callbacks cannot be connected through the host.**
   `DispatchLeaf(uint)` is explicitly documented as Tick-only. `Update` acknowledges Enter,
   Exit and Abort with a fabricated successful dispatch completion without invoking node code.
   Therefore Actions relying on initialization, cancellation or cleanup in those callbacks
   cannot implement their existing lifecycle contract through this host. This is an integration
   gap, not a claim that the delegate violates its own documented Tick-only signature.
4. **P2 — Budgeted execution is claimed but not implemented in the host.**
   ADR-P7-010 and P7-027 name Immediate and Budgeted. The host has no budget configuration or
   budget driver and advances until Completed/Waiting. The lower-level budget implementation
   exists; it is not connected here. No measured performance regression is asserted.

Items 3 and 4 are established by current control-flow/API inspection, not a new Player test.

## Depends on

- P7-027.
- P7-010 (accepted ADR defining the host's scope).

## Required reading

- `Documentation~/architecture.md`.
- `Documentation~/specifications/execution-semantics-v1.md` (lifecycle, decorators, budget and root completion).
- `Documentation~/specifications/reference-executor-machine-v1.md`.
- `Documentation~/decisions/ADR-P7-010-production-play-mode-host.md`.
- `Planning~/DECISION_BOUNDARIES.md` and P7-027's card/evidence.
- `Runtime/Integration/ProductionTreeHost.cs`, `Runtime/Scheduling/SchedulingPolicyDriver.cs`,
  `Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs` and the existing native budget driver.

## Scope

- `Runtime/Integration/ProductionTreeHost.cs` and narrowly scoped host adapters in that directory.
- `Tests/Runtime/Integration/ProductionTreeHostTests.cs` and focused host integration fixtures.
- Host API documentation and P7-027 claim corrections; regenerate affected public API documentation
  only after an approved API decision. `Planning~/Evidence/P7-030/`.

## Implementation plan and decision boundary

1. Agree the smallest host API for callback phase/context, clock input, budget and terminal handling
   before implementation. Public/cross-assembly API changes require the existing decision process.
   Do not silently choose scaled versus unscaled time, automatic restart, or disable/teardown semantics.
2. Add behavior tests covering the findings, then wire the existing native mechanisms through the
   approved host boundary. Preserve Runtime's independence from Authoring, Editor and MCP.
3. Verify real multi-frame execution and update the host's documented capability claims.

## Forbidden changes

- No new VM, generic dispatch framework, population coordinator, BatchedJobsSameFrame/PipelinedJobs
  host support, blackboard redesign, or node-library expansion.
- Do not weaken native lifecycle preconditions to allow ticking a terminal instance.
- Do not silently change lifecycle/order/time semantics or break the existing public delegate.
- Do not add automatic restart as an assumed bug fix; compliant terminal stopping is sufficient
  unless an explicit restart policy is separately agreed.

## Deliverables and acceptance criteria

- Success and Failure roots are handled across subsequent frames without repeated lifetime errors,
  unintended callbacks, or resource leaks; restart, if agreed, occurs only on a later update.
- With controlled time, Timeout around a Running child aborts/exits at the deadline, not before;
  a revisited Cooldown blocks before its deadline and permits entry at the boundary.
- An Action fixture whose initialization is required by Tick and whose cancellation/cleanup are
  observable receives Enter/Tick/Exit and Abort/Exit(Aborted) in the normative order, exactly once
  where specified. Prove the actual callback invocation, not merely trace records of acknowledgement.
- A small step budget yields across host frames, preserves activation/cursor state, never fabricates
  Abort/Exit on yield, and reaches the same outcome and callback order as Immediate for fixed inputs.
- No unrelated public API, assembly dependency, or runtime performance claim changes.

## Required verification

From the package root, set `$UnityPath`, `$ProjectPath`, `$OutputPath` to the actual verification environment:

```powershell
& './Tools~/Verification/Verify-Static.ps1'
& './Tools~/Verification/Run-UnityTests.ps1' -UnityPath $UnityPath -ProjectPath $ProjectPath -OutputPath $OutputPath -Mode EditMode -Scope Full
git diff --check
```

Run the focused host behavior tests first. On an already-open project use Unity MCP tests instead
of launching a second Editor. Also capture real Play-mode multi-frame behavior and teardown;
reflection-driven Update checks alone are insufficient for that claim. Record exact test counts,
baseline failures and any unverified Player/platform claims. Measure warmed-up host allocations
if the implementation changes the per-frame path; do not infer zero GC from unit tests.

## Handoff notes

Creating this Draft card authorizes no implementation. P7-029 owns migration/hot reload,
P7-031 owns MCP node development, and P7-032 owns scheduler error recovery.
