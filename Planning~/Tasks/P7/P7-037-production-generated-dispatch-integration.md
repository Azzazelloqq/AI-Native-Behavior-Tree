# P7-037 — Production generated-dispatch lifecycle integration

Status: `Draft`

## Objective

Connect normally compiled behavior-tree instances to generated Burst catalog dispatch in production.
The bridge must execute real custom-node callbacks through both immediate and scheduled facades and
return their results to the existing lifecycle machine without reflection, caller-authored layout
offsets or precomputed node statuses.

This is the prerequisite for P7-033's population Jobs policies and the Swarm Arena sample. It does
not choose scheduling policy, define profiles or allocate a global frame budget.

## Depends on

- `P2-012` — closed native dispatch ABI v2.
- `P4-003` — same-frame and pipelined scheduler ownership.
- `P6-022`/`P7-009` — generated-metadata translation proof and production authoring translator.
- `P7-030` — complete single-tree production lifecycle host.
- `P7-032` — recoverable native scheduler ownership.

## Required reading

- `Documentation~/decisions/ADR-P6-022-generic-native-dispatch-test-harness.md` and
  `Planning~/Evidence/P7-009/README.md`; their translator proves metadata materialization for an
  authoring/MCP test harness, not production host integration.
- `Authoring/Compilation/Generated/GeneratedCompiledProgramV2.cs` and
  `GeneratedBurstDispatchPrebindingV2.cs`.
- `Runtime/Execution/Burst/Dispatch/`, especially dispatch ABI/workspace ownership.
- `Runtime/Execution/Native/Core/NativeLifecycleMachineV1.cs` and
  `Runtime/Integration/ProductionTreeHost.cs`.
- Generated catalog facade contracts and P7-032's same-frame/pipelined recovery evidence.

## Mandatory proof before production promotion

Build the smallest disposable end-to-end proof first:

1. Compile a real custom Burst node and a tree through the normal authoring pipeline.
2. Carry the generated catalog handshake and canonical layout into Player-safe runtime data without
   reflection or hand-authored offsets.
3. Execute one complete lifecycle update through the generated immediate facade.
4. Execute the same update through the generated scheduled facade and a real `JobHandle`.
5. Verify the same observable node status, lifecycle callbacks, memory, blackboard writes and
   reduced commands.

If this proof reveals a new public semantic choice, record a focused decision before widening the
API. Mechanical public names remain subject to the existing API-diff review.

## Allowed changes

- `Runtime/Integration/GeneratedDispatch/`.
- Focused additions to `Runtime/Execution/Burst/Dispatch/` needed for runtime-safe ownership.
- `Authoring/Compilation/Generated/` for compiler-produced runtime bootstrap data.
- `CodeGen~/AIBT.CodeGen/` only if the generated facade lacks a required closed-ABI entry point.
- Focused Runtime/Editor tests and `Planning~/Evidence/P7-037/`.

## Forbidden changes

- Do not use `SchedulingPolicyDriver` or caller-supplied/precomputed node statuses as production
  dispatch.
- No reflection in Player, managed fallback in a Burst path, per-node hardcoded offsets, or one Job
  per tree/node.
- No scheduler profile, priority, policy-selection, global-budget or sample-gameplay work owned by
  P7-033/P7-034.
- No new behavior-node semantics or promotion of sample nodes into the standard library.

## Acceptance criteria

- A normally compiled tree containing a real generated custom node runs through production-owned
  immediate and scheduled dispatch with identical observable lifecycle results.
- Runtime bootstrap rejects catalog/program/layout mismatches with a structured diagnostic before
  execution and never guesses a compatible layout.
- Enter, Tick, Exit and Abort callbacks reach the generated implementation when declared; terminal,
  failure and cancellation paths preserve the lifecycle contract.
- Blackboard/snapshot/command ownership follows existing deterministic publication rules.
- Multiple instances share catalog metadata while retaining isolated mutable state.
- Outstanding scheduled work can complete, reject invalid output and dispose/recover through the
  P7-032 ownership contract.
- Hot paths allocate no managed memory after warmup and leak no native ownership.

## Required verification

```text
Verify-Static.ps1
focused immediate/scheduled parity and mismatch/recovery tests
Run-UnityTests.ps1 -Mode EditMode -Scope Full
public API diff and generated documentation checks when the public surface changes
live Unity scheduled-Job proof using a normally compiled generated custom node
allocation measurement after warmup
git diff --check
```

## Handoff notes

P7-033 may start only after this card proves the real path required by its Jobs policies. P7-034 and
P7-035 must consume this public production path rather than a benchmark or test-only adapter.
