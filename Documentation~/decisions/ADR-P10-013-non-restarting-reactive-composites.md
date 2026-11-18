# ADR P10-013: Reactive composites that keep their running child

- Status: Accepted (2026-11-17). Proposed the same day; the owner accepted it with all nine
  recommendations of §6 (see "Owner decisions" below) and approved stage 1 of §5, including the
  specification edits of §2.1. Stage 2 (native executor and equivalence) and stage 3 (IL2CPP
  measurement) were approved and done on 2026-11-18 and are in the owner's review; the fixes of the
  independent code review are task P10-014 (`Planning~/Evidence/P10-014/README.md`).
- Decision ID: AIBT-048
- Task: `Planning~/Tasks/P10/P10-013-non-restarting-reactive-composites.md`
- Origin: finding F2 of P10-011 (`Planning~/Evidence/P10-011/README.md` §7, "F2"), confirmed in
  numbers by P10-011 §5.1 and P10-012.

In this document "v1 reactive" means today's `aibt.core.reactive-sequence` and
`aibt.core.reactive-selector`. "Retaining reactive" means the proposed new composites. File
references are relative to the package root (`Assets/AIBT`) at `1dd7dd0`.

## 1. Problem

### 1.1 What the specification says today

- `ReactiveSequence`: "If it has a running child from the previous update, that subtree is aborted
  and exited before reevaluation begins. It may then be re-entered if selected again."
  (`Documentation~/specifications/execution-semantics-v1.md:52`). The selector has the same rule
  (`:73`). "No candidate child is entered while the previous running subtree is still active"
  (`:57`, `:78`).
- `reference-executor-machine-v1.md:44-48` ("Reactive replacement") calls this restart "the Phase 1
  contract", chosen so that reactive composites "avoid speculative side effects": the old branch is
  fully exited before any child is evaluated again.

So by contract a v1 reactive composite never lets a child stay `Running` across two updates. The
child is aborted and, if selected again, entered anew.

### 1.2 What the code does (verified by reading)

Native executor (`Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs`):

- At the start of every update `TryBeginUpdate` calls
  `if (!ScheduleExpiredTimeout(ref control)) ScheduleReactiveReplacement(ref control);` (`:368`).
- `ScheduleReactiveReplacement` (`:1749-1762`) finds the shallowest `Running` frame whose kind is
  `ReactiveSequence` or `ReactiveSelector` and requests an abort of everything below it with
  `AbortReason = BurstNodeAbortReason.Explicit` (`:1758`) and `ReactiveResetPending = 1`.
- After the subtree is aborted, `TryAdvance` performs the `ReactiveReset` step: it writes the
  composite's cursor back to 0 and counts one semantic step (`:401-418`). Evaluation then restarts
  at child 0.
- The kind is chosen by type ID only: `NativeReactivePolicyV1.Matches` compares the numeric type ID
  with two constants (`Runtime/Execution/Native/Composites/Reactive/NativeReactivePolicyV1.cs:5-15`).
  The record's `NodeTypeVersion` exists (`Runtime/Compiled/Native/NativeProgramContracts.cs:298`)
  but no kind classifier reads it (also `NativeHotReloadInstance.ClassifyKind`,
  `Runtime/Execution/Native/HotReload/NativeHotReloadInstance.cs:48-62`).

Reference executor (`Runtime/Execution/Reference/Core/ReferenceExecutionMachine.cs`):

- When a new update opens, `PrepareRequestedReevaluationForNewUpdate`, then
  `PrepareExpiredTimeoutForNewUpdate`, then `PrepareReactiveResetForNewUpdate` run (`:557-558`).
- `PrepareReactiveResetForNewUpdate` (`:1433-1460`) marks the shallowest reactive frame not yet
  reset in this update and starts an abort traversal of its subtree with `NodeAbortReason.Explicit`.
- `AdvanceReactiveComposite` (`:1324-1350`) then resets the cursor to 0 and continues like a memory
  composite. The handlers themselves are the memory-sequence/-selector tables
  (`Runtime/Execution/Reference/Composites/Reactive/ReferenceReactiveCompositeHandlers.cs:132-146`),
  registered for type version 1 only (`:63-78`).

Both executors therefore implement the specification exactly. F2 is the specified behaviour, not a
bug.

### 1.3 Consequences

**External actions (simulation container), verified by a test and a benchmark.**
`Tests/Integration/Simulation/TreeSimulationActionTests.cs:928-956`
(`UnderAReactiveSelector_TheJobRestartsEveryThink_AndACompletionIsNeverConsumed`, in place and with
jobs) pins it. The tree is `repeater → memory-sequence [reactive-selector [alarm, job], wait 2]`
(`:39-50`). At every think the job is aborted, which the container reports as
`ActionCancelReason.Preempted` because the abort reason is `Explicit`
(`Runtime/Simulation/SimulationNativeV1.cs:511-517`), and a new action is started. A completion
reported by the game wakes the agent, and that think aborts the job before it can read the
completion, so a completion is never consumed. In the IL2CPP benchmark `act-reactive16` this is
200 starts, 200 `Preempted` cancels and 100 completions per tick, none consumed, no orders
(`Planning~/Evidence/P10-011/README.md` §5.1, "`act-reactive16`, finding F2 in numbers").

**Any multi-update leaf, verified by reading the code (no executed test exists yet).**
`aibt.stdlib.wait` keeps its count in `Activation` memory and sets it to 0 in `Enter`
(`Authoring/Registry/Generated/BuiltInLeaves/Runtime/BuiltInLeafNodes.cs:16-41`, `Elapsed = 0`), then
`Tick` adds one and succeeds when `Elapsed >= Ticks` (`:43-50`). Activation memory is also zeroed
before every `Enter` (`execution-semantics-v1.md:30`). Under a v1 reactive composite the wait is
aborted and re-entered at every update (§1.2), so `Elapsed` never exceeds 1. A `wait` with
`ticks >= 2` that is reached on every update therefore never succeeds; with `ticks: 1` it succeeds on
its first tick and is unaffected (which is why the benchmark trees, whose waits are all `ticks: 1`,
did not show it). The same holds for every leaf whose progress lives in activation memory or starts
in `Enter`. Stage 1 of the plan (§5) adds a test that pins this for v1.

