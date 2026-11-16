# ADR P10-002: Jobs stage J2 — one job advances an agent through its whole frame

- Status: Accepted by owner direction for P10-004 (changes 1, 2, 3(a), 4 and 5 below); implemented
  in P10-004 as the opt-in policy `SchedulingPolicy.JobsAgentFrame` (2026-11-16)
- Decision ID: not assigned
- Evidence: `Planning~/Evidence/P10-002/README.md`, spike `Spikes~/P10-002-J2/`,
  implementation `Planning~/Evidence/P10-004/README.md`

## Context

`BatchedJobsSameFrame` runs a frame as rounds. In each round, every open agent's lifecycle machine
advances exactly one step in a job. The main thread then completes the job, reads every result and
resolves every `DispatchRequired` through managed generated dispatch. The next round starts after
that. The P10-002 frame profile was measured in the IL2CPP non-development Player with
`AIBT_SCHEDULER_FRAME_PROFILE` and covers 1000 agents (µs per frame, median):

| Scenario | Policy | Round overhead¹ | Dispatch | Frame total | Rounds | Dispatches |
|---|---|---:|---:|---:|---:|---:|
| n-io-wait1 | Immediate | — | 2 858 (HostDispatch) | 3 656 | — | 1 000 |
| n-io-wait1 | BatchedJobsSameFrame | 1 534 | 2 146 (RoundResolve) | 4 407 | 2 | 1 000 |
| n-shared-loop16 | Immediate | — | 6 936 | 8 255 | — | ≈2 344 |
| n-shared-loop16 | BatchedJobsSameFrame | 4 738 | 5 477 | 11 026 | 5.6 | 2 344 |
| n-shared-reactive16 | Immediate | — | 121 539 | 133 031 | — | 46 000 |
| n-shared-reactive16 | BatchedJobsSameFrame | 64 592 | 104 299 | 174 285 | 77 | 46 000 |

¹ RoundAllocate + RoundSchedule + RoundComplete + RoundDispose: work J1 removes or reduces.

The table supports two conclusions.

- **Dispatch dominates both policies.** Dispatch is 70–90% of the frame in both. Almost all of it
  is the managed framing around the Burst body, not the node body itself:
  - activation generation and memory clear,
  - tree-value snapshot and commit,
  - workspace lease,
  - command publication,
  - trace and effect capture.
- **J1 cannot make Jobs faster than Immediate.** After J1 removes the round overhead, a Jobs frame
  still costs about the same as Immediate. Each round still returns to the main thread, and every
  dispatch still runs there, one after another.

## Proposed design

J2 adds a new opt-in policy, working name `JobsAgentFrame`. One Burst `IJobFor` work item runs one
agent's whole update:

1. It advances the machine.
2. At each `DispatchRequired`, it executes the generated node body in place: native framing, then
   the catalog's Burst switch.
3. It continues until `Waiting` or `Completed`.

The main thread keeps the following, in agent order:

- the world bridge and fact feeding,
- due-agent collection,
- one schedule and one complete per group,
- trace drain,
- effect delivery,
- status and restart bookkeeping.

Building blocks, and whether they exist today:

| Block | Today | J2 needs |
|---|---|---|
| Multi-step advance in one job | Machine is a struct over NativeArrays; one step per job | Loop to a barrier in the job (the spike checks it) |
| Generated node body | Static `[BurstCompile] ExecuteImmediate(ref BurstExecutionBatch)` with a generated switch, reached through the managed `IGeneratedBurstCatalogExecutorV2` | Codegen emits a per-catalog agent-frame job, or a Burst function pointer (the spike checks it) |
| Dispatch framing | Managed (`GeneratedTreeDispatchAdapterV2.DispatchSlot.Execute`) | Native equivalent of every framing step, in the same order |
| Traces | `NativeTraceRecorderV1`, written on the main thread | Per-agent native buffers written in the job; the trace writer lease already supports `TryRegisterDependency` |
| Effects | Captured per dispatch, consumed on the main thread into the managed `ProductionTreeEffectQueue` | Per-agent native buffers, delivered after `Complete` in agent order, then emission order |
| Async completions | Not wired in the facade (group dispatch passes an empty set) | Unchanged; the policy is not selectable for trees with async leaves until they are wired |

## Changes it would need (owner decisions)

None of these is made by P10-002.

1. **New policy value.** A new `SchedulingPolicy`/profile entry (public API, additive). The defaults
   stay unchanged. Making the automatic selector pick J2 later would change a default.
2. **Generator output.** Every catalog gains an emitted agent-frame job, or a function-pointer entry.
   - Regenerated shards change. ABI v2 (`ExpectedPublicAbiV2.txt`) only stays unchanged if the job
     is internal or lives outside `AIBT.Burst`.
   - Consumers must regenerate their catalogs.
3. **Shared-scope blackboard visibility.** In Immediate, and in today's rounds (dispatch on the main
   thread, in agent order), agent *i* sees agent *i−1*'s shared writes within the same frame.
   Parallel whole-frame jobs cannot keep that. The options are:
   - (a) J2 refuses trees that write shared or world scope; they fall back to the current policy.
     No semantic change.
   - (b) Double-buffered shared writes, visible next frame. This is a semantic change.
4. **Managed leaves and managed tree-value readers.** Trees with managed or async leaves, or managed
   bridges, cannot run in a job. They fall back, and the selection reason is reported as a new
   diagnostic (additive).
5. **Runaway trees.** A tree that completes instantly under a large repeater loops inside one update.
   The runtime has no per-update step limit when `StepBudget` is null; see the evidence.
   - Today this blocks the main thread.
   - Under J2 it would block a worker, and then the main thread's `Complete`.
   - J2 needs a per-update step guard, a new diagnostic and failure. This is a diagnostics change
     for the owner to decide, and it is also useful for Immediate.
