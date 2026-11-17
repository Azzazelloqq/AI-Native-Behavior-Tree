# Reference executor machine v1

This contract makes Phase 1 execution and budgeting executable without defining the Phase 2 public node ABI.

## Machine state

A tree instance owns an explicit stack of frames. A frame stores runtime node index, lifecycle state, activation generation, handler program counter, active-child data, and node-memory range. No C# call stack or recursive coroutine is persisted across an update.

Internal lifecycle states are `Inactive`, `Entered`, `Running`, `TerminalPendingExit`, and `Aborting`. Public root observations remain only `Success`, `Failure`, or `Running`; when no terminal result has been exposed the host observes no root result, not an `Inactive` node status.

## Atomic node steps

One budget step is exactly one of:

1. invoke `Enter` and emit `NodeEntered`;
2. invoke one leaf `Tick` and emit `NodeTicked`;
3. perform one composite/decorator transition, including selecting or accepting one child result;
4. invoke `Abort` and emit `NodeAbortStarted`;
5. invoke `Exit` and emit `NodeExited`;
6. evaluate one queued observer condition and emit `ObserverEvaluated`.

Entering and first ticking therefore require separate steps even when both occur in one execution pass. A callback is never split. Child execution occurs in its own frame and is never hidden inside one parent callback.

Retaining reactive composites (ADR-P10-013) add to item 3: starting a guard pass is one composite
transition. Resuming the retained child after a successful guard pass is one composite transition.
Each guard's selection, lifecycle callbacks and acceptance count exactly as for any other child.
Budget suspension MAY occur between any of these steps; it preserves the retained branch, the guard
cursor and any active guard branch.

## Budget

- Budget `0` performs no step and returns an internal suspended outcome.
- A positive budget decrements after each completed atomic step.
- Unlimited budget runs until root terminal, `Running` waiting on a future update, or an error.
- Exhaustion preserves all frames, memory, command stream, and cursors and emits one `BudgetYielded` trace event. Resume emits `ExecutionResumed` before the next semantic step; trace bookkeeping does not consume steps.
- Budget suspension is not an eligible update and cannot cause reactive reevaluation, time advancement, or another leaf Tick with unchanged update ID.

The executor returns an envelope containing progress (`Completed`, `Waiting`, or `Suspended`), optional public root result, steps executed, and diagnostics. This envelope is executor control state, not node status.

## Eligible update

Each host update has an update ID, immutable snapshot revision, time point, normalized completion/event input, and budget. A leaf that returned `Running` is ticked at most once per eligible update unless its manifest explicitly defines an internal immediate loop, which Phase 1 built-ins do not.

Resuming a budget-suspended update continues the same update ID and frozen inputs. Starting a new update while suspended or executing the same instance reentrantly is rejected.

## Root and reset

After root Exit, the instance retains the terminal result and has no active frames. `Restart` is explicit, is rejected while active or suspended, clears per-node memory and tree slots according to their reset contracts, increments activation generation safely, and makes the root eligible only for a later update.

## Reactive replacement

Reactive composites avoid speculative side effects. On each eligible update they first abort and exit the previously running child subtree, deepest-first, then evaluate children from index zero. A previously selected child may consequently be re-entered. This deterministic restart behavior is the Phase 1 contract.

For a reactive sequence, children are evaluated until Failure or Running; for a reactive selector, until Success or Running. The newly selected branch is entered only after the old branch has fully exited. This rule replaces any interpretation that requires discovering a new child's result before aborting the old branch.

## Retaining reactive composites

`aibt.core.reactive-sequence-retaining` and `aibt.core.reactive-selector-retaining` follow
`execution-semantics-v1.md`, "Retaining reactive sequence and selector". The rules of "Reactive
replacement" above apply only to the v1 reactive composites.

**Update-start processing with retaining composites.** At the start of an eligible update the
executor considers the active path from the root. Let `R` be the shallowest retaining reactive
composite on that path that has a retained child. The existing update-start rules (an expired
`Timeout`, else v1 reactive replacement, each shallowest first) apply unchanged to the frames from
the root down to `R`. They do not look into `R`'s retained branch. If none of them aborts `R` or an
ancestor of `R`, the update continues with `R`'s guard pass. When `R` resumes its retained child, the
same update-start rules are applied to the retained branch down to the next retaining composite with
a retained child, which then performs its guard pass, and so on. A path without retaining composites
is processed exactly as today. Restoring a suspended `Parallel` branch applies the same processing to
the restored frames, as it applies today's rules.

**Retained branch.** During the guard pass the retained branch stays active and is not on the path
being executed: its frames keep their state and receive no callback. The steps of one guard pass are,
in order: start (one transition, also when the retained child is child `0` and the pass is empty);
for each guard, selection, the guard's own steps and acceptance; then exactly one of the following.

- *Resume* (one transition) after the last guard was accepted without ending the pass. The update-start
  rules are then applied to the retained branch as described above.
- *End.* The acceptance that ends the composite starts the abort of the retained branch. The branch is
  aborted deepest-first with reason `Explicit` and the guard child as source; afterwards the
  composite's terminal result is recorded (no extra step) and the composite exits as usual.
- *Takeover.* When the guard branch has returned `Running` and execution would leave the composite's
  subtree (the update would wait, or an enclosing `Parallel` would suspend the branch), the retained
  branch is first aborted deepest-first with reason `Explicit` and the guard child as source. The
  update then continues exactly as it would have after the guard returned `Running`. A `Parallel`
  inside the guard branch suspending one of its own branches does not leave the composite's subtree.

An abort that reaches the composite while a branch is retained aborts the active guard branch first,
then the retained branch, then the composite, as one deepest-first traversal; no extra step is spent
on moving between the branches.

## Trace points

Trace records are emitted after their corresponding atomic effect. A terminal leaf Tick is followed by Exit before the parent acceptance transition. Root result is exposed only after root Exit and command writes complete.