**Decorators, inferred from the specification text (not verified by code or test).**
`Timeout` captures its deadline at `Enter` (`execution-semantics-v1.md:100`). Re-entered at every
update, its deadline moves forward each time, so under a v1 reactive composite a timeout over a
multi-update child fires only if one gap between updates exceeds its duration. `Cooldown` with
`on-enter` records the next deadline before child `Enter` (`:101`); re-entered on the next update it
is still inside its cooldown and returns its blocked result, ending the branch after one update.

**Cost, measured.** With 23 job workers a `Step` of 500 agents costs 238 µs on `io-reactive16` and
61 µs on `io-loop16`; with 0 workers 2582 µs and 92 µs
(`C:\UnityProjects\_aibt_acceptance\perf-data\P10-012\sim-trace-table.md`, rows "0 traced").
**Most of this gap is not caused by the restart.** `io-reactive16` is a reactive sequence of 15
children, 14 of which complete at once (`sense` and thirteen `wait` with `ticks: 1`) and the last of
which (`hold`) stays `Running` (`Benchmarks~/Phase10/PlayerPerf/Assets/AibtPerf/Trees/io-reactive16.aibt.json`).
Re-evaluating the 14 guards at every think is what the owner's decision keeps. By the atomic-step
definition (`reference-executor-machine-v1.md:13-20`) each instantly completing child costs about five
steps (select, Enter, Tick, Exit, accept), so a think is about 75 steps of guards plus 3 for the
restart of `hold` (abort, exit, reset). The fix removes the restart part only: an estimated 5–10 % on
`io-reactive16`, not a return to `io-loop16` levels. On `act-reactive16` the fix removes the
`Preempted` cancels and the restarts and lets completions be consumed; P10-011 §5.1 attributes the
1.96× `Step` cost of `act-reactive16` over `io-reactive16` to the extra wake-up thinks, which remain
(a consumed completion still wakes its agent). These are estimates; stage 3 measures them.

### 1.4 Abort observers today

- The specification defines observers (`None`, `Self`, `LowerPriority`, `Both`) only for a condition
  child of a reactive composite (`execution-semantics-v1.md:117-131`). The validator and the compiler
  enforce this by comparing the parent's type ID with the two v1 reactive IDs
  (`Authoring/Validation/TreeValidator.cs:835-842`,
  `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs:644-648`).
- The reference executor implements them: a watched Tree-scope write queues the observer, the
  condition is re-evaluated, and a changed result resets the owning composite through the same
  reactive-reset path with reason `ObserverSelf` or `ObserverLowerPriority`
  (`ReferenceExecutionMachine.cs:2085-2232`).
- In the native executor `NativeLifecycleMachineV1.TryApplyObserverTransition` (`:578-632`) does the
  same, but its only callers are tests (`Tests/Integration/NativeRuntime/NativeBehaviorCaseAdapter.cs:298`,
  `Tests/Runtime/NativeExecution/LifecycleAndMemory/NativeLifecycleMachineTests.cs:373`,
  `Tests/Runtime/NativeExecution/ParallelAndDecorators/NativeParallelAndDecoratorPolicyTests.cs:460`).
  No production host drives observers.
- Under v1 an observer only adds a reaction *inside* an update, because every update already resets
  the composite.

### 1.5 Something noticed while reading (not part of F2)

In an update in which a `Timeout` expires, neither executor schedules the reactive reset: native
`if (!ScheduleExpiredTimeout(ref control)) ScheduleReactiveReplacement(ref control);` (`:368`),
reference `:557-558`. The timeout's result is then accepted by the reactive composite at its current
cursor, without re-evaluation from child 0 in that update. This reads as a deviation from "on every
eligible update" (`execution-semantics-v1.md:52`). It is read from code only, not tested, and this ADR
does not change it (v1 must keep its behaviour). It is listed as open question 8.

## 2. Decision: new semantics

Add two new composites, a **retaining reactive sequence** and a **retaining reactive selector**.
They re-evaluate from child 0 on every eligible update, like v1, but keep the running child instead
of restarting it. The running child is aborted only when an earlier child takes over or ends the
composite, or when the composite itself is aborted from above. v1 composites are not changed.
§3 explains why they are new type IDs; the proposed IDs are
`aibt.core.reactive-sequence-retaining` and `aibt.core.reactive-selector-retaining` (open question 2).

The text below is written as normative text for `execution-semantics-v1.md` and
`reference-executor-machine-v1.md`. "MUST" and "MAY" follow `conventions.md`.

### 2.1 Normative text: retaining reactive sequence and selector

