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

## Trace points

Trace records are emitted after their corresponding atomic effect. A terminal leaf Tick is followed by Exit before the parent acceptance transition. Root result is exposed only after root Exit and command writes complete.
