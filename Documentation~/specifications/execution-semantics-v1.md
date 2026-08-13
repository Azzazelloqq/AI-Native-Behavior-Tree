# Execution semantics v1

Read `conventions.md` first. This specification defines behavior independently of executor backend.

## Status model

Public node statuses are exactly:

- `Success`: activation completed successfully.
- `Failure`: activation completed unsuccessfully.
- `Running`: activation remains active.

`Inactive` and budget suspension are internal executor states and MUST NOT be returned by user nodes. Exact reference-machine state and step accounting follow `reference-executor-machine-v1.md`.

## Lifecycle

Every activation follows one of two paths:

```text
Enter exactly once -> Tick one or more times -> Exit(Success|Failure) exactly once
Enter exactly once -> Tick zero or more times -> Abort exactly once -> Exit(Aborted) exactly once
```

Rules:

1. `Enter` and the first `Tick` MAY occur in the same execution pass.
2. A terminal result from `Tick` MUST be followed by `Exit` before the parent observes completion.
3. Abort traversal is deepest active descendant first, then its ancestors.
4. `Abort` requests cancellation and MAY emit cancellation commands. `Exit(Aborted)` performs final cleanup.
5. `Activation` node memory is zero-initialized immediately before `Enter` and cleared immediately after the matching `Exit`, including `Exit(Aborted)`. `Instance` node memory is zero-initialized only when the tree instance is created, restarted, or reset and persists through terminal Exit and abort. Every node manifest declares one lifetime explicitly; Phase 1 uses `Instance` only for `Cooldown`.
6. A completed node cannot be ticked again without a new activation.
7. Exceptions MUST NOT cross a Burst execution boundary. Managed exceptions become structured diagnostics and fail the affected update according to the host error policy.

## Per-agent ordering

A single tree instance is evaluated sequentially in semantic order. `Parallel` expresses multi-child progress semantics, not simultaneous execution of children. Parallelism is between independent instances or batches.

Each instance permits at most one execution pass at a time. Scheduling the same instance concurrently is an error.

## Memory sequence

`MemorySequence` starts at child zero. Within one pass it continues across children that return `Success`.

- `Success`: all children succeeded.
- `Failure`: current child failed.
- `Running`: current child is running.

The index of a running child is retained. Earlier successful children are not reevaluated until the sequence is activated again. An empty sequence succeeds.

## Reactive sequence

`ReactiveSequence` reevaluates from child zero on every eligible update. If it has a running child from the previous update, that subtree is aborted and exited before reevaluation begins. It may then be re-entered if selected again.

- It advances through successful children in semantic order.
- On `Failure`, the sequence fails.
- On `Running`, that child becomes the active child and the sequence runs.
- No candidate child is entered while the previous running subtree is still active.

An empty reactive sequence succeeds.

## Memory selector

`MemorySelector` starts at child zero and continues across children that return `Failure`.

- `Success`: current child succeeded.
- `Failure`: all children failed.
- `Running`: current child is running.

The index of a running child is retained. Earlier failed children are not reevaluated until the selector is activated again. An empty selector fails.

## Reactive selector

`ReactiveSelector` reevaluates from child zero on every eligible update. If it has a running child from the previous update, that subtree is aborted and exited before reevaluation begins. It may then be re-entered if selected again.

- It advances through failed children in semantic order.
- On `Success`, the selector succeeds.
- On `Running`, that child becomes the active child and the selector runs.
- No candidate child is entered while the previous running subtree is still active.

An empty reactive selector fails.

## Parallel

Every currently non-terminal child is visited in semantic order once per eligible update. Terminal children are remembered and are not ticked again during the same activation. Completion is evaluated after that full child visit, not immediately after an individual child result.

Supported completion policies are:

- `RequireAllSuccess`: fail when any child fails; succeed when every child succeeds.
- `RequireAnySuccess`: succeed when any child succeeds; fail when every child fails.
- `Threshold`: explicit positive success and failure thresholds.

Thresholds MUST NOT exceed child count and MUST satisfy `successThreshold + failureThreshold <= childCount + 1`, ensuring a terminal outcome after all children complete. For `Threshold`, if both thresholds are satisfied after the full visit, the node MUST declare `SuccessFirst` or `FailureFirst`; omission is a validation error. When the parallel node completes or is aborted, every running child is aborted in reverse semantic order. An empty parallel node is invalid.