6. **Failure text.** Failures inside the job are codes only. Exception-based diagnostics from managed
   dispatch cannot happen there, so diagnostic messages for those cases would differ.

## Gain estimate

This is an estimate from the profile, not a measurement.

- **Main thread at N = 1000, `n-io-wait1`.** The main-thread work per frame is world bridge, due
  collection and bookkeeping (about 180 µs). Add one schedule and complete, plus trace and effect
  drain at about 0.3–0.5 µs per agent: **about 0.5–0.8 ms**, against Immediate 3.66 ms and
  Jobs 4.41 ms. That is 4.5–7× lower main-thread time.
- **Worker CPU.** The Burst node bodies plus native framing should cost well below today's managed
  framing. Wall-clock gain then scales with the worker count.
- **Reactive16.** It dispatches 46 000 times per 1000 agents. It gains the most in absolute terms.
  Its tree has no blackboard keys ("shared" in the benchmark names means one shared program
  shape), so option 3(a) admits it.

**Note after P10-003 (stage F1).** F1 made the existing main-thread framing cheaper, so the baseline
J2 is compared with moved: `n-io-wait1` Immediate at N = 1000 is now about 1.97 ms per frame
(was 3.66–3.75 ms), and `shared-reactive16` 54 ms (was 128 ms). The 0.5–0.8 ms J2 estimate above is
unchanged, so J2's main-thread gain over Immediate shrinks from 4.5–7× to about 2.5–4× for
`n-io-wait1`. Most of the remaining cost per dispatch (≈1.2–1.7 µs) is now the node body and the
bridge running as IL2CPP code (`ExecuteImmediate` is not Burst on the main thread); a J2 job would
run the same bridge Burst-compiled. A generator-emitted Burst `ExecuteImmediate` would reach part
of that gain without J2 (estimated up to ≈40% of a single-host dispatch, not measured;
`Planning~/Evidence/P10-003/README.md`, section 2.4). This note changes no decision; the status
stays Proposed.

## Risks

- **Native dispatch framing.** Re-implementing it is the bulk of the work. Equivalence has to be
  proved by `JobsPolicyEquivalenceTests`, extended to the new policy.
- **Function pointers.** A function pointer to a method that returns a struct by value may not
  compile; the spike records the outcome. The fallback is a codegen-emitted job per catalog.
- **Safety system.** Nested native containers inside the machine struct need
  `[NativeDisableContainerSafetyRestriction]` on an `UnsafeList`. Race detection for per-agent
  state is then lost and must rest on the one-agent-per-work-item invariant.
- **Error isolation.** A Burst job cannot catch exceptions. A bug in a node body corrupts the agent
  silently instead of producing a managed diagnostic.

## Outcome (P10-004)

- **Body entry: emitted job.** Every catalog emits a private `[BurstCompile]` agent-frame job; its
  executor implements `AIBT.AgentFrame.IGeneratedAgentFrameExecutorV1`. A function-pointer variant
  (body reached through `BurstCompiler.CompileFunctionPointer`, result through a `ref` parameter)
  compiled and ran in the IL2CPP Player and was within −4%…+3% per dispatch, so the simpler
  emitted job was kept. `ExpectedPublicAbiV2.txt` is unchanged.
- **Framing.** Every framing step has a native equivalent in the same order
  (`AgentFrameCoreV1`); traces and effects go to per-agent buffers replayed in agent order.
  Cross-request ledger floors (a corruption check) are not tracked in the job.
- **Shared writes: option 3(a).** No semantic change. The generated adapter already refuses
  Shared-scope bindings and async completions, so for generated hosts the fallback reasons
  `SharedOrWorldScopeWrite` and `AsyncOrManagedLeaf` are defensive only. The reachable reasons are
  `ManagedDispatch`, `StepBudgetConfigured` and `CatalogWithoutAgentFrameEntry`.
- **Step limit.** `ProductionTreeHost.MaximumStepsPerUpdate`, default 1 000 000, for Immediate,
  BatchedJobsSameFrame and JobsAgentFrame, failing with `NativeUpdateStepLimitExceeded`.
- **Failure text (change 6).** The host maps job outcomes to the same messages as Immediate.
- **Measured gain** (IL2CPP, 23 workers, interleaved): main-thread time 2.1–2.2× lower than
  Immediate for `io-wait1`, 2.9–3.5× for `shared-loop16`, 2.2–2.3× for `io-move1-fact-effect`,
  10.7× for `shared-reactive16`. The estimate above (0.5–0.8 ms for `io-wait1` at N = 1000) was
  close: measured 0.92 ms. Per dispatch inside the job: 0.48–1.17 µs on one thread.

## Spike evidence

`Spikes~/P10-002-J2/README.md` (Editor 6000.7.0b1, Burst on, safety checks on, 2026-11-15):

- **Multi-step advance.** One `IJobFor` advancing every machine to its next barrier produced the same
  step kinds per agent as one-step-per-job rounds, for N = 64, 1000 and 5000.
- **Cost.** The test tree needs 9 steps (9 rounds) to its first barrier. One-step rounds took
  54.8–60.3 ms at N = 1000 and 224–297 ms at N = 5000; the single multi-step job took 0.36–0.65 ms
  and 0.42–0.55 ms. These are Editor numbers with the pre-P10-002 round owner, so they show the
  shape (the round trip is the cost), not Player timings.
- **Function pointer.** `BurstCompiler.CompileFunctionPointer` on a catalog's generated
  `ExecuteImmediate` compiled. It was not invoked, so calling it from a job (struct return, IL2CPP)
  is unverified; a codegen-emitted per-catalog job stays the fallback.
- **Not covered.** Dispatch framing, trace and effect writing inside a job, and Player timings.