> **Retaining reactive sequence.** `RetainingReactiveSequence` evaluates its children in semantic
> order. On the update in which it is entered it behaves exactly like `MemorySequence`. On every
> later eligible update in which it still has a running child `r` (the *retained child*), it first
> performs a *guard pass* over children `0 … r-1` and only then continues `r`.
>
> - Each child `i < r` is evaluated as a new activation (`Enter`, `Tick` until a result, `Exit`)
>   in semantic order. While this happens the retained child `r` and its descendants stay active
>   and receive no callback.
> - A guard that returns `Success` lets the pass continue with child `i + 1`.
> - A guard that returns `Failure` ends the sequence: the retained branch is aborted deepest-first
>   (`Abort`, then `Exit(Aborted)` per node), and then the sequence exits with `Failure`.
> - A guard that returns `Running` becomes the new running child: the retained branch is aborted
>   deepest-first, and the sequence stays `Running` with child `i` as its running child.
> - When every child `0 … r-1` has returned `Success`, the sequence *resumes* `r`. Resuming is not an
>   activation: `r` is not entered again and keeps its activation memory. Execution continues in
>   `r`'s branch as it would under a `MemorySequence`, and a leaf in it is ticked at most once in
>   this update.
> - A result of `r` is accepted as in `MemorySequence`: `Success` continues with `r + 1` in the same
>   update, `Failure` fails the sequence, `Running` keeps `r` retained.
>
> An empty retaining reactive sequence succeeds.
>
> **Retaining reactive selector.** `RetainingReactiveSelector` follows the same rules with the
> roles of the results exchanged: in the guard pass a child returning `Failure` lets the pass
> continue, a child returning `Success` ends the selector with `Success` after the retained branch
> is aborted, and a child returning `Running` takes over as above. A higher-priority child (lower
> index) therefore preempts the retained child only when it actually succeeds or runs; when every
> higher-priority child fails, the retained child continues. `r` failing continues with `r + 1` in
> the same update. An empty retaining reactive selector fails.
>
> **Common rules.**
>
> 1. A guard pass happens at most once per activation of the composite per eligible update. It does
>    not happen in the update in which the composite is entered, and resuming a budget-suspended
>    update does not repeat it (`reference-executor-machine-v1.md`, "Budget").
> 2. Abort of a retained branch because of a guard uses reason `Explicit`, and its source node is the
>    guard child whose result caused it. The guard's own `Enter` and `Tick` (and `Exit`, when it
>    completed) precede the retained branch's `Abort`. This is the one ordering in which a candidate
>    child is entered while the previous running subtree is still active; it applies only to
>    retaining reactive composites. The v1 rule "No candidate child is entered while the previous
>    running subtree is still active" is unchanged for v1 reactive composites.
> 3. When the composite itself is aborted (by an ancestor, a `Timeout`, a parallel completion, a
>    tree stop or hot reload), its running child is aborted as for any composite. If the abort
>    arrives during a guard pass, the active guard branch is aborted first, then the retained
>    branch, each deepest-first.
> 4. Children after `r` are evaluated only after `r` completes, as in a memory composite.
> 5. A leaf in a guard position is entered, ticked and exited on every eligible update, including any
>    command it emits. Authors MUST NOT place a leaf whose `Enter` or `Tick` starts work that is
>    expensive or not idempotent in a guard position. (This is unchanged from v1, where the same leaf
>    is re-run on every update.)

> **Update-start processing with retaining composites** (new paragraph for
> `reference-executor-machine-v1.md`). At the start of an eligible update the executor considers the
> active path from the root. Let `R` be the shallowest retaining reactive composite on that path
> that has a retained child. The existing update-start rules (an expired `Timeout`, else v1 reactive
> replacement, each shallowest first) apply unchanged to the frames from the root down to `R`. They
> do not look into `R`'s retained branch. If none of them aborts `R` or an ancestor of `R`, the
> update continues with `R`'s guard pass. When `R` resumes its retained child, the same update-start
> rules are applied to the retained branch down to the next retaining composite with a retained
> child, which then performs its guard pass, and so on. A path without retaining composites is
> processed exactly as today. Restoring a suspended `Parallel` branch applies the same processing to
> the restored frames, as it applies today's rules
> (`NativeLifecycleMachineV1.cs:1092`, `ReferenceExecutionMachine.cs:989-992`).

> **Atomic steps** (additions to `reference-executor-machine-v1.md`, "Atomic node steps", item 3).
> Starting a guard pass is one composite transition. Resuming the retained child after a successful
> guard pass is one composite transition. Each guard's selection, lifecycle callbacks and acceptance
> count exactly as for any other child. Budget suspension MAY occur between any of these steps; it
> preserves the retained branch, the guard cursor and any active guard branch.

### 2.2 Interactions

| Construct | Behaviour with a retaining composite |
|---|---|
| **Memory composites** | Unchanged. A memory composite inside a retained branch keeps its cursor; it does not re-check its earlier children. To re-check a guard deeper down, use a retaining composite there too. |
| **v1 reactive inside a retained branch** | Still restarts its own running child whenever the update reaches it (update-start processing above). Mixing is allowed and does what each node says. |
| **v1 reactive above a retaining composite** | The v1 reset at update start aborts the retaining composite with everything below it, as today. |
| **Parallel** | A retaining composite inside a parallel branch performs its guard pass when the branch is restored in the update. A `Parallel` used as a guard child is a new activation on each update; if it returns `Running` it takes over. Parallel completion aborts a retaining child like any running child. |
| **Repeater** | Unchanged. A retaining composite as the repeated child is a new activation per iteration, so the first update of each iteration has no guard pass. A repeater as the retained child keeps its iteration count. |
| **Timeout** | A `Timeout` above `R` is checked first (update-start rule) and aborts `R` with reason `Timeout`. A `Timeout` inside the retained branch keeps its deadline, because it is not re-entered; it is checked when the walk reaches it after `R`'s guard pass. If a guard takes over first, the timeout's subtree is aborted with reason `Explicit`, not `Timeout`. A `Timeout` in a guard position is re-entered each update, as in v1. |
| **Cooldown** | A `Cooldown` in the retained branch is not re-entered, so `on-enter` records its deadline once per real activation. A `Cooldown` in a guard position is evaluated each update like any guard. |
| **Budget suspension** | Only between atomic steps. The retained branch is neither aborted nor exited by suspension. A resumed update does not start a second guard pass. |
| **Root completion** | Unchanged (`execution-semantics-v1.md:139-141`): the root's result is exposed after root `Exit`. A retaining composite as root that ends because of a guard exposes its result after the retained branch was aborted and exited and the composite exited. |
| **Step limit** (`MaximumStepsPerThink`) | Counts the guard-pass steps like any other steps. |
| **Hot reload** | Hot reload runs between updates (`TryQueueHotReloadChildReset` rejects an open update, `NativeLifecycleMachineV1.cs:537-542`), so no guard pass is ever in progress (P10-014: `NativeHotReloadStateMigration.TryMigrate` now also refuses an open update). A retaining composite is a composite for the cursor-reset rule (ADR-P5-001 item 2). In the first update after a structural edit that resets a composite's cursor, update-start processing is replaced by that reset in both executors, so no retaining composite on the active path, above or below the reset owner, has a guard pass in that update; the guard passes resume in the next update (recorded by P10-014, non-normative). Changing a node between v1 and retaining is a type change and is classified `Incompatible`, which restarts the smallest enclosing subtree (`ADR-P5-001-hot-reload-compatibility-model.md:53-61`). |