## Decorators

- `Inverter` maps `Success` to `Failure`, `Failure` to `Success`, and preserves `Running`.
- `Succeeder` returns `Running` while its child runs and `Success` after any terminal child result.
- `Failer` returns `Running` while its child runs and `Failure` after any terminal child result.
- Phase 1 `Repeater` declares a positive finite `count` and `stopOnFailure`. Count zero is invalid. Each terminal child result ends one iteration and fully exits the child before the next Enter. With `stopOnFailure`, the first child failure returns `Failure`; otherwise child failures count as completed iterations. Completing `count` iterations returns `Success`. At most one new iteration begins per composite transition, so budgeting can suspend between iterations.
- `Timeout` declares a positive duration and terminal result (`Success` or `Failure`). It captures `deadline = enterTime + duration`. On each eligible update it checks `now >= deadline` before ticking or entering the child. At deadline it aborts a running child and returns the configured result; a child that completed on an earlier update keeps its result.
- Phase 1 `Cooldown` uses instance memory, declares positive duration, blocked result (`Success` or `Failure`), and start policy `OnEnter` or `OnSuccessfulExit`. If `now < nextAllowed`, it does not enter its child and returns the blocked result. `OnEnter` records the next deadline immediately before child Enter. `OnSuccessfulExit` records it only after the child exits `Success`; failure and abort do not start cooldown. Tree-blackboard-backed cooldown is deferred beyond Phase 1.

Decorators accept exactly one child unless their node contract states a stricter rule.

## Built-in configuration names

Phase 1 canonical parameters are fixed:

- `Parallel`: `policy` (`require-all-success`, `require-any-success`, or `threshold`); threshold additionally requires positive `successThreshold`, positive `failureThreshold`, and `tieBreak` (`success-first` or `failure-first`). Non-threshold policies forbid threshold fields.
- `Repeater`: positive `count` and Boolean `stopOnFailure`.
- `Timeout`: positive `durationMicroseconds` and `terminalResult` (`success` or `failure`).
- `Cooldown`: positive `durationMicroseconds`, `blockedResult` (`success` or `failure`), and `startPolicy` (`on-enter` or `on-successful-exit`).
- Sequence, selector, inverter, succeeder, and failer have no parameters in Phase 1.

Unknown parameters are validation errors. These names and values are persisted contracts, not implementation choices.

## Abort observers

Observer modes are `None`, `Self`, `LowerPriority`, and `Both`.

An observer belongs to a condition child within a reactive composite and declares watched Tree-scope blackboard keys. Phase 1 does not implement external event observers. When a watched slot version changes during Execute, the observer is queued once. The queue is drained at the observer reevaluation point after the current atomic node step and before selecting the next ordinary frame transition. A write never recursively invokes a callback.

Queued observers are ordered by tree instance ID and runtime observer node index. Re-evaluation ticks the observer condition once using the current Tree blackboard. A changed condition result triggers its mode: `Self` aborts its active descendant when the condition no longer permits it; `LowerPriority` is valid only for a condition under a reactive selector and, when the condition becomes successful, aborts the active sibling with greater child index; `Both` applies both transitions in that order. `None` records no observer and never queues. Repeating the same result performs no abort.

- `Self` may abort the observer's active descendant branch.
- `LowerPriority` may abort an active lower-priority sibling branch.
- `Both` permits both behaviors.

Abort reason and source node are recorded in trace output.

Priority is semantic child order under a selector: lower index means higher priority. `LowerPriority` outside a selector context is a validation error.

## Budget suspension

Budgets may suspend only between node steps. Suspension preserves activation and cursor state and does not call `Abort` or `Exit`. Budget suspension is internal and MUST NOT be stored as a node result. Exact steps and resume behavior follow `reference-executor-machine-v1.md`.

Step budgets are deterministic for identical inputs. Wall-clock budgets can change when behavior observes frame-varying input and therefore MUST be explicitly enabled and reported by diagnostics.

## Root completion

The tree instance exposes the root's terminal result only after all required lifecycle callbacks and command writes for that result complete. Restart behavior is host-configured and occurs on a later update; a terminal root is never implicitly re-entered in the same update.
