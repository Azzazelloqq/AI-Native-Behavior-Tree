# P10-002 J2 spike

This folder holds throwaway evidence for the J2 ADR
(`Documentation~/decisions/ADR-P10-002-agent-frame-jobs.md`). It is not package code.
`J2SpikeTests.cs` ran once from `Tests/Integration/NativeRuntime/` (assembly
`AIBT.Integration.Tests`) on 2026-11-15, in Editor 6000.7.0b1 with Burst on and safety checks on.
It was then moved here, so it no longer compiles. To run it again, copy it back into
`Tests/Integration/NativeRuntime/`. It needs `JobsEquivalenceCatalog` from
`JobsPolicyEquivalenceTests.cs`.

## Questions and answers

1. **Can one Burst job advance a machine through several lifecycle steps up to a barrier, and do
   the steps match the current one-step rounds?** Yes.
   - The job is an `IJobFor` over an `UnsafeList<NativeLifecycleMachineV1>` with
     `[NativeDisableContainerSafetyRestriction]`. A barrier is `DispatchRequired`, `Waiting` or
     `Completed`.
   - It ran under the Editor's safety checks without errors.
   - For every agent, the step kinds were identical to those of the current one-step-per-job rounds
     (`NativeBatchedLifecycleOwnerV1`). The test asserts this per agent, for N = 64, 1000 and 5000.
2. **What does it cost?** The test tree is a repeater over a parallel with selectors. It reaches its
   first barrier after 9 steps: 4 × (CompositeEntered, ChildSelected), then DispatchRequired.

   | N | Rounds (one step each) | One-step rounds | One multi-step IJobFor |
   |---:|---:|---:|---:|
   | 64 | 9 | 3.5–4.8 ms | 0.74–0.97 ms |
   | 1000 | 9 | 54.8–60.3 ms | 0.36–0.65 ms |
   | 5000 | 9 | 224–297 ms | 0.42–0.55 ms |

   These are two Editor runs. The rounds still create an owner per round, which is the
   pre-P10-002 path; they are Editor numbers, not Player numbers. The ratio shows that the
   round-trip per step, not the machine advance, is the cost.
3. **Can a runtime job reach a catalog's generated `ExecuteImmediate` through a Burst function
   pointer as it is?** `BurstCompiler.CompileFunctionPointer` on
   `JobsEquivalenceCatalog.ExecuteImmediate` returned a created pointer ("compiled: True").
   - The pointer was not invoked.
   - Whether calling it from a job with a struct return value works in IL2CPP has not been verified.
   - The ADR keeps a codegen-emitted per-catalog job as the fallback.

## Not covered

- Native dispatch framing (snapshot and commit, workspace lease, command publication) inside a job.
- Trace or effect writing from a job.
- Player (IL2CPP) timings for the spike.