### 2.3 Trace records

No new record kind is needed and `trace-v1.md` keeps its list (`trace-v1.md:9-21`). Composite
transitions are not traced today, in either executor; the native recorder deliberately drops the
`ReactiveReset` step (`Runtime/Scheduling/NativeTraceRecorderV1.cs:58`, `:91`). What a trace shows:

- **Retained child kept:** guard records (`NodeEntered`, `NodeTicked`, `NodeExited` of each guard),
  then `NodeTicked` of the retained leaf **without** a preceding `NodeAbortStarted`/`NodeEntered`
  for it. In v1 every update shows `NodeAbortStarted(Explicit)`, `NodeExited(Aborted)`,
  `NodeEntered` for the running branch.
- **Preemption:** the guard's records, then `NodeAbortStarted` with reason `Explicit` and source =
  the guard, then `NodeExited(Aborted)` for each node of the retained branch, deepest first.
  As implemented (recorded by P10-014): the reference trace writes the guard as the source; the
  production native trace (`NativeTraceRecorderV1`) writes no source for any abort, retaining or
  v1, so a native preemption record carries reason `Explicit` without a source node. The native
  machine keeps the guard in its control state (`HasAbortSource`/`AbortSourceNode`), read only by
  the equivalence tests. The trace format is unchanged.
- **Simulation container:** a preemption is reported as `Cancelled` with `Preempted`, as today
  (`SimulationNativeV1.cs:511-517`); a kept action produces no event until it is completed and
  consumed.

### 2.4 Abort observers

Recommendation: **keep the definitions unchanged, do not allow observers under the new composites in
this change** (validation error), and revisit once a production host drives observers.

- With the guard pass, a retaining composite already reacts at every update, which is what F2 needs.
  An observer would only add a reaction in the middle of an update.
- The native production path has no observer driver (§1.4). Allowing observers under the new nodes
  would add work in the validator, the compiler, both executors and the equivalence tests that no
  production tree can use.
- If allowed later, the natural definition is: a triggered `Self`/`LowerPriority`/`Both` transition
  starts an immediate guard pass of the owner in the current update. It is not a spurious restart,
  because it only fires on the result change that the next guard pass would act on anyway.
