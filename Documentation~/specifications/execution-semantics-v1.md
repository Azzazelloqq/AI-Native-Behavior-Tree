# Execution semantics v1

Read `conventions.md` first. This specification defines behavior independently of executor backend.

## Status model

Public node statuses are exactly:

- `Success`: activation completed successfully.
- `Failure`: activation completed unsuccessfully.
- `Running`: activation remains active.

`Inactive` and `BudgetYielded` are internal runtime states and MUST NOT be returned by user nodes.

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
5. Node memory is zero-initialized before `Enter` and cleared after `Exit` in v1. Persistent data belongs in a declared blackboard scope.
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

`ReactiveSequence` reevaluates from child zero on every eligible update.

- It advances through successful children in semantic order.
- On `Failure`, the sequence fails.
- On `Running`, that child becomes the active child and the sequence runs.
- If reevaluation selects a different child than the previously active child, the old active subtree is aborted before the new child is entered.
- If an earlier child fails, any previously active later subtree is aborted before the sequence exits.

An empty reactive sequence succeeds.

## Memory selector

`MemorySelector` starts at child zero and continues across children that return `Failure`.

- `Success`: current child succeeded.
- `Failure`: all children failed.
- `Running`: current child is running.

The index of a running child is retained. Earlier failed children are not reevaluated until the selector is activated again. An empty selector fails.

## Reactive selector

`ReactiveSelector` reevaluates from child zero on every eligible update.

- It advances through failed children in semantic order.
- On `Success`, the selector succeeds.
- On `Running`, that child becomes the active child and the selector runs.
- When a higher-priority child becomes `Running` or `Success`, a previously active lower-priority subtree is aborted before the new branch takes ownership.

An empty reactive selector fails.

## Parallel

Every currently non-terminal child is visited in semantic order once per eligible update. Terminal children are remembered and are not ticked again during the same activation. Completion is evaluated after that full child visit, not immediately after an individual child result.

Supported completion policies are:

- `RequireAllSuccess`: fail when any child fails; succeed when every child succeeds.
- `RequireAnySuccess`: succeed when any child succeeds; fail when every child fails.
- `Threshold`: explicit positive success and failure thresholds.

Thresholds MUST NOT exceed child count. For `Threshold`, if both thresholds are satisfied after the full visit, the node MUST declare `SuccessFirst` or `FailureFirst`; omission is a validation error. When the parallel node completes or is aborted, every running child is aborted in reverse semantic order. An empty parallel node is invalid.

## Decorators

- `Inverter` maps `Success` to `Failure`, `Failure` to `Success`, and preserves `Running`.
- `Succeeder` returns `Running` while its child runs and `Success` after any terminal child result.
- `Failer` returns `Running` while its child runs and `Failure` after any terminal child result.
- `Repeater` MUST declare a finite count or an explicit unbounded flag allowed by project policy. It resets the child between iterations.
- `Timeout` uses the tree's declared clock source, aborts a running child when the deadline is reached, and returns its configured terminal result.
- `Cooldown` uses a declared blackboard or instance memory clock and has explicit start-on-enter or start-on-exit policy.

Decorators accept exactly one child unless their node contract states a stricter rule.

## Abort observers

Observer modes are `None`, `Self`, `LowerPriority`, and `Both`.

Observers declare their blackboard keys and event dependencies. A dependency change queues reevaluation; it MUST NOT recursively execute a tree from inside a write. Reevaluations are processed in the next defined reevaluation phase in stable tree-instance and node order.

- `Self` may abort the observer's active descendant branch.
- `LowerPriority` may abort an active lower-priority sibling branch.
- `Both` permits both behaviors.

Abort reason and source node are recorded in trace output.

Priority is semantic child order under a selector: lower index means higher priority. `LowerPriority` outside a selector context is a validation error.

## Budget suspension

Budgets may suspend only between node steps. Suspension preserves activation and cursor state and does not call `Abort` or `Exit`. `BudgetYielded` is internal and MUST NOT be stored as a node result.

Step budgets are deterministic for identical inputs. Wall-clock budgets can change when behavior observes frame-varying input and therefore MUST be explicitly enabled and reported by diagnostics.

## Root completion

The tree instance exposes the root's terminal result only after all required lifecycle callbacks and command writes for that result complete. Restart behavior is host-configured and occurs on a later update; a terminal root is never implicitly re-entered in the same update.
