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

## Retaining reactive sequence and selector

Added by `Documentation~/decisions/ADR-P10-013-non-restarting-reactive-composites.md`. The type IDs
are `aibt.core.reactive-sequence-retaining` and `aibt.core.reactive-selector-retaining`. They are new
nodes beside `ReactiveSequence` and `ReactiveSelector`, whose rules above are unchanged. In this
section "v1 reactive composite" means `ReactiveSequence` or `ReactiveSelector`.

**Retaining reactive sequence.** `RetainingReactiveSequence` evaluates its children in semantic
order. On the update in which it is entered it behaves exactly like `MemorySequence`. On every
later eligible update in which it still has a running child `r` (the *retained child*), it first
performs a *guard pass* over children `0 … r-1` and only then continues `r`.

- Each child `i < r` is evaluated as a new activation (`Enter`, `Tick` until a result, `Exit`)
  in semantic order. While this happens the retained child `r` and its descendants stay active
  and receive no callback.
- A guard that returns `Success` lets the pass continue with child `i + 1`.
- A guard that returns `Failure` ends the sequence: the retained branch is aborted deepest-first
  (`Abort`, then `Exit(Aborted)` per node), and then the sequence exits with `Failure`.
- A guard that returns `Running` becomes the new running child: the retained branch is aborted
  deepest-first, and the sequence stays `Running` with child `i` as its running child.
- When every child `0 … r-1` has returned `Success`, the sequence *resumes* `r`. Resuming is not an
  activation: `r` is not entered again and keeps its activation memory. Execution continues in
  `r`'s branch as it would under a `MemorySequence`, and a leaf in it is ticked at most once in
  this update.
- A result of `r` is accepted as in `MemorySequence`: `Success` continues with `r + 1` in the same
  update, `Failure` fails the sequence, `Running` keeps `r` retained.

An empty retaining reactive sequence succeeds.

**Retaining reactive selector.** `RetainingReactiveSelector` follows the same rules with the
roles of the results exchanged: in the guard pass a child returning `Failure` lets the pass
continue, a child returning `Success` ends the selector with `Success` after the retained branch
is aborted, and a child returning `Running` takes over as above. A higher-priority child (lower
index) therefore preempts the retained child only when it actually succeeds or runs; when every
higher-priority child fails, the retained child continues. `r` failing continues with `r + 1` in
the same update. An empty retaining reactive selector fails.

**Common rules.**

1. A guard pass happens at most once per activation of the composite per eligible update. It does
   not happen in the update in which the composite is entered, and resuming a budget-suspended
   update does not repeat it (`reference-executor-machine-v1.md`, "Budget").
2. Abort of a retained branch because of a guard uses reason `Explicit`, and its source node is the
   guard child whose result caused it. The guard's own `Enter` and `Tick` (and `Exit`, when it
   completed) precede the retained branch's `Abort`. This is the one ordering in which a candidate
   child is entered while the previous running subtree is still active; it applies only to
   retaining reactive composites. The v1 rule "No candidate child is entered while the previous
   running subtree is still active" is unchanged for v1 reactive composites.
3. When the composite itself is aborted (by an ancestor, a `Timeout`, a parallel completion, a
   tree stop or hot reload), its running child is aborted as for any composite. If the abort
   arrives during a guard pass, the active guard branch is aborted first, then the retained
   branch, each deepest-first.
4. Children after `r` are evaluated only after `r` completes, as in a memory composite.
5. A leaf in a guard position is entered, ticked and exited on every eligible update, including any
   command it emits. Authors MUST NOT place a leaf whose `Enter` or `Tick` starts work that is
   expensive or not idempotent in a guard position. (This is unchanged from v1, where the same leaf
   is re-run on every update.)

**Interactions.**

- *Memory composites* are unchanged. A memory composite inside a retained branch keeps its cursor;
  it does not re-check its earlier children. To re-check a guard deeper down, use a retaining
  composite there too.
- *A v1 reactive composite inside a retained branch* still restarts its own running child whenever
  the update reaches it (update-start processing, `reference-executor-machine-v1.md`). Mixing is
  allowed and does what each node says.
- *A v1 reactive composite above a retaining composite* aborts the retaining composite with
  everything below it at update start, as for any running child.
- *Parallel.* A retaining composite inside a parallel branch performs its guard pass when the branch
  is restored in the update. A `Parallel` used as a guard child is a new activation on each update;
  if it returns `Running` it takes over. Parallel completion aborts a retaining child like any
  running child.
- *Repeater* is unchanged. A retaining composite as the repeated child is a new activation per
  iteration, so the first update of each iteration has no guard pass. A repeater as the retained
  child keeps its iteration count.
- *Timeout.* A `Timeout` above the retaining composite is checked first and aborts it with reason
  `Timeout`. A `Timeout` inside the retained branch keeps its deadline, because it is not
  re-entered; it is checked when the update reaches it after the guard pass. If a guard takes over
  first, the timeout's subtree is aborted with reason `Explicit`, not `Timeout`. A `Timeout` in a
  guard position is re-entered on each update, as under a v1 reactive composite.
- *Cooldown.* A `Cooldown` in the retained branch is not re-entered, so `OnEnter` records its
  deadline once per real activation. A `Cooldown` in a guard position is evaluated on each update
  like any guard.
- *Budget suspension* happens only between atomic steps. The retained branch is neither aborted nor
  exited by suspension. A resumed update does not start a second guard pass.
- *Root completion* is unchanged: a retaining composite as root that ends because of a guard exposes
  its result after the retained branch was aborted and exited and the composite exited.
- *Hot reload* runs between updates, so no guard pass is ever in progress. A retaining composite is
  a composite for the hot-reload cursor-reset rule. In the first update after a structural edit that
  resets a composite's cursor, the reset replaces update-start processing, so no retaining
  composite on the active path has a guard pass in that update. Changing a node between a v1
  reactive composite and a retaining one is a type change.

**Several multi-update children in one retaining sequence (informative).** Every child before the
running one is a guard. In `RetainingReactiveSequence [cond, stepA, stepB]` with multi-update
`stepA` and `stepB`, `stepA` becomes a guard once `stepB` runs, is re-entered on the next update,
returns `Running` and takes over, so `stepB` never finishes. Multi-step work belongs in one child,
for example `RetainingReactiveSequence [cond, MemorySequence [stepA, stepB]]`.

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

The parent of an observer MUST be a v1 reactive composite (`ReactiveSequence` or `ReactiveSelector`);
`LowerPriority` and `Both` require `ReactiveSelector`. An observer under a retaining reactive composite
is a validation error: retaining reactive composites do not support observers yet
(ADR-P10-013 §2.4). Their guard pass already re-evaluates every guard on each eligible update.

## Budget suspension

Budgets may suspend only between node steps. Suspension preserves activation and cursor state and does not call `Abort` or `Exit`. Budget suspension is internal and MUST NOT be stored as a node result. Exact steps and resume behavior follow `reference-executor-machine-v1.md`.

Step budgets are deterministic for identical inputs. Wall-clock budgets can change when behavior observes frame-varying input and therefore MUST be explicitly enabled and reported by diagnostics.

## Root completion

The tree instance exposes the root's terminal result only after all required lifecycle callbacks and command writes for that result complete. Restart behavior is host-configured and occurs on a later update; a terminal root is never implicitly re-entered in the same update.