- The validator rule becomes: an observer's parent MUST be a v1 reactive composite; `lower-priority`
  and `both` require the v1 reactive selector. The diagnostic text names the reason ("retaining
  reactive composites do not support observers yet").

This is open question 3.

### 2.5 Worked examples

Notation: `E` Enter, `T→S/F/R` Tick returning Success/Failure/Running, `X` Exit, `A` Abort,
`X(Ab)` Exit(Aborted). One row per eligible update (a "think" in the simulation container).

**Example 1 — simulation container: start, think while running, completion consumed.**
Tree (as `CrewTree`, `TreeSimulationActionTests.cs:39-50`): `loop → memory-seq [top, hold]`,
`top = selector [alarm, job]`. `job` starts an external action in its first tick and succeeds when
it reads a completion.

| Think | Game input | v1 `reactive-selector` | retaining selector |
|---|---|---|---|
| 1 | alarm down | `top` E; `alarm` E T→F X; `job` E T→R, **Started A1** | same |
| 2 | — | `job` A, X(Ab), **Cancelled A1 Preempted**; `alarm` E T→F X; `job` E T→R, **Started A2** | guard pass: `alarm` E T→F X; resume `job` T→R (no event) |
| — | game completes the running action | `TryComplete(A2)` accepted, wakes agent | `TryComplete(A1)` accepted, wakes agent |
| 3 | — | `job` A, X(Ab), **Cancelled A2 Preempted** (completion not consumed); `alarm` F; `job` E T→R, **Started A3** | `alarm` E T→F X; resume `job` T→S (**completion consumed**), `job` X; `top` X→S; `hold` E T→R |

**Example 2 — simulation container: preemption by the alarm, and the same-step race.**
Same tree; the retaining case starts at think 1 of example 1.

| Think | Game input | v1 | retaining selector |
|---|---|---|---|
| 2 | alarm rises | `job` A, X(Ab), Cancelled A1 Preempted; `alarm` E T→S X; `top` X→S | `alarm` E T→S X; then `job` A, X(Ab), **Cancelled A1 Preempted**; `top` X→S |
| 2' | alarm rises **and** the game completed A1 before this step | as above | as above: the guard runs before `job` is ticked, so preemption wins and the completion is not consumed (`TryComplete` afterwards is `StaleAction`), the property today's test pins (`TreeSimulationActionTests.cs:944`, `:954`) |

Note the order difference: under v1 `job` is aborted before `alarm` is evaluated; under the
retaining selector `alarm`'s records come first and name the cause of the abort.

**Example 3 — a multi-update leaf: `sequence [ok, wait(ticks: 3)]`.**

| Update | `ok` | v1 `reactive-sequence` | retaining sequence |
|---|---|---|---|
| 1 | S | seq E; `ok` E T→S X; `wait` E (`Elapsed=0`) T (`1`)→R | same |
| 2 | S | `wait` A X(Ab); `ok` E T→S X; `wait` E (`0`) T (`1`)→R | `ok` E T→S X; resume `wait` T (`2`)→R |
| 3 | S | as update 2: `Elapsed` is 1 again — **never succeeds** | `ok` E T→S X; `wait` T (`3`)→S X; seq X→S |
| 3 (alt.) | F | `wait` A X(Ab); `ok` E T→F X; seq X→F | `ok` E T→F X; `wait` A X(Ab); seq X→F |

**Example 4 — selector takeover by a running higher-priority branch.**
`selector [flee, patrol]`, `flee = memory-seq [alarm, run]`, `run` and `patrol` multi-update actions.

| Update | alarm | v1 | retaining selector |
|---|---|---|---|
| 1 | down | `flee` E, `alarm` F, `flee` X→F; `patrol` E T→R | same |
| 2 | down | `patrol` A X(Ab); `flee` → F; `patrol` E T→R (restarted) | guard: `flee` E, `alarm` F, `flee` X→F; resume `patrol` T→R |
| 3 | up | `patrol` A X(Ab); `flee` E, `alarm` S, `run` E T→R | guard: `flee` E, `alarm` S, `run` E T→R; **takeover**: `patrol` A X(Ab) |
| 4 | down | `run` A X(Ab), `flee` X(Ab); `flee` E, `alarm` F → F; `patrol` E T→R | `run` is the retained branch, child 0: no guards; resume `run` T→R. `flee` is a memory sequence and does not re-check `alarm`; make `flee` a retaining sequence if it should |

**Example 5 — the pitfall: two multi-update children in one sequence.**
`sequence [cond, stepA(wait 2), stepB(wait 2)]`, `cond` always S. This is the only pattern where
retaining semantics still restarts work.

| Update | v1 | retaining sequence |
|---|---|---|
| 1 | `cond` S; `stepA` E T(1)→R | same |
| 2 | `stepA` A X(Ab); `cond` S; `stepA` E T(1)→R — `stepA` never finishes | `cond` S; resume `stepA` T(2)→S X; `stepB` E T(1)→R |
| 3 | as 2 | guard pass over `cond`, `stepA`: `stepA` is a guard now, E T(1)→R, **takes over**; `stepB` A X(Ab) |
| 4 | as 2 | `cond` S; resume `stepA` T(2)→S X; `stepB` E T(1)→R — and so on, `stepB` never finishes |

"Re-evaluate from child zero on every update" means every child before the running one is a guard.
Multi-step work belongs in one child, for example `retaining-seq [cond, memory-seq [stepA, stepB]]`.
The node documentation ("discouraged use") and `simulation-container.md` must say so (open
question 6).

**Example 6 — `Timeout` over a long action.** `sequence [ok, timeout(3 s, failure) [wait(1000)]]`,
one update per second, `ok` always S.

| Time | v1 | retaining sequence |
|---|---|---|
| 0 s | `timeout` E (deadline 3 s); `wait` E T→R | same |
| 1–2 s | everything aborted and re-entered; deadline becomes 4 s, 5 s, … — **never fires** | `ok` S; resume: `timeout` deadline not reached; `wait` T→R |
| 3 s | as before | `ok` S; walk reaches `timeout`: deadline reached → `wait` A(`Timeout`) X(Ab); `timeout` X→F; seq X→F |

## 3. Versioning options

What the code allows, in short: **one version per type ID**, everywhere.

- `NodeRegistryBuilder.Build` rejects a second manifest with the same type ID: a different version
  is `AIBT3002` `IncompatibleVersions` (`Authoring/Registry/NodeRegistryBuilder.cs:118-126`,
  `NodeRegistryDiagnostics.cs:8`, `:29-33`).
- `NodeRegistry` indexes entries by type ID and by numeric type ID only
  (`Authoring/Registry/NodeRegistry.cs:97-104`, `:116-130`).
- The validator requires `typeVersion` to equal the one registered manifest's version
  (`Authoring/Validation/TreeValidator.cs:488-493`, `UnsupportedNodeVersion`).
- The numeric type ID is FNV-1a of the type string; the version is not part of it
  (`Documentation~/specifications/identity-and-hashing-v1.md:24`).
- `BurstNodeGenerator` rejects two manifests with the same type ID in the complete catalog
  (`CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs:863-866`).
- Native kind classification ignores the version (§1.2).

### (a) `typeVersion: 2` of the existing two types

| Area | Effect |
|---|---|
| Registry, validator | v1 and v2 cannot both be registered (`AIBT3002`). Keeping old trees working would need a multi-version registry: new keys in `NodeRegistry`, `TreeValidator`, MCP/editor catalogs, `NodeManifestCanonicalJson` ordering, the authoring compiler. Otherwise v1 is replaced, every existing tree fails validation with `UnsupportedNodeVersion`, and old trees do **not** keep their behaviour. |
| P7-028 freeze, byte parity | The authority (`Runtime/Nodes/Contracts/RuntimeBuiltInCatalogAuthority.cs:385-466`, hash `:651`) must change. With both versions in it the generator rejects the catalog ("duplicate or incompatible node identity", `BurstNodeGenerator.cs:865`) — the generator would need multi-version support too. |
| Native executor | `NativeReactivePolicyV1`, `ClassifyKind` and every caller of it (`ProductionTreeAgentCore.cs:187`, `GeneratedTreeDispatchAdapterV2.cs:99`, `GeneratedTreeRuntimeDefinitionV2.cs:57`, `SimulationProgramMapV1.cs:119`, hot reload) classify by type ID only. A v2 node that reached the native executor without changes there would silently run v1 semantics. Every classifier must start reading `NodeTypeVersion`. |
| Compiled format | Records already carry `NodeTypeVersion`; no format change. |
| ABI v2 file | No change. |
| Hot reload | v1↔v2 is a version change → `Incompatible` → subtree restart. Fine. |
| Docs / MCP catalog / graph editor | Catalogs list one entry per type ID; showing two versions needs a version-aware palette and catalog. |
| Existing assets | Untouched only if the multi-version registry is built. |

Verdict: not feasible as a small change. It is either a multi-version registry project across
authoring, codegen and native classification, or it is option (d) with a version number.

### (b) New type IDs (recommended)

Proposed: `aibt.core.reactive-sequence-retaining`, `aibt.core.reactive-selector-retaining`.

| Area | Effect |
|---|---|
| Registry, validator | Two new built-in manifests in `BuiltInNodeManifests` (`Authoring/Model/Nodes/BuiltInNodeManifests.cs:20-33`). No registry mechanism changes. `aibt.core.` is the only built-in namespace for structural composites (`NodeRegistryBuilder.cs:216-226`). |
| P7-028 freeze, byte parity | The authority's JSON and hash are regenerated to contain 13 instead of 11 `aibt.core.` entries. The mechanism allows this: the generator only requires every authority entry to be `aibt.core.` (`BurstNodeGenerator.cs:803-810`, check at `:805`) and rejects *shards* that claim `aibt.core.` (`:858-861`); the new composites have no shard, they are executed by `NativeLifecycleNodeKindV1` like the 11. The byte-parity test (`RuntimeBuiltInCatalogAuthorityVerifier.cs:50-60`, `RuntimeBuiltInCatalogAuthorityTests`) keeps holding after regeneration. **But** the P7-028 card states the namespace is "permanently reserved for the original 11" (`Planning~/Tasks/P7/P7-028-production-node-library.md:108-113`). That sentence must be amended by the owner (open question 1). |
| Registry hash | The registry hash changes and is part of every compiled program's content hash (`Authoring/Compilation/CompiledContentHasher.cs:47`, `Runtime/Compiled/Native/CompiledProgramContentHashV1.cs:34`), and of every catalog set's handshake hash (`burst-node-abi-v2.md`, "Implicit Runtime built-in metadata authority"). Committed fixtures that pin such hashes are regenerated (precedent: P8-003, `Tests/Fixtures/P2/CodeGen/generated-tree-v2.aibt.json`). Programs compiled before the change are recompiled on import as usual; this already happened when P7-028 added `aibt.stdlib.*`. |
| Native executor | Two new values appended to the internal `NativeLifecycleNodeKindV1` (`NativeLifecycleMachineV1.cs:8-22`), two IDs in `NativeReactivePolicyV1` and `ClassifyKind`, a binding rule in `ValidBinding` (`:1695-1719`). Internal only. |
| Compiled format | No change: new type IDs in existing records. `compiled-program-v1.md:63` lists the built-in type IDs and gets two more. |
| ABI v2 file | No change, if preemption keeps reason `Explicit` (open question 4). `BurstNodeAbortReason` is in `Tests/Editor/CodeGen/Contracts/ExpectedPublicAbiV2.txt:269-283`; reactive composites are not. |
| Hot reload | `ClassifyKind` and `IsCompositeKind` (`NativeHotReloadStateMigration.cs:316-323`) learn the new kinds. v1↔retaining edit = type change = `Incompatible` = subtree restart. |
| Docs / MCP catalog | `Documentation~/generated/node-catalog.md` regenerated; MCP and editor catalogs read the registry (`Editor/Graph/NodeSearchProvider.cs:34`, `Editor/Registry/ProjectNodeCatalog.cs`). `TreeGroup.Debug.CoreTypeNames` (`Runtime/Simulation/TreeGroup.Debug.cs:436-441`) gets two names. |
| Graph editor | New palette entries from the registry; no editor code beyond that. |
| Existing assets and samples | Unchanged and keep v1 behaviour. Authors opt in per node. |

### (c) A parameter on the existing nodes (e.g. `retainRunningChild: boolean`)

| Area | Effect |
|---|---|
| Specification | Breaks "Sequence, selector … have no parameters in Phase 1" and "These names and values are persisted contracts" (`execution-semantics-v1.md:113-115`). |
| Manifests, freeze | The v1 manifests' canonical bytes change, so the authority JSON and hash change exactly as in (b), plus the existing two entries change meaning. |
| Behaviour | One type with two behaviours; both executors branch on configuration. Today the reactive composites have no configuration (`ValidBinding` checks memory only, `NativeLifecycleMachineV1.cs:1715-1719`); a configuration field and its packing must be added. An absent parameter would have to mean "v1". Readers of a tree (people, MCP, LLM authors) must notice a flag to know which semantics apply. |
| Hot reload | Flipping the flag changes configuration only; same type, version and memory layout classify as `Compatible` and migrate, so a running composite would switch semantics in the middle of an activation. Needs an extra rule. |
| Existing assets | Unchanged. |

Verdict: possible, but it changes the frozen v1 manifests and a normative "no parameters" rule, and
hides a semantic switch in a flag.

### (d) Change v1 in place

Smallest code change, but every existing tree changes behaviour: golden behaviour cases
(`Tests/Fixtures/Golden/Cases/patrol-react.aibtcase.json`), `ReactiveComposite*Tests`,
`ReferenceObserverMachineTests`, the Swarm Arena sample (`Samples~/SwarmArena/Trees/swarm-arena.aibt.json`),
the benchmark baselines and the simulation tests. It contradicts the owner's requirement that old trees
keep their behaviour and AGENTS.md ("Breaking changes require a migration and a documented
decision"). Rejected.

### Recommendation

**(b) new type IDs.** It is the only option that keeps every existing tree, manifest byte for v1,
compiled-format version and the ABI v2 file unchanged, and needs no new registry mechanism. Its cost
is a regenerated authority and registry hash (routine since P7-028) and the owner's amendment of the
"original 11" sentence. The owner's assumed plan, `typeVersion: 2`, does not work in this codebase
without a multi-version registry (§3 (a)).

## 4. Impact

Sizes: S < 50 lines, M 50–300, L > 300 (production code or text, tests counted separately).

| Area | Files | Change | Size |
|---|---|---|---|
| Specifications | `execution-semantics-v1.md` (two sections, interactions, observer restriction); `reference-executor-machine-v1.md` (guard pass, update-start processing, atomic steps); `compiled-program-v1.md:63` (two IDs) | §2.1 text | M |
| Manifests and authority | `BuiltInNodeManifests.cs` (+2, memory 8 bytes/4 align `Activation`: retained index and guard cursor), `RuntimeBuiltInCatalogAuthority.cs` (regenerated JSON + hash) | | S + generated |
| Validator and compiler | `TreeValidator.cs:835-842`, `GeneratedCompiledProgramV2.cs:644-648` (observer owners stay v1 only, clearer diagnostic) | | S |
| Reference executor | `ReferenceReactiveCompositeHandlers.cs` (kind, bindings), `ReferenceExecutionMachine.cs` (guard pass, parking the retained branch, takeover, update-start walk), `ReferenceFrame.cs` | the bulk of the semantics | L |
| Native executor | `NativeLifecycleMachineV1.cs` (kinds, `ValidBinding`, update-start walk, guard pass, parking/restoring the retained branch, takeover abort), `NativeReactivePolicyV1.cs`, `NativeInstanceArenaOwner.cs`/capacity (room for a parked branch) | the risky part | L |
| Kind classification | `NativeHotReloadInstance.ClassifyKind`, `NativeHotReloadStateMigration.IsCompositeKind`, `TreeGroup.Debug.CoreTypeNames` | | S |
| Managed agent layer | `Agents/Runtime/ManagedTreeInstance.cs:123`, `:433` (registry includes the new handlers; comment) | | S |
| Authoring preview | `Authoring/Execution/ReferencePreviewFixtureEnvironment.cs:44` (uses `CreatePhase1BuiltIns`, picks the new bindings up) | | S |
| Tests | see §5 | | L |
| Docs | `node-catalog.md` (generated), `simulation-container.md` ("Not under a reactive composite", `:224-230`), `performance.md`, `README.md:267`, `CHANGELOG.md`, MCP recipes if they recommend reactive composites | | M |
| Benchmarks | `Benchmarks~/Phase10/PlayerPerf/Tools/gen_trees.py` (`act-retain16`, `io-retain16`), `PerfRunner.cs`, `PerfBuild.cs`, `README.md` | | S |
| Fixtures pinning hashes | `Tests/Fixtures/P2/CodeGen/generated-tree-v2.aibt.json` and any test that pins a registry or content hash | regenerate | S |

**Untouched:** v1 manifests and handlers; v1 behaviour in both executors (old trees run the same
callbacks in the same order and the same step counts); `ExpectedPublicAbiV2.txt`; the compiled
format version; the trace format; the package version (until the release decision); existing
`.aibt.json` trees, samples, golden cases and benchmark trees; the simulation container API.

## 5. Implementation plan

Each stage ends with a commit in the submodule, evidence under `Planning~/Evidence/P10-013/` and the
owner's acceptance before the next stage.

### Stage 1 — specification text, manifests, reference executor, behaviour cases

- Spec edits from §2.1 (separately approved, since normative specs change only by decision).
- Manifests, authority regeneration, validator/compiler observer rule.
- Reference executor: guard pass, retained branch, takeover, update-start walk.
- Tests to add:
  - v1 pins first (before any production change): `wait(ticks: 3)` under a v1 reactive sequence
    never succeeds; `Timeout` over a long child under v1 never fires; `Cooldown(on-enter)` over a
    long child blocks on the second update. They protect v1 from accidental change.
  - Reference behaviour tests for examples 1–6 (exact callback order), budget suspension at every
    step of a guard pass (results equal to unlimited budget), nested retaining composites, a
    retaining composite inside a `Parallel`, a v1 reactive inside a retained branch and vice versa,
    abort from above during a guard pass, empty composites, root completion.
  - Behaviour cases (`behavior-case-v1.md`) for examples 1, 3, 4, 5, 6 in
    `Tests/Fixtures/Golden/Cases/`.
  - Validator: observer under a retaining composite is rejected; manifest contract tests
    (`NodeManifestContractTests`), authority parity (`RuntimeBuiltInCatalogAuthorityTests`).
- Acceptance: all new tests pass; all existing AIBT test assemblies equal the baseline (v1 traces
  unchanged); `Verify-Static.ps1` passes; `ExpectedPublicAbiV2.txt` byte-identical.

### Stage 2 — native executor and equivalence

- Native guard pass, retained-branch parking (a short spike decides between reusing the `Parallel`
  suspended-frame storage and stacking guard frames above the retained branch with an explicit
  parent index), arena capacity, kind classification, hot reload.
- Tests to add: the stage-1 behaviour cases through `NativeBehaviorCaseAdapter` (reference and
  native traces equal); `JobsPolicyEquivalenceTests` with retaining trees (Immediate, Jobs,
  JobsAgentFrame frame-by-frame equal); hot reload v1↔retaining (`Incompatible` restart) and a
  structural edit under a retaining composite (cursor reset); zero managed allocation per tick;
  capacity overflow reported, not corrupting.
  Simulation: a new test next to the F2 test, with the retaining selector: action started once,
  completion consumed, `Done` command emitted, preemption by the alarm cancels with `Preempted`,
  same-step completion and alarm → preemption wins. The existing F2 test stays and keeps pinning v1.
- Acceptance: all of the above in place and with jobs; Web single-thread backend runs the behaviour
  cases (no core behaviour compiled out); baseline unchanged otherwise.

### Stage 3 — IL2CPP measurement

- New benchmark trees `act-retain16` and `io-retain16`: identical to `act-reactive16`/`io-reactive16`
  except the root type. Same harness as P10-011 §5.1 / P10-012 (500 agents, `Spread(5)`, 23 and 0
  workers, 6 runs, ABBA order), plus the v1 trees in the same runs as control.
- Acceptance (numbers are targets from §1.3, to be confirmed or explained):
  - `act-retain16`: 0 `Preempted` cancels in steady state; consumed completions equal completions
    minus those in flight; orders > 0; `Step` E/B ratio against `io-retain16` ≤ 1.2 (v1: 1.96 and
    2.11).
  - `io-retain16`: `Step` median not above `io-reactive16` (expected ≈ 5–10 % lower; the remaining
    gap to `io-loop16` is guard re-evaluation and is expected).
  - v1 trees within run-to-run noise of the P10-012 numbers (238/2582 µs) — no regression.
  - 0 bytes managed allocation per tick inside `Step`.

### Risks and mitigations

| Risk | Mitigation |
|---|---|
| Native parking of the retained branch is the first time two branches of one composite are live; frame capacity and abort order bugs. | Spike in stage 2; reuse the `Parallel` suspended-frame machinery, which already solves storage and deepest-first abort; capacity tests; reference-vs-native trace equality on every behaviour case. |
| v1 behaviour changes by accident (shared code paths in `TryBeginUpdate`, `PrepareReactiveResetForNewUpdate`). | v1 pin tests land before production code; all existing reactive tests and golden cases must stay byte-identical; the update-start walk is a no-op on paths without retaining composites. |
| Authors expect "reactive" to re-check guards *inside* the running branch's memory composites. | Docs, node "discouraged use" text, example 4 in the documentation. |
| Example 5 pitfall (two multi-update children in a sequence). | Documented; open question 6 on a validator warning. |
| Guards with side effects run every update. | Same as v1; spelled out in the node text and the spec (common rule 5). |
| Registry/content hash change surprises a consumer with pinned hashes. | CHANGELOG entry; fixtures regenerated in stage 1; same as P7-028. |
| Parallel AIBT work in the same submodule conflicts in `NativeLifecycleMachineV1.cs`. | One writer at a time on the executors; stages are separate commits. |

## 6. Open questions for the owner

1. **Versioning.** Use new type IDs (option (b)) instead of `typeVersion: 2`? This needs the
   P7-028 sentence "`aibt.core.` is permanently reserved for the original 11" to read "reserved for
   structural composites and decorators executed by the native lifecycle machine; adding one needs an
   ADR". *Recommendation: yes. `typeVersion: 2` cannot coexist with v1 without building
   multi-version support in the registry, validator, generator and native executor.*
2. **Names.** *Recommendation: `aibt.core.reactive-sequence-retaining` and
   `aibt.core.reactive-selector-retaining`*, display names "Reactive sequence (keeps running child)"
   and "Reactive selector (keeps running child)". They sort next to the v1 nodes in catalogs and say
   what differs. Alternatives: `aibt.core.guarded-sequence` / `aibt.core.priority-selector`.
3. **Observers under the new nodes.** *Recommendation: not allowed for now (clear validation error);
   revisit when a production host drives observers.* The guard pass already gives a reaction every
   update.
4. **Abort reason of a preemption.** *Recommendation: reuse `Explicit`* (the simulation container
   already maps it to `Preempted`). A new `BurstNodeAbortReason` value would change the public ABI v2
   file.
5. **v1 nodes.** Keep them as they are, without deprecation, and steer new trees through docs and the
   new nodes' "use when" text? *Recommendation: yes; do not edit the v1 manifests' text, because
   that changes their canonical bytes and the authority hash for no behavioural gain.*
6. **Pitfall of example 5.** Document only, or add a validator warning for a retaining sequence
   with an `Action` before its last child? *Recommendation: document only.* A warning by behaviour
   kind would also flag composite children that are fine.
7. **Benchmarks.** Add `act-retain16`/`io-retain16` instead of changing the existing trees?
   *Recommendation: add*, so P10-011/P10-012 numbers stay comparable.
8. **The v1 observation in §1.5** (no reactive reset in an update in which a `Timeout` expires).
   *Recommendation: pin it with a test as a separate small task and decide then; do not change v1 in
   this work.*
9. **Expectation on cost.** The fix removes restarts and makes actions under reactive composites
   work; it does not bring `io-reactive16` close to `io-loop16`, because re-evaluating 14 guards per
   think is the requested behaviour. *Recommendation: accept the stage-3 targets in §5 as the
   measure of success.*

## Owner decisions (2026-11-17)

The owner accepted every recommendation of §6:

1. Versioning: new type IDs, option (b). The P7-028 sentence now reads "reserved for structural
   composites and decorators executed by the native lifecycle machine; adding one needs an ADR"
   (`Planning~/Tasks/P7/P7-028-production-node-library.md`).
2. Names: `aibt.core.reactive-sequence-retaining` and `aibt.core.reactive-selector-retaining`,
   display names "Reactive sequence (keeps running child)" and "Reactive selector (keeps running
   child)".
3. Observers under the new nodes are a validation error for now.
4. Preemption keeps abort reason `Explicit`; `ExpectedPublicAbiV2.txt` does not change.
5. v1 nodes stay untouched and are not deprecated; their manifest bytes do not change.
6. The pitfall of example 5 is documented only; no validator warning.
7. New benchmark trees (`act-retain16`, `io-retain16`) in stage 3; existing trees unchanged.
8. The v1 observation of §1.5 (no reactive reset in an update in which a `Timeout` expires) is not
   touched by this work.
9. Success is measured by the stage-3 targets of §5.

Stages 1, 2 and 3 are done (stage 1 on 2026-11-17, stages 2 and 3 on 2026-11-18); their results and
any deviation from this document are recorded in `Planning~/Evidence/P10-013/README.md`, the review
fixes in `Planning~/Evidence/P10-014/README.md`.
