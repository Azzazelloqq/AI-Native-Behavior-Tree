# Platform backends v1

## Capability model

Backends are selected from runtime capabilities, allowed latency, workload, and user policy. Platform preprocessor symbols may provide defaults but MUST NOT be the public scheduling abstraction.

Capabilities include worker jobs, Burst AOT, WASM32, same-frame completion, pipelining, deterministic step budgeting, wall-clock budgeting, and managed/main-thread integration.

Forcing an unavailable policy produces a structured diagnostic. It does not silently select a semantically different latency mode.

## Native backend

Windows x64 and Android ARM64 are mandatory pre-1.0 validation targets. Supported native policies are:

- `Immediate`;
- `BatchedJobsSameFrame`;
- `PipelinedJobs` when explicitly allowed;
- `Budgeted` with step or wall-clock budget;
- `Auto` selecting among allowed policies.

Unity Job System owns threads. AIBT owns grouping, schedule policy, batching, budgets, and diagnostics.

## Web backend

Unity Web is a mandatory pre-1.0 target for supported desktop browsers. Mobile-browser support is not claimed unless Unity and AIBT validate it separately.

The Web backend is functionally equivalent but single-threaded for C# execution:

- `SingleThreadImmediate`;
- `SingleThreadBudgeted`;
- `Auto`, which selects one of those policies.

It uses the same semantic model, compiled program, node contracts, blackboard, commands, tests, and trace format. It MUST preserve zero GC allocations after initialization for the supported unmanaged path.

The backend divides agents and node steps across frames when budgeted. Browser tab throttling and frame-varying input are recorded as latency factors. `BatchedJobsSameFrame` and `PipelinedJobs` are unavailable unless a future verified Unity capability changes this decision.

## Required Web spike

Before implementing the production Web backend, measure a minimal representative program using:

1. unmanaged immediate execution;
2. `IJob.Run` where supported;
3. a Burst-compatible direct entry point where supported.

Build with the repository's exact Unity/Burst/Collections versions. Record build compatibility, Burst WASM behavior, allocations, native memory, code size, node throughput, frame-budget behavior, exceptions, and Chrome/Firefox/Safari results. The spike selects an implementation through an accepted decision; it does not alter semantic contracts.

## Unity references

- [Unity 6 Web technical limitations](https://docs.unity3d.com/6000.0/Documentation/Manual/webgl-technical-overview.html)
- [Unity 6 Web Player settings](https://docs.unity3d.com/6000.0/Documentation/Manual/class-PlayerSettingsWebGL.html)
- [Unity 6 Web performance considerations](https://docs.unity3d.com/6000.0/Documentation/Manual/webgl-performance.html)
